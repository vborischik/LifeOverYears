using System.Text;
using System.Text.Json;
using Autofac;
using LifeOverYears.Models;
using LifeOverYears.Services.Interfaces;
using Microsoft.Extensions.Logging;

namespace LifeOverYears.Services;

// Measures how often Vision reads a photo the way a person does, against a
// folder of images with an expected.json beside them.
//
// Deliberately NOT one of the --smoke-* suites: it needs the network, takes
// half a minute per photo, and it grades a model rather than the code around
// it. A smoke suite must be able to fail for a reason the author can fix.
//
// It exists because of a mistake worth not repeating. The verification pass was
// reported as working on the strength of it CHANGING six fields, and the
// changes were never checked; looking at the photographs afterwards, they were
// wrong. An agreement rate says nothing without ground truth — so this compares
// against ground truth, and can be run with the verification pass on and off to
// say whether it helps or hurts rather than merely whether it does something.
public static class VisionAccuracyTest
{
    private static readonly string[] ImageExtensions = { ".jpg", ".jpeg", ".png" };

    private static readonly JsonSerializerOptions ReadJson =
        new() { PropertyNameCaseInsensitive = true };

    public static async Task<int> RunAsync(
        string[] args, string launchDir, IContainer container, ILoggerFactory loggerFactory)
    {
        var logger = loggerFactory.CreateLogger("VisionAccuracy");

        if (args.Length < 1)
        {
            logger.LogError("usage: vision-accuracy <folder> [--no-double-check]");
            return 1;
        }

        var folder = Path.GetFullPath(args[0], launchDir);
        if (!Directory.Exists(folder))
        {
            logger.LogError("Folder not found: {Folder}", folder);
            return 1;
        }

        var expectedPath = Path.Combine(folder, "expected.json");
        if (!File.Exists(expectedPath))
        {
            logger.LogError(
                "No expected.json in {Folder}. Accuracy needs ground truth — without it this would " +
                "only report what the model said, which is what vision-variance already does.", folder);
            return 1;
        }

        // Read loosely: the file carries "_comment" and per-entry "_note" keys
        // that explain the judgement calls, and a strict shape would reject the
        // very notes that make the labels auditable. Anything whose key starts
        // with an underscore, or whose value is not an object, is commentary.
        var raw = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(
            await File.ReadAllTextAsync(expectedPath), ReadJson) ?? new();

        var expected = raw
            .Where(kv => !kv.Key.StartsWith('_') && kv.Value.ValueKind == JsonValueKind.Object)
            .ToDictionary(
                kv => kv.Key,
                kv => kv.Value.EnumerateObject()
                        .Where(p => !p.Name.StartsWith('_'))
                        .ToDictionary(p => p.Name, p => p.Value, StringComparer.OrdinalIgnoreCase),
                StringComparer.OrdinalIgnoreCase);

        // The verification pass is the thing under test as much as the model is,
        // so it is switchable here rather than read from config.
        var doubleCheck = !args.Contains("--no-double-check");
        var vision = new VisionService(
            container.Resolve<IVisionProvider>(),
            container.Resolve<IDataService>(),
            loggerFactory.CreateLogger<VisionService>(),
            doubleCheck);

        var images = Directory.EnumerateFiles(folder)
            .Where(f => ImageExtensions.Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase))
            .OrderBy(f => Path.GetFileName(f), StringComparer.OrdinalIgnoreCase)
            .ToList();

        logger.LogInformation("Vision accuracy over {Count} photos, DoubleCheck={DoubleCheck}",
            images.Count, doubleCheck);

        // Written after every photo, not at the end. Three of these runs were
        // stopped part-way and left nothing behind, having spent the calls
        // anyway — a measurement that only exists if it finishes is a
        // measurement you cannot take on a slow day.
        var outDir = Path.Combine("output", "vision-accuracy");
        Directory.CreateDirectory(outDir);
        var suffix     = doubleCheck ? "doublecheck" : "single";
        var reportPath = Path.Combine(outDir, $"{DateTime.Now:yyyyMMdd-HHmm}-{suffix}.txt");

        var rows = new List<Row>();
        foreach (var path in images)
        {
            var name = Path.GetFileNameWithoutExtension(path);
            if (!expected.TryGetValue(name, out var want))
            {
                logger.LogWarning("{Name} has no entry in expected.json — skipped", name);
                continue;
            }

            logger.LogInformation("Analyzing {Name}", name);
            SceneDna dna;
            try
            {
                dna = await vision.AnalyzeAsync(path);
            }
            catch (Exception ex)
            {
                // A photo the model could not read at all is its own result,
                // not a reason to abandon the measurement.
                logger.LogError(ex, "Vision failed for {Name}", name);
                rows.Add(new Row(name, "(failed)", "(failed)", "(failed)", want, Failed: true));
                continue;
            }

            var row = new Row(
                name,
                dna.SceneType ?? "(none)",
                dna.Geometry.Parking ?? "(none)",
                dna.Geometry.Sidewalks ? "true" : "false",
                want,
                Failed: false);
            rows.Add(row);

            // Visible as it goes, so a run that is cut short has still told you
            // something by the time it stops.
            logger.LogInformation("{Name}: {Summary}", name, Summarize(row));
            await File.WriteAllTextAsync(reportPath, BuildReport(rows, doubleCheck, images.Count), Encoding.UTF8);
        }

        var report = BuildReport(rows, doubleCheck, images.Count);
        Console.WriteLine();
        Console.WriteLine(report);
        await File.WriteAllTextAsync(reportPath, report, Encoding.UTF8);
        logger.LogInformation("Report written to {Path}", reportPath);

        // Always zero: this grades a model, and a model scoring badly is a
        // finding, not a build failure. Nothing in CI should go red because
        // today's photographs were harder than yesterday's.
        return 0;
    }

    // One photo's verdict, for the running log.
    private static string Summarize(Row row)
    {
        var parts = new List<string>();
        foreach (var (key, got) in new (string, string)[]
                 { ("scene_type", row.SceneType), ("parking", row.Parking), ("sidewalks", row.Sidewalks) })
        {
            if (!row.Want.TryGetValue(key, out var el)) continue;
            var want = el.ValueKind == JsonValueKind.True  ? "true"
                     : el.ValueKind == JsonValueKind.False ? "false"
                     : el.ToString();
            parts.Add(string.Equals(want, got, StringComparison.OrdinalIgnoreCase)
                ? $"{key} ok"
                : $"{key} MISMATCH (want {want}, got {got})");
        }
        return string.Join("; ", parts);
    }

    private record Row(
        string Name, string SceneType, string Parking, string Sidewalks,
        Dictionary<string, JsonElement> Want, bool Failed);

    private static string BuildReport(List<Row> rows, bool doubleCheck, int imageCount)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"VISION ACCURACY — {rows.Count} labelled photos of {imageCount}, " +
                      $"DoubleCheck={(doubleCheck ? "on" : "off")}");
        sb.AppendLine();

        var fields = new (string Key, Func<Row, string> Got)[]
        {
            ("scene_type", r => r.SceneType),
            ("parking",    r => r.Parking),
            ("sidewalks",  r => r.Sidewalks),
        };

        sb.AppendLine($"{"photo",-12} {"field",-11} {"expected",-16} {"got",-16} result");
        foreach (var row in rows)
            foreach (var (key, got) in fields)
            {
                if (!row.Want.TryGetValue(key, out var wantEl)) continue;
                var want = wantEl.ValueKind == JsonValueKind.True  ? "true"
                         : wantEl.ValueKind == JsonValueKind.False ? "false"
                         : wantEl.ToString();
                var actual = got(row);
                var ok = string.Equals(want, actual, StringComparison.OrdinalIgnoreCase);
                sb.AppendLine($"{row.Name,-12} {key,-11} {want,-16} {actual,-16} {(ok ? "ok" : "MISMATCH")}");
            }

        sb.AppendLine();
        foreach (var (key, got) in fields)
        {
            var judged = rows.Where(r => r.Want.ContainsKey(key)).ToList();
            if (judged.Count == 0) continue;
            var hits = judged.Count(r =>
            {
                var el = r.Want[key];
                var want = el.ValueKind == JsonValueKind.True  ? "true"
                         : el.ValueKind == JsonValueKind.False ? "false"
                         : el.ToString();
                return string.Equals(want, got(r), StringComparison.OrdinalIgnoreCase);
            });
            sb.AppendLine($"{key,-11} {hits}/{judged.Count} correct");
        }

        var failed = rows.Count(r => r.Failed);
        if (failed > 0)
            sb.AppendLine($"\n{failed} photo(s) the model could not read at all.");

        sb.AppendLine();
        sb.AppendLine("Run again with --no-double-check and compare: a verification pass that");
        sb.AppendLine("does not raise these numbers is not earning its call.");
        return sb.ToString();
    }
}
