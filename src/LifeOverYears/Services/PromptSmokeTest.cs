// TODO: remove smoke test
using System.Text;
using System.Text.Json;
using LifeOverYears.Models;
using LifeOverYears.Services.Interfaces;
using Microsoft.Extensions.Logging;
using Environment = LifeOverYears.Models.Environment;

namespace LifeOverYears.Services;

// TODO: remove smoke test
public static class PromptSmokeTest
{
    private static readonly int[] Years = { 1975, 1985, 1995, 2005, 2015, 2025 };

    private static readonly JsonSerializerOptions WriteJson = new() { WriteIndented = true };

    private static readonly string[] Placeholders =
    {
        "{YEAR}", "{PRESERVE_BLOCK}", "{SCENE_BLOCK}", "{PEOPLE_BLOCK}",
        "{VEHICLES_BLOCK}", "{ENVIRONMENT_BLOCK}", "{STYLE_BLOCK}"
    };

    // Expected coffee-price substring in downtown_street storefronts per era (C8)
    private static readonly Dictionary<int, string> DowntownCoffeePrices = new()
    {
        { 1975, "HOT COFFEE 25¢" },
        { 1985, "HOT COFFEE 45¢" },
        { 1995, "HOT COFFEE 75¢" },
        { 2005, "COFFEE $1.25"   },
        { 2015, "COFFEE $2.25"   },
        { 2025, "COFFEE $3.50"   }
    };

    // ── Entry point ───────────────────────────────────────────────────────────

    public static async Task<int> RunAsync(
        IPromptService promptService,
        IDataService   dataService,
        ILogger        logger)
    {
        logger.LogInformation("[Smoke] PromptSmokeTest starting");

        // a) Fake SceneDna objects
        var gasScene      = MakeGasStationScene();
        var downtownScene = MakeDowntownScene();
        var unknownScene  = MakeUnknownScene();

        // Load all era profiles
        var eras = new Dictionary<int, EraProfile>();
        foreach (var year in Years)
            eras[year] = await dataService.LoadEraProfileAsync(year);

        // b) Run prompts: 2 scenes × 2 runs × 6 years
        var gasRun1 = await BuildRun(promptService, gasScene,      eras, 42,   Years);
        var gasRun2 = await BuildRun(promptService, gasScene,      eras, 1337, Years);
        var dtRun1  = await BuildRun(promptService, downtownScene, eras, 42,   Years);
        var dtRun2  = await BuildRun(promptService, downtownScene, eras, 1337, Years);

        // c) Unknown scene — 1985 only, must not throw
        var unknownCtx    = new GenerationContext { Random = new Random(42) };
        var unknownPrompt = await promptService.BuildAsync(unknownScene, eras[1985], unknownCtx);
        // SceneType "unknown" has no dedicated key → fell back to "default" scene_content
        logger.LogWarning("[Smoke] SceneType 'unknown' fell back to default scene_content for era 1985 — fallback path exercised");

        // Save output
        await SaveRun(gasScene.SceneType,      1, gasRun1);
        await SaveRun(gasScene.SceneType,      2, gasRun2);
        await SaveRun(downtownScene.SceneType, 1, dtRun1);
        await SaveRun(downtownScene.SceneType, 2, dtRun2);
        await SaveRun(unknownScene.SceneType,  1, new Dictionary<int, Prompt> { { 1985, unknownPrompt } });

        // d) Checks C1–C10
        var findings = new List<(string Id, string Desc, bool Pass, string Detail)>();

        DoC1 (eras,                                           findings);
        DoC2 (gasRun1, gasRun2, dtRun1, dtRun2, unknownPrompt, findings);
        DoC3 (gasRun1, gasRun2, dtRun1, dtRun2,               findings);
        DoC4 (gasRun1, gasRun2, dtRun1, dtRun2, eras,         findings);
        DoC5 (gasRun1, gasRun2, dtRun1, dtRun2,               findings);
        DoC6 (gasRun1, gasRun2, gasScene,                     findings);
        DoC7 (gasRun1, gasRun2, dtRun1, dtRun2,               findings);
        DoC8 (gasRun1, gasRun2, dtRun1, dtRun2, eras,         findings);
        DoC9 (gasRun1, gasScene, dtRun1, downtownScene,       findings);
        DoC10(gasRun1, gasRun2, dtRun1, dtRun2,               findings);
        DoC11(gasRun1, gasRun2, dtRun1, dtRun2, unknownPrompt, findings);
        DoC12(gasRun1, gasRun2, dtRun1, dtRun2, eras,         findings);
        DoC13(gasRun1, gasRun2, dtRun1, dtRun2, eras,         findings);
        DoC14(gasRun1, gasRun2,                               findings);

        // e) Report
        await WriteReport(findings, gasRun1, gasRun2, dtRun1, dtRun2, logger);

        int passed = findings.Count(f => f.Pass);
        int total  = findings.Count;
        Console.WriteLine();
        Console.WriteLine($"Smoke test: {passed}/{total} checks passed" +
                          (passed == total ? "" : " — FAILURES DETECTED"));
        Console.WriteLine("See output/smoke/report.md for full details.");
        logger.LogInformation("[Smoke] Done: {Passed}/{Total} checks passed", passed, total);

        return passed == total ? 0 : 1;
    }

    // ── Fake SceneDna factories ───────────────────────────────────────────────

    private static SceneDna MakeGasStationScene() => new(
        Id:        "smoke-gas-station",
        CreatedAt: "2025-01-01T00:00:00Z",
        SceneType: "gas_station",
        Camera: new Camera(Height: "eye-level", Direction: "street-facing", Fov: 75),
        Geometry: new Geometry(
            Roads:
            [
                new Road(
                    Type:     "commercial arterial",
                    Lanes:    4,
                    Markings: ["yellow center line", "white edge lines", "turn lane arrows"],
                    Surface:  "asphalt")
            ],
            Sidewalks: true,
            Curbs:     true,
            Buildings:
            [
                new Building(
                    Type:      "gas station canopy",
                    Position:  "center lot over pump islands",
                    Stories:   1,
                    Materials: ["steel frame", "corrugated metal fascia"],
                    Roof:      "flat",
                    Setback:   "30 feet from road"),
                new Building(
                    Type:      "service station office",
                    Position:  "rear right corner",
                    Stories:   1,
                    Materials: ["concrete block", "brick veneer"],
                    Roof:      "flat parapet",
                    Setback:   "at parking apron"),
                new Building(
                    Type:      "open service bay",
                    Position:  "rear left corner",
                    Stories:   1,
                    Materials: ["concrete block"],
                    Roof:      "gabled metal",
                    Setback:   "at parking apron")
            ],
            Driveways: ["north driveway apron", "south driveway apron"],
            Parking:   "open asphalt apron surrounding pump islands"),
        Environment: new Environment(
            Terrain:   "prairie",
            Utilities: ["overhead power lines on wooden poles", "transformer on northeast corner pole"],
            Trees:
            [
                new Tree(Position: "northeast corner of lot", Size: "medium", Type: "cottonwood"),
                new Tree(Position: "sidewalk edge left of entrance", Size: "small",  Type: "elm"),
                new Tree(Position: "back fence line",                Size: "large",  Type: "cedar")
            ],
            Landscape: ["flat asphalt apron", "painted concrete curbing", "narrow grass strip at road edge"]),
        ImmutableElements:
        [
            "canopy with four corner support posts",
            "pump island with eight dispensers",
            "north and south driveway aprons"
        ]);

    private static SceneDna MakeDowntownScene() => new(
        Id:        "smoke-downtown-street",
        CreatedAt: "2025-01-01T00:00:00Z",
        SceneType: "downtown_street",
        Camera: new Camera(Height: "eye-level", Direction: "street-facing", Fov: 80),
        Geometry: new Geometry(
            Roads:
            [
                new Road(
                    Type:     "main street",
                    Lanes:    2,
                    Markings: ["yellow center line", "white edge lines", "painted crosswalks"],
                    Surface:  "asphalt")
            ],
            Sidewalks: true,
            Curbs:     true,
            Buildings:
            [
                new Building(
                    Type:      "two-story brick commercial",
                    Position:  "left block face",
                    Stories:   2,
                    Materials: ["red brick", "cast iron storefront columns"],
                    Roof:      "flat parapet",
                    Setback:   "at sidewalk"),
                new Building(
                    Type:      "single-story retail",
                    Position:  "center block face",
                    Stories:   1,
                    Materials: ["brick", "plate glass storefront"],
                    Roof:      "flat with low parapet",
                    Setback:   "at sidewalk"),
                new Building(
                    Type:      "corner mixed-use",
                    Position:  "right corner",
                    Stories:   3,
                    Materials: ["brick", "stone trim cornice"],
                    Roof:      "flat",
                    Setback:   "at sidewalk")
            ],
            Driveways: ["alley entrance mid-block"],
            Parking:   "parallel street parking both sides"),
        Environment: new Environment(
            Terrain:   "urban flat",
            Utilities: ["overhead power and telephone lines", "wooden utility poles every 50 feet"],
            Trees:
            [
                new Tree(Position: "sidewalk left near corner",            Size: "large",  Type: "oak"),
                new Tree(Position: "sidewalk center in front of retail", Size: "medium", Type: "maple")
            ],
            Landscape: ["concrete sidewalks", "brick pavers at crosswalks", "small planted tree pits"]),
        ImmutableElements:
        [
            "cast iron storefront columns on left building",
            "recessed corner entrance on right building",
            "brick alley mid-block"
        ]);

    private static SceneDna MakeUnknownScene() => new(
        Id:        "smoke-unknown",
        CreatedAt: "2025-01-01T00:00:00Z",
        SceneType: "unknown",
        Camera: new Camera(Height: "elevated", Direction: "oblique", Fov: 60),
        Geometry: new Geometry(
            Roads:
            [
                new Road(
                    Type:     "local street",
                    Lanes:    2,
                    Markings: ["center line"],
                    Surface:  "asphalt")
            ],
            Sidewalks: false,
            Curbs:     false,
            Buildings:
            [
                new Building(
                    Type:      "industrial warehouse",
                    Position:  "background",
                    Stories:   1,
                    Materials: ["corrugated metal"],
                    Roof:      "gabled",
                    Setback:   "50 feet from road")
            ],
            Driveways: ["gravel access road"],
            Parking:   "gravel lot"),
        Environment: new Environment(
            Terrain:   "industrial flat",
            Utilities: ["overhead power lines"],
            Trees:
            [
                new Tree(Position: "fence line east", Size: "small", Type: "pine")
            ],
            Landscape: ["gravel and packed dirt lot"]),
        ImmutableElements:
        [
            "loading dock on east wall"
        ]);

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static async Task<Dictionary<int, Prompt>> BuildRun(
        IPromptService           svc,
        SceneDna                 scene,
        Dictionary<int, EraProfile> eras,
        int                      seed,
        int[]                    years)
    {
        var ctx     = new GenerationContext { Random = new Random(seed) };
        var prompts = new Dictionary<int, Prompt>();
        foreach (var year in years)
            prompts[year] = await svc.BuildAsync(scene, eras[year], ctx);
        return prompts;
    }

    private static async Task SaveRun(string sceneType, int run, Dictionary<int, Prompt> prompts)
    {
        foreach (var (year, prompt) in prompts)
        {
            var dir = Path.Combine("output", "smoke", sceneType, $"run{run}");
            Directory.CreateDirectory(dir);
            await File.WriteAllTextAsync(Path.Combine(dir, $"{year}.txt"),  prompt.Text);
            await File.WriteAllTextAsync(Path.Combine(dir, $"{year}.json"),
                JsonSerializer.Serialize(prompt, WriteJson));
        }
    }

    // ── Checks ────────────────────────────────────────────────────────────────

    private static void DoC1(
        Dictionary<int, EraProfile> eras,
        List<(string, string, bool, string)> f)
    {
        var errs = new List<string>();
        string[] requiredKeys = { "downtown_street", "gas_station", "strip_mall", "default" };

        foreach (var (year, era) in eras)
        {
            if (era.SceneContent is null)
            {
                errs.Add($"{year}: scene_content is null");
                continue;
            }
            foreach (var key in requiredKeys)
                if (!era.SceneContent.ContainsKey(key))
                    errs.Add($"{year}: missing scene_content key '{key}'");

            if (era.Photography.ColorMode is null)
                errs.Add($"{year}: photography.color_mode is null");
        }

        f.Add(("C1", "Era deserialization: scene_content has required keys and color_mode present",
            errs.Count == 0, errs.Count == 0 ? "All 6 eras OK" : Join(errs)));
    }

    private static void DoC2(
        Dictionary<int, Prompt> gasRun1, Dictionary<int, Prompt> gasRun2,
        Dictionary<int, Prompt> dtRun1,  Dictionary<int, Prompt> dtRun2,
        Prompt unknownPrompt,
        List<(string, string, bool, string)> f)
    {
        var errs = new List<string>();
        var all  = gasRun1.Values.Concat(gasRun2.Values)
                          .Concat(dtRun1.Values).Concat(dtRun2.Values)
                          .Append(unknownPrompt);

        foreach (var p in all)
            foreach (var ph in Placeholders)
                if (p.Text.Contains(ph))
                    errs.Add($"year={p.Year}: found '{ph}'");

        f.Add(("C2", "No unresolved template placeholders remain",
            errs.Count == 0, errs.Count == 0 ? "All placeholders resolved" : Join(errs)));
    }

    private static void DoC3(
        Dictionary<int, Prompt> gasRun1, Dictionary<int, Prompt> gasRun2,
        Dictionary<int, Prompt> dtRun1,  Dictionary<int, Prompt> dtRun2,
        List<(string, string, bool, string)> f)
    {
        var errs = new List<string>();

        void Check(Dictionary<int, Prompt> run, string label)
        {
            var all   = run.Values.SelectMany(p => p.SelectedVehicles).ToList();
            var dupes = all.GroupBy(v => v, StringComparer.OrdinalIgnoreCase)
                           .Where(g => g.Count() > 1).Select(g => g.Key).ToList();
            if (dupes.Any())
                errs.Add($"{label}: {string.Join(", ", dupes)}");
        }

        Check(gasRun1, "gas_station/run1");
        Check(gasRun2, "gas_station/run2");
        Check(dtRun1,  "downtown_street/run1");
        Check(dtRun2,  "downtown_street/run2");

        f.Add(("C3", "No vehicle model reuse within each run (dedup invariant)",
            errs.Count == 0, errs.Count == 0 ? "No duplicates in any run" : "Duplicates: " + Join(errs)));
    }

    private static void DoC4(
        Dictionary<int, Prompt> gasRun1, Dictionary<int, Prompt> gasRun2,
        Dictionary<int, Prompt> dtRun1,  Dictionary<int, Prompt> dtRun2,
        Dictionary<int, EraProfile> eras,
        List<(string, string, bool, string)> f)
    {
        var errs = new List<string>();

        void Check(Dictionary<int, Prompt> run, string sceneType, string label)
        {
            foreach (var (year, prompt) in run)
            {
                var era    = eras[year];
                SceneContent? sc = null;
                if (era.SceneContent?.TryGetValue(sceneType, out sc) != true)
                    era.SceneContent?.TryGetValue("default", out sc);
                if (sc is null) continue;

                int actual = prompt.SelectedVehicles.Count;
                if (actual < sc.Vehicles.Min || actual > sc.Vehicles.Max)
                    errs.Add($"{label}/{year}: count={actual} outside [{sc.Vehicles.Min},{sc.Vehicles.Max}]");

                int linesInText = prompt.SelectedVehicles.Count(m => prompt.Text.Contains($"- {m}"));
                if (linesInText != actual)
                    errs.Add($"{label}/{year}: VEHICLES section has {linesInText} model lines, SelectedVehicles.Count={actual}");
            }
        }

        Check(gasRun1, "gas_station",     "gas_station/run1");
        Check(gasRun2, "gas_station",     "gas_station/run2");
        Check(dtRun1,  "downtown_street", "downtown_street/run1");
        Check(dtRun2,  "downtown_street", "downtown_street/run2");

        f.Add(("C4", "Vehicle count in range and VEHICLES section lines match SelectedVehicles.Count",
            errs.Count == 0, errs.Count == 0 ? "All vehicle counts correct" : Join(errs)));
    }

    private static void DoC5(
        Dictionary<int, Prompt> gasRun1, Dictionary<int, Prompt> gasRun2,
        Dictionary<int, Prompt> dtRun1,  Dictionary<int, Prompt> dtRun2,
        List<(string, string, bool, string)> f)
    {
        var errs = new List<string>();

        void Check(Dictionary<int, Prompt> run1, Dictionary<int, Prompt> run2, string scene)
        {
            int vehicleDiffs  = 0;
            var identicalYears = new List<int>();

            foreach (var year in Years)
            {
                var v1 = run1[year].SelectedVehicles.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
                var v2 = run2[year].SelectedVehicles.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();

                if (!v1.SequenceEqual(v2, StringComparer.OrdinalIgnoreCase))
                    vehicleDiffs++;

                if (run1[year].Text == run2[year].Text)
                    identicalYears.Add(year);
            }

            if (vehicleDiffs < 3)
                errs.Add($"{scene}: only {vehicleDiffs}/6 year vehicle lists differ (need ≥3)");
            if (identicalYears.Any())
                errs.Add($"{scene}: identical texts for years {string.Join(", ", identicalYears)}");
        }

        Check(gasRun1, gasRun2, "gas_station");
        Check(dtRun1,  dtRun2,  "downtown_street");

        f.Add(("C5", "Run1 vs Run2: ≥3 years differ in vehicles; no year has identical full text",
            errs.Count == 0, errs.Count == 0 ? "Sufficient variance between seeds" : Join(errs)));
    }

    private static void DoC6(
        Dictionary<int, Prompt> gasRun1, Dictionary<int, Prompt> gasRun2,
        SceneDna scene,
        List<(string, string, bool, string)> f)
    {
        var errs = new List<string>();

        void CheckLadder(Dictionary<int, Prompt> run, string label)
        {
            if (!run[1975].Text.Contains("very young sapling"))
                errs.Add($"{label}/1975: missing 'very young sapling'");
            if (!run[2005].Text.Contains("maturing tree"))
                errs.Add($"{label}/2005: missing 'maturing tree'");
            if (!run[2025].Text.Contains("mature tree, large canopy"))
                errs.Add($"{label}/2025: missing 'mature tree, large canopy'");
        }

        CheckLadder(gasRun1, "gas_station/run1");
        CheckLadder(gasRun2, "gas_station/run2");

        // Every tree position+type must appear verbatim in every year prompt
        foreach (var tree in scene.Environment.Trees)
        {
            var expected = $"{tree.Type} tree at {tree.Position}";
            foreach (var (run, label) in new[] { (gasRun1, "gas_station/run1"), (gasRun2, "gas_station/run2") })
                foreach (var (year, prompt) in run)
                    if (!prompt.Text.Contains(expected))
                        errs.Add($"{label}/{year}: missing tree '{expected}'");
        }

        // A mature (large) source tree must render a distinct size label in all six eras
        var matureTree = scene.Environment.Trees.FirstOrDefault(t =>
            t.Size.Equals("large", StringComparison.OrdinalIgnoreCase));
        if (matureTree is not null)
        {
            var linePrefix = $"- {matureTree.Type} tree at {matureTree.Position}: ";
            foreach (var (run, label) in new[] { (gasRun1, "gas_station/run1"), (gasRun2, "gas_station/run2") })
            {
                var labels = new List<string>();
                foreach (var (year, prompt) in run)
                {
                    var line = prompt.Text.Split('\n').FirstOrDefault(l => l.StartsWith(linePrefix));
                    if (line is null)
                        errs.Add($"{label}/{year}: no size label line for mature tree '{matureTree.Type}'");
                    else
                        labels.Add(line[linePrefix.Length..].Trim());
                }
                if (labels.Distinct().Count() != labels.Count)
                    errs.Add($"{label}: mature tree size labels not distinct across years: {string.Join(" | ", labels)}");
            }
        }

        f.Add(("C6", "Tree size ladder (distinct per era for mature trees, size-relative) and tree position+species in all prompts",
            errs.Count == 0, errs.Count == 0 ? "Tree ladder and positions correct" : Join(errs)));
    }

    private static void DoC7(
        Dictionary<int, Prompt> gasRun1, Dictionary<int, Prompt> gasRun2,
        Dictionary<int, Prompt> dtRun1,  Dictionary<int, Prompt> dtRun2,
        List<(string, string, bool, string)> f)
    {
        var errs = new List<string>();

        void Check(Dictionary<int, Prompt> run, string label)
        {
            if (!run[1975].Text.Contains("STRICTLY BLACK AND WHITE"))
                errs.Add($"{label}/1975: missing 'STRICTLY BLACK AND WHITE'");

            foreach (var year in Years.Where(y => y != 1975))
            {
                if (!run[year].Text.Contains("COLOR photograph"))
                    errs.Add($"{label}/{year}: missing 'COLOR photograph'");
                if (run[year].Text.Contains("STRICTLY BLACK AND WHITE"))
                    errs.Add($"{label}/{year}: unexpected 'STRICTLY BLACK AND WHITE'");
            }
        }

        Check(gasRun1, "gas_station/run1");
        Check(gasRun2, "gas_station/run2");
        Check(dtRun1,  "downtown_street/run1");
        Check(dtRun2,  "downtown_street/run2");

        f.Add(("C7", "1975=B&W (STRICTLY BLACK AND WHITE); 1985-2025=COLOR photograph",
            errs.Count == 0, errs.Count == 0 ? "Color mode correct in all prompts" : Join(errs)));
    }

    private static void DoC8(
        Dictionary<int, Prompt> gasRun1, Dictionary<int, Prompt> gasRun2,
        Dictionary<int, Prompt> dtRun1,  Dictionary<int, Prompt> dtRun2,
        Dictionary<int, EraProfile> eras,
        List<(string, string, bool, string)> f)
    {
        var errs = new List<string>();

        // Gas station: fuel price always emitted unconditionally — check both runs every year
        foreach (var year in Years)
        {
            var price = eras[year].Transportation.Fuel.AveragePricePerGallon;
            foreach (var (run, label) in new[] { (gasRun1, "gas/run1"), (gasRun2, "gas/run2") })
                if (!run[year].Text.Contains(price))
                    errs.Add($"{label}/{year}: fuel price '{price}' not found");
        }

        // Downtown: coffee price is sampled — require at least one run per year to contain it
        foreach (var year in Years)
        {
            var coffeeStr = DowntownCoffeePrices[year];
            if (!dtRun1[year].Text.Contains(coffeeStr) && !dtRun2[year].Text.Contains(coffeeStr))
                errs.Add($"downtown/{year}: coffee price '{coffeeStr}' absent from both runs (sampling miss)");
        }

        f.Add(("C8", "Gas station fuel prices always present; downtown coffee price in ≥1 run per year",
            errs.Count == 0, errs.Count == 0 ? "All price anchors found" : Join(errs)));
    }

    private static void DoC9(
        Dictionary<int, Prompt> gasRun1, SceneDna gasScene,
        Dictionary<int, Prompt> dtRun1,  SceneDna dtScene,
        List<(string, string, bool, string)> f)
    {
        var errs = new List<string>();

        void Check(Dictionary<int, Prompt> run, SceneDna scene, string label)
        {
            foreach (var (year, prompt) in run)
            {
                foreach (var b in scene.Geometry.Buildings)
                    if (!prompt.Text.Contains(b.Type))
                        errs.Add($"{label}/{year}: building type '{b.Type}' not in PRESERVE");
                foreach (var el in scene.ImmutableElements)
                    if (!prompt.Text.Contains(el))
                        errs.Add($"{label}/{year}: immutable element '{el}' not in PRESERVE");
            }
        }

        Check(gasRun1, gasScene, "gas_station/run1");
        Check(dtRun1,  dtScene,  "downtown_street/run1");

        f.Add(("C9", "PRESERVE block contains all building types and immutable elements verbatim",
            errs.Count == 0, errs.Count == 0 ? "All building types and immutable elements present" : Join(errs)));
    }

    private static void DoC10(
        Dictionary<int, Prompt> gasRun1, Dictionary<int, Prompt> gasRun2,
        Dictionary<int, Prompt> dtRun1,  Dictionary<int, Prompt> dtRun2,
        List<(string, string, bool, string)> f)
    {
        var errs = new List<string>();

        void Check(Dictionary<int, Prompt> run, string label)
        {
            foreach (var (year, prompt) in run)
            {
                // TEXT OVERLAY section is now removed — year is applied by a later overlay step
                if (prompt.Text.Contains("TEXT OVERLAY"))
                    errs.Add($"{label}/{year}: unexpected 'TEXT OVERLAY' section still present");

                // Year still anchors the VEHICLES block ("no vehicle newer than 1975")
                if (!prompt.Text.Contains($"no vehicle newer than {year}"))
                    errs.Add($"{label}/{year}: 'no vehicle newer than {year}' not found");
            }
        }

        Check(gasRun1, "gas_station/run1");
        Check(gasRun2, "gas_station/run2");
        Check(dtRun1,  "downtown_street/run1");
        Check(dtRun2,  "downtown_street/run2");

        f.Add(("C10", "No TEXT OVERLAY section remains; year still anchors the VEHICLES block",
            errs.Count == 0, errs.Count == 0 ? "Overlay removed and vehicle year anchors correct" : Join(errs)));
    }

    private static void DoC11(
        Dictionary<int, Prompt> gasRun1, Dictionary<int, Prompt> gasRun2,
        Dictionary<int, Prompt> dtRun1,  Dictionary<int, Prompt> dtRun2,
        Prompt unknownPrompt,
        List<(string, string, bool, string)> f)
    {
        var errs = new List<string>();
        var all  = new[]
        {
            (gasRun1, "gas/run1"), (gasRun2, "gas/run2"),
            (dtRun1, "downtown/run1"), (dtRun2, "downtown/run2")
        };

        // Words = whitespace tokens containing at least one letter or digit;
        // bullet dashes and em-dashes are punctuation, not words.
        int WordCount(string text) =>
            text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
                .Count(t => t.Any(char.IsLetterOrDigit));

        foreach (var (run, label) in all)
            foreach (var (year, prompt) in run)
            {
                int words = WordCount(prompt.Text);
                if (words >= 500)
                    errs.Add($"{label}/{year}: {words} words (limit 500)");
            }
        int unknownWords = WordCount(unknownPrompt.Text);
        if (unknownWords >= 500)
            errs.Add($"unknown/1985: {unknownWords} words (limit 500)");

        f.Add(("C11", "Every prompt is under 500 words",
            errs.Count == 0, errs.Count == 0 ? "All prompts under 500 words" : Join(errs)));
    }

    private static void DoC12(
        Dictionary<int, Prompt> gasRun1, Dictionary<int, Prompt> gasRun2,
        Dictionary<int, Prompt> dtRun1,  Dictionary<int, Prompt> dtRun2,
        Dictionary<int, EraProfile> eras,
        List<(string, string, bool, string)> f)
    {
        var errs = new List<string>();
        var runs = new[]
        {
            (gasRun1, "gas/run1"), (gasRun2, "gas/run2"),
            (dtRun1, "downtown/run1"), (dtRun2, "downtown/run2")
        };

        foreach (var (year, era) in eras.Where(e => e.Value.Photography.ColorMode == "black_and_white"))
        {
            foreach (var (run, label) in runs)
            {
                var text = run[year].Text;
                foreach (var color in era.Transportation.Cars.Colors)
                    if (System.Text.RegularExpressions.Regex.IsMatch(
                            text, $@"\b{System.Text.RegularExpressions.Regex.Escape(color)}\b",
                            System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                        errs.Add($"{label}/{year}: vehicle color '{color}' present in B&W prompt");
                if (text.Contains("Fashion palette"))
                    errs.Add($"{label}/{year}: 'Fashion palette' present in B&W prompt");
                if (text.Contains("desaturated", StringComparison.OrdinalIgnoreCase))
                    errs.Add($"{label}/{year}: 'desaturated' present in B&W prompt");
            }
        }

        f.Add(("C12", "B&W prompts contain no vehicle pool colors, no 'Fashion palette', no 'desaturated'",
            errs.Count == 0, errs.Count == 0 ? "B&W prompts are color-free" : Join(errs)));
    }

    private static void DoC13(
        Dictionary<int, Prompt> gasRun1, Dictionary<int, Prompt> gasRun2,
        Dictionary<int, Prompt> dtRun1,  Dictionary<int, Prompt> dtRun2,
        Dictionary<int, EraProfile> eras,
        List<(string, string, bool, string)> f)
    {
        var errs = new List<string>();
        var runs = new[]
        {
            (gasRun1, "gas/run1"), (gasRun2, "gas/run2"),
            (dtRun1, "downtown/run1"), (dtRun2, "downtown/run2")
        };

        foreach (var (run, label) in runs)
            foreach (var (year, prompt) in run)
            {
                if (eras[year].Photography.ColorMode == "black_and_white") continue;

                var colors = new List<string>();
                foreach (var model in prompt.SelectedVehicles)
                {
                    var prefix = $"- {model} — ";
                    var line   = prompt.Text.Split('\n').FirstOrDefault(l => l.StartsWith(prefix));
                    if (line is null)
                    {
                        errs.Add($"{label}/{year}: no color assigned for '{model}'");
                        continue;
                    }
                    colors.Add(line[prefix.Length..].Trim());
                }
                var dupes = colors.GroupBy(c => c, StringComparer.OrdinalIgnoreCase)
                                  .Where(g => g.Count() > 1).Select(g => g.Key).ToList();
                if (dupes.Any())
                    errs.Add($"{label}/{year}: duplicate vehicle colors: {string.Join(", ", dupes)}");
            }

        f.Add(("C13", "Color eras: every vehicle has a color and no color repeats within one prompt",
            errs.Count == 0, errs.Count == 0 ? "All vehicle colors unique per prompt" : Join(errs)));
    }

    private static void DoC14(
        Dictionary<int, Prompt> gasRun1, Dictionary<int, Prompt> gasRun2,
        List<(string, string, bool, string)> f)
    {
        var errs = new List<string>();

        foreach (var (run, label) in new[] { (gasRun1, "gas/run1"), (gasRun2, "gas/run2") })
        {
            var text = run[2025].Text;
            // \bEVs?\b (case-sensitive) so lowercase words like "eye-level" don't false-match
            if (System.Text.RegularExpressions.Regex.IsMatch(text, @"\bEVs?\b"))
                errs.Add($"{label}/2025: contains 'EV'");
            foreach (var term in new[] { "electric", "charger", "Lightning" })
                if (text.Contains(term, StringComparison.OrdinalIgnoreCase))
                    errs.Add($"{label}/2025: contains '{term}'");
        }

        f.Add(("C14", "Gas station 2025 prompt has no EV/electric/charger/Lightning content",
            errs.Count == 0, errs.Count == 0 ? "2025 gas prompts are fully de-electrified" : Join(errs)));
    }

    // ── Report ────────────────────────────────────────────────────────────────

    private static async Task WriteReport(
        List<(string Id, string Desc, bool Pass, string Detail)> findings,
        Dictionary<int, Prompt> gasRun1, Dictionary<int, Prompt> gasRun2,
        Dictionary<int, Prompt> dtRun1,  Dictionary<int, Prompt> dtRun2,
        ILogger logger)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Smoke Test Report");
        sb.AppendLine();
        sb.AppendLine($"Generated: {DateTimeOffset.UtcNow:o}");
        sb.AppendLine();

        // Check table
        sb.AppendLine("## Check Results");
        sb.AppendLine();
        sb.AppendLine("| Check | Description | Status | Detail |");
        sb.AppendLine("|-------|-------------|--------|--------|");
        foreach (var (id, desc, pass, detail) in findings)
        {
            var status = pass ? "✅ PASS" : "❌ FAIL";
            var safeDetail = detail.Replace("|", "\\|");
            sb.AppendLine($"| {id} | {desc} | {status} | {safeDetail} |");
        }
        sb.AppendLine();

        // Vehicle selection tables
        sb.AppendLine("## Vehicle Selections");
        sb.AppendLine();

        void AppendVehicleTable(Dictionary<int, Prompt> run, string heading)
        {
            sb.AppendLine($"### {heading}");
            sb.AppendLine("| Year | Count | Vehicles |");
            sb.AppendLine("|------|-------|----------|");
            foreach (var year in Years)
            {
                var p = run[year];
                sb.AppendLine($"| {year} | {p.SelectedVehicles.Count} | {string.Join(", ", p.SelectedVehicles)} |");
            }
            sb.AppendLine();
        }

        AppendVehicleTable(gasRun1, "gas_station / Run 1 (seed=42)");
        AppendVehicleTable(gasRun2, "gas_station / Run 2 (seed=1337)");
        AppendVehicleTable(dtRun1,  "downtown_street / Run 1 (seed=42)");
        AppendVehicleTable(dtRun2,  "downtown_street / Run 2 (seed=1337)");

        var outDir = Path.Combine("output", "smoke");
        Directory.CreateDirectory(outDir);
        await File.WriteAllTextAsync(Path.Combine(outDir, "report.md"), sb.ToString());

        // Mirror to log
        logger.LogInformation("[Smoke] Check summary:");
        foreach (var (id, _, pass, detail) in findings)
            logger.LogInformation("[Smoke]   {Id} {Status}: {Detail}",
                id, pass ? "PASS" : "FAIL", detail);
    }

    private static string Join(IEnumerable<string> items) => string.Join("; ", items);
}
