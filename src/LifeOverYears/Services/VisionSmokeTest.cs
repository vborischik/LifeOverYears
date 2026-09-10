// TODO: remove smoke test
using System.Text;
using System.Text.Json;
using LifeOverYears.Models;
using LifeOverYears.Providers;
using LifeOverYears.Services.Interfaces;
using Microsoft.Extensions.Logging;

namespace LifeOverYears.Services;

// TODO: remove smoke test
// Isolated smoke test for reading what the vision model says back. Drives the
// real VisionProvider against a fake INvidiaProvider, so every check exercises
// the shipping parse path without touching the network or an API key.
//
// This suite exists because both failures this provider has had were in exactly
// this code and both were silent: a PascalCase answer that the snake_case DTO
// read as all-nulls, and an empty stream that became a confident "unknown"
// scene type. Neither raised an error; both cost real runs. Everything here is
// deterministic — the fake returns scripted chunks — so it belongs in the
// offline suite, unlike the accuracy of what the model actually sees.
public static class VisionSmokeTest
{
    private const string Photo = "vision-smoke.jpg";

    public static async Task<int> RunAsync(ILoggerFactory loggerFactory, ILogger logger)
    {
        logger.LogInformation("[SmokeVision] VisionSmokeTest starting");

        var findings = new List<(string Id, string Desc, bool Pass, string Detail)>();
        var sandbox  = Path.Combine(Path.GetTempPath(), "loy-vision-smoke-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(sandbox);
        var photoPath = Path.Combine(sandbox, Photo);
        await File.WriteAllBytesAsync(photoPath, new byte[] { 1, 2, 3, 4 });

        try
        {
            await DoN1(loggerFactory, photoPath, findings);
            await DoN2(loggerFactory, photoPath, findings);
            await DoN3(loggerFactory, photoPath, findings);
            await DoN4(loggerFactory, photoPath, findings);
            await DoN5(loggerFactory, photoPath, findings);
            await DoN6(loggerFactory, photoPath, findings);
            await DoN7(loggerFactory, photoPath, findings);
            await DoN8(loggerFactory, photoPath, findings);
        }
        finally
        {
            try { Directory.Delete(sandbox, recursive: true); } catch { /* temp dir, best effort */ }
        }

        await WriteReport(findings, logger);

        var passed = findings.Count(f => f.Pass);
        var total  = findings.Count;
        Console.WriteLine();
        Console.WriteLine($"Vision smoke test: {passed}/{total} checks passed" +
                          (passed == total ? "" : " — FAILURES DETECTED"));
        Console.WriteLine("See output/smoke-vision/report.md for full details.");
        logger.LogInformation("[SmokeVision] Done: {Passed}/{Total} checks passed", passed, total);

        return passed == total ? 0 : 1;
    }

    // ── Checks ────────────────────────────────────────────────────────────────

    // The ordinary case, and the shape the vision prompt actually documents.
    private static async Task DoN1(
        ILoggerFactory lf, string photo, List<(string, string, bool, string)> f)
    {
        var errs = new List<string>();
        var (provider, _) = Build(lf, Chunks(SnakeCaseAnswer));

        var dna = await provider.AnalyzeImageAsync(photo, "prompt");

        if (dna.SceneType != "gas_station")      errs.Add($"scene_type read as '{dna.SceneType}'");
        if (dna.Geometry.Parking != "lot")       errs.Add($"parking read as '{dna.Geometry.Parking}'");
        if (!dna.Geometry.Sidewalks)             errs.Add("sidewalks read as false");
        if (dna.Geometry.Roads.Count != 1)       errs.Add($"{dna.Geometry.Roads.Count} roads, expected 1");
        if (dna.Geometry.Buildings.Count != 1)   errs.Add($"{dna.Geometry.Buildings.Count} buildings, expected 1");
        if (dna.Environment.Trees.Count != 1)    errs.Add($"{dna.Environment.Trees.Count} trees, expected 1");
        if (dna.Environment.Terrain != "urban")  errs.Add($"terrain read as '{dna.Environment.Terrain}'");
        if (dna.Distinctive is not { Count: 1 }) errs.Add("distinctive did not survive the parse");

        f.Add(("N1", "A snake_case answer parses into a complete SceneDna",
            errs.Count == 0, errs.Count == 0 ? "every field read back correctly" : string.Join("; ", errs)));
    }

    // The regression that mattered: the model answers in whatever casing the
    // conversation put in front of it, and "SceneType" does not match a
    // [JsonPropertyName] of "scene_type" however case-insensitive the options
    // are — an underscore is a different name, not a different case. This read
    // every field as null and stamped "unknown", which then looked like the
    // model failing to classify the photo.
    private static async Task DoN2(
        ILoggerFactory lf, string photo, List<(string, string, bool, string)> f)
    {
        var errs = new List<string>();
        var (provider, _) = Build(lf, Chunks(PascalCaseAnswer));

        var dna = await provider.AnalyzeImageAsync(photo, "prompt");

        if (dna.SceneType != "gas_station")
            errs.Add($"scene_type read as '{dna.SceneType}' — PascalCase keys were not normalized");
        if (dna.Geometry.Parking != "lot")     errs.Add($"parking read as '{dna.Geometry.Parking}'");
        if (dna.Geometry.Buildings.Count != 1) errs.Add("buildings lost");
        if (dna.Environment.Terrain != "urban") errs.Add($"terrain read as '{dna.Environment.Terrain}'");

        f.Add(("N2", "A PascalCase answer parses too — key casing is normalized, not trusted",
            errs.Count == 0, errs.Count == 0 ? "PascalCase read back identically to snake_case" : string.Join("; ", errs)));
    }

    // The other silent failure: the gateway returns 200 with one contentless
    // chunk, ExtractStreamedContent hands back "", and the old code let that
    // become the "unknown" stub. It must be retried and then thrown, because a
    // stub here is indistinguishable from a real classification downstream.
    private static async Task DoN3(
        ILoggerFactory lf, string photo, List<(string, string, bool, string)> f)
    {
        var errs = new List<string>();
        var (provider, fake) = Build(lf, new List<string> { """{"choices":[{"delta":{}}]}""" });

        try
        {
            var dna = await provider.AnalyzeImageAsync(photo, "prompt");
            errs.Add($"returned a SceneDna ('{dna.SceneType}') instead of throwing on an empty stream");
        }
        catch (InvalidOperationException ex)
        {
            if (!ex.Message.Contains("empty response", StringComparison.OrdinalIgnoreCase))
                errs.Add($"threw, but the message does not name the cause: {ex.Message}");
        }
        catch (Exception ex)
        {
            errs.Add($"threw {ex.GetType().Name}, expected InvalidOperationException");
        }

        // Retried, not given up on at the first blank: the failure is intermittent.
        if (fake.Calls < 2)
            errs.Add($"only {fake.Calls} attempt(s) — an empty stream must be retried");

        f.Add(("N3", "An empty stream is retried and then thrown, never turned into an 'unknown' SceneDna",
            errs.Count == 0, errs.Count == 0 ? $"retried {fake.Calls} times, then threw with the cause named" : string.Join("; ", errs)));
    }

    // Reasoning models wrap the answer, and the gateway sometimes adds prose
    // around it. Both are stripped before parsing.
    private static async Task DoN4(
        ILoggerFactory lf, string photo, List<(string, string, bool, string)> f)
    {
        var errs = new List<string>();
        var wrapped = "<think>The forecourt has pumps, so this is a filling station.</think>\n"
                    + "Here is the JSON you asked for:\n```json\n" + SnakeCaseAnswer + "\n```\nHope that helps.";
        var (provider, _) = Build(lf, Chunks(wrapped));

        var dna = await provider.AnalyzeImageAsync(photo, "prompt");
        if (dna.SceneType != "gas_station")
            errs.Add($"scene_type read as '{dna.SceneType}' — the wrapper was not stripped");
        if (dna.Geometry.Buildings.Count != 1)
            errs.Add("buildings lost while stripping the wrapper");

        f.Add(("N4", "A <think> block, prose and code fences around the JSON are stripped before parsing",
            errs.Count == 0, errs.Count == 0 ? "wrapped answer read back correctly" : string.Join("; ", errs)));
    }

    // Unparseable is not the same as empty: there IS an answer, it is just not
    // JSON. The stub is the right outcome here — but it must be reached through
    // the logged catch, not silently, and it must still be a usable object.
    private static async Task DoN5(
        ILoggerFactory lf, string photo, List<(string, string, bool, string)> f)
    {
        var errs = new List<string>();
        var (provider, _) = Build(lf, Chunks("I am sorry, I cannot help with that request."));

        var dna = await provider.AnalyzeImageAsync(photo, "prompt");

        if (dna.SceneType != "unknown")
            errs.Add($"scene_type is '{dna.SceneType}', expected the 'unknown' stub");
        if (dna.Geometry.Roads.Count != 0 || dna.Geometry.Buildings.Count != 0)
            errs.Add("the stub carried geometry it could not have read");

        // And the guard that stops it costing money: an unrenderable type is
        // refused before generation (the same rule C87 asserts).
        if (SceneDnaValidator.IsRenderableSceneType(dna.SceneType, dna.Environment.Terrain,
                new Dictionary<string, string> { ["gas_station"] = "x", ["unknown"] = "y" }))
            errs.Add("the stub would pass the pre-generation guard and bill a run");

        f.Add(("N5", "A non-JSON answer falls back to the 'unknown' stub, which the spend guard then refuses",
            errs.Count == 0, errs.Count == 0 ? "stub returned and refused by IsRenderableSceneType" : string.Join("; ", errs)));
    }

    // Verification takes only the five fields it asked about. Anything else in
    // the answer is ignored: this pass is a second opinion on named claims, not
    // a second chance to rewrite the scene.
    private static async Task DoN6(
        ILoggerFactory lf, string photo, List<(string, string, bool, string)> f)
    {
        var errs = new List<string>();
        var answer = """
            {"scene_type": "corner_shop", "parking": "none", "sidewalks": true,
             "terrain": "suburban", "trees": [],
             "immutable_elements": ["this must be ignored"]}
            """;
        var (provider, _) = Build(lf, Chunks(answer));

        var before = Original();
        var after  = await provider.VerifyAsync(photo, before);

        if (after.SceneType != "corner_shop")        errs.Add("scene_type was not applied");
        if (after.Geometry.Parking != "none")        errs.Add($"parking is '{after.Geometry.Parking}', expected 'none'");
        if (after.Environment.Terrain != "suburban") errs.Add("terrain was not applied");
        if (after.Environment.Trees.Count != 0)      errs.Add("trees were not applied");

        // Untouched: not asked about, so not up for revision.
        if (after.Id != before.Id)                            errs.Add("the id changed");
        if (after.Geometry.Buildings.Count != before.Geometry.Buildings.Count)
            errs.Add("buildings were rewritten by a pass that never asked about them");
        if (!Equals(after.ImmutableElements.Count, before.ImmutableElements.Count))
            errs.Add("immutable_elements were taken from the verification answer");

        f.Add(("N6", "Verification applies only the five fields it asked about and leaves the rest of the SceneDna alone",
            errs.Count == 0, errs.Count == 0 ? "five fields applied, id/buildings/immutable untouched" : string.Join("; ", errs)));
    }

    // Best-effort by design: the first pass already produced a usable SceneDna,
    // and a failed second opinion must never cost the run.
    private static async Task DoN7(
        ILoggerFactory lf, string photo, List<(string, string, bool, string)> f)
    {
        var errs = new List<string>();
        var before = Original();

        var (garbage, _) = Build(lf, Chunks("no json here at all"));
        var afterGarbage = await garbage.VerifyAsync(photo, before);
        if (afterGarbage != before)
            errs.Add("an unparseable verification answer changed the SceneDna");

        var (empty, _) = Build(lf, new List<string> { """{"choices":[{"delta":{}}]}""" });
        var afterEmpty = await empty.VerifyAsync(photo, before);
        if (afterEmpty != before)
            errs.Add("an empty verification stream changed the SceneDna");

        f.Add(("N7", "A failed verification keeps the first pass rather than throwing or corrupting it",
            errs.Count == 0, errs.Count == 0 ? "unparseable and empty answers both left the SceneDna untouched" : string.Join("; ", errs)));
    }

    // The enrich round-trip that could never work: the SceneDna went out in the
    // C# record's PascalCase, the model mirrored that shape back, and the DTO
    // read nulls. Asserted on the request body, because the bug was in what we
    // sent, not in what came back.
    private static async Task DoN8(
        ILoggerFactory lf, string photo, List<(string, string, bool, string)> f)
    {
        var errs = new List<string>();
        var (provider, fake) = Build(lf, Chunks(SnakeCaseAnswer));

        // NOT "scene_type" as the missing field: it appears in the prompt's own
        // sentence about what is missing, and the positive assertion below would
        // then pass on that alone. The first version of this check did exactly
        // that — it could not fail, and did not, with the bug reintroduced.
        await provider.EnrichAsync(photo, Original(), new[] { "geometry.roads" });

        var sent = fake.LastBody ?? "";
        if (!sent.Contains("scene_type", StringComparison.Ordinal))
            errs.Add("the SceneDna shown to the model is not in snake_case");
        // Bare, not quoted: the body is itself JSON, so the SceneDna's own quotes
        // arrive backslash-escaped and a quoted needle never matches.
        if (sent.Contains("SceneType", StringComparison.Ordinal))
            errs.Add("the SceneDna went out in PascalCase — the model will mirror it back unreadable");

        f.Add(("N8", "Enrichment shows the model the SceneDna in the same snake_case its schema documents",
            errs.Count == 0, errs.Count == 0 ? "request body carries snake_case keys only" : string.Join("; ", errs)));
    }

    // ── Fixtures ──────────────────────────────────────────────────────────────

    private static (VisionProvider Provider, FakeNvidia Fake) Build(ILoggerFactory lf, List<string> chunks)
    {
        var fake = new FakeNvidia(chunks);
        return (new VisionProvider(fake, lf.CreateLogger<VisionProvider>()), fake);
    }

    // One SSE payload per content fragment, the shape ExtractStreamedContent
    // reassembles. Split so the test exercises the reassembly, not just a
    // single-chunk shortcut.
    private static List<string> Chunks(string text) =>
        Enumerable.Range(0, (text.Length + 39) / 40)
            .Select(i => JsonSerializer.Serialize(new
            {
                choices = new[] { new { delta = new { content = text.Substring(i * 40, Math.Min(40, text.Length - i * 40)) } } }
            }))
            .ToList();

    private static SceneDna Original() => new(
        Id:        "vision-smoke-1",
        CreatedAt: "2026-01-01T00:00:00Z",
        SceneType: "gas_station",
        Camera:    new Camera("eye-level", "street", 60),
        Geometry:  new Geometry(
            [new Road("arterial", 2, ["center line"], "asphalt")],
            Sidewalks: true, Curbs: true,
            [new Building("pump canopy", "center", 1, ["metal"], "flat", "deep")],
            ["left"], "lot"),
        Environment: new Models.Environment("urban", ["power lines"],
            [new Tree("kerb", "medium", "maple")], ["grass strip"]),
        ImmutableElements: ["canopy support columns"],
        Distinctive: ["BALLENOIL sign in blue letters"]);

    private const string SnakeCaseAnswer = """
        {"scene_type":"gas_station",
         "camera":{"height":"eye-level","direction":"street","fov":60},
         "composition":{"subject_distance":"mid","subject_frame_share":"large","horizon":"high"},
         "geometry":{"roads":[{"type":"arterial","lanes":2,"markings":["center line"],"surface":"asphalt"}],
                     "sidewalks":true,"curbs":true,
                     "buildings":[{"type":"pump canopy","position":"center","stories":1,
                                   "materials":["metal"],"roof":"flat","setback":"deep"}],
                     "driveways":["left"],"parking":"lot"},
         "environment":{"terrain":"urban","utilities":["power lines"],
                        "trees":[{"position":"kerb","size":"medium","type":"maple"}],
                        "landscape":["grass strip"]},
         "immutable_elements":["canopy support columns"],
         "distinctive":["BALLENOIL sign in blue letters"]}
        """;

    // The same content in the C# record's own casing — what the model sends back
    // when the conversation showed it that shape.
    private const string PascalCaseAnswer = """
        {"SceneType":"gas_station",
         "Camera":{"Height":"eye-level","Direction":"street","Fov":60},
         "Geometry":{"Roads":[{"Type":"arterial","Lanes":2,"Markings":["center line"],"Surface":"asphalt"}],
                     "Sidewalks":true,"Curbs":true,
                     "Buildings":[{"Type":"pump canopy","Position":"center","Stories":1,
                                   "Materials":["metal"],"Roof":"flat","Setback":"deep"}],
                     "Driveways":["left"],"Parking":"lot"},
         "Environment":{"Terrain":"urban","Utilities":["power lines"],
                        "Trees":[{"Position":"kerb","Size":"medium","Type":"maple"}],
                        "Landscape":["grass strip"]},
         "ImmutableElements":["canopy support columns"],
         "Distinctive":["BALLENOIL sign in blue letters"]}
        """;

    // Returns the same scripted chunks to every call and records what it was
    // asked, so a check can assert on the request as well as the response.
    private sealed class FakeNvidia : INvidiaProvider
    {
        private readonly List<string> _chunks;
        public int Calls { get; private set; }
        public string? LastBody { get; private set; }

        public FakeNvidia(List<string> chunks) => _chunks = chunks;

        public Task<IReadOnlyList<string>> PostStreamAsync(string url, object body)
        {
            Calls++;
            LastBody = JsonSerializer.Serialize(body);
            return Task.FromResult<IReadOnlyList<string>>(_chunks);
        }

        public Task<string> PostAsync(string url, object body) =>
            throw new NotSupportedException("the vision path streams");

        public Task<string> PollAsync(string url, int timeoutSeconds = 120) =>
            throw new NotSupportedException("the vision path streams");
    }

    // ── Report ────────────────────────────────────────────────────────────────

    private static async Task WriteReport(
        List<(string Id, string Desc, bool Pass, string Detail)> findings, ILogger logger)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Vision Smoke Test Report");
        sb.AppendLine();
        sb.AppendLine($"Generated: {DateTimeOffset.UtcNow:o}");
        sb.AppendLine();
        sb.AppendLine("Offline: the real VisionProvider against a fake INvidiaProvider.");
        sb.AppendLine("Covers reading the answer, not the accuracy of what the model saw.");
        sb.AppendLine();
        sb.AppendLine("| Check | Description | Status | Detail |");
        sb.AppendLine("|-------|-------------|--------|--------|");
        foreach (var (id, desc, pass, detail) in findings)
            sb.AppendLine($"| {id} | {desc} | {(pass ? "✅ PASS" : "❌ FAIL")} | {detail.Replace("|", "\\|")} |");
        sb.AppendLine();

        var outDir = Path.Combine("output", "smoke-vision");
        Directory.CreateDirectory(outDir);
        await File.WriteAllTextAsync(Path.Combine(outDir, "report.md"), sb.ToString());

        logger.LogInformation("[SmokeVision] Check summary:");
        foreach (var (id, _, pass, detail) in findings)
            logger.LogInformation("[SmokeVision]   {Id} {Status}: {Detail}", id, pass ? "PASS" : "FAIL", detail);
    }
}
