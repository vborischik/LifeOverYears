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

    private const int MaxPromptChars = 4850;

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
        DoC15(gasRun1, gasRun2, dtRun1, dtRun2, unknownPrompt, findings);
        DoC16(gasRun1, gasRun2, dtRun1, dtRun2, unknownPrompt, findings);
        DoC17(eras,                                           findings);
        DoC18(gasRun1, gasRun2, dtRun1, dtRun2, unknownPrompt, findings);
        DoC19(gasRun1, gasRun2, dtRun1, dtRun2,               findings);
        DoC20(gasRun1, gasRun2, dtRun1, dtRun2, eras,         findings);
        DoC21(gasRun1, gasRun2, dtRun1, dtRun2, eras,         findings);
        DoC22(gasRun1, gasRun2, dtRun1, dtRun2, unknownPrompt, logger, findings);
        DoC23(gasRun1, gasRun2, dtRun1, dtRun2,               findings);

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
                // Conditions are gas-station-only: abandoned forces 0 vehicles and
                // declining clamps to 1-2 there. Every other scene type must always
                // land inside its scene_content range — 0 is never legal.
                var clamped = sceneType == "gas_station" &&
                              prompt.SceneCondition is "abandoned" or "declining";
                if (sceneType != "gas_station" && actual == 0)
                    errs.Add($"{label}/{year}: count=0 for non-gas scene (condition leak)");
                else if (!clamped && (actual < sc.Vehicles.Min || actual > sc.Vehicles.Max))
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

                // Year still anchors the VEHICLES block ("no vehicle newer than 1975");
                // an abandoned era has no vehicles and therefore no anchor line.
                if (prompt.SelectedVehicles.Count > 0 &&
                    !prompt.Text.Contains($"no vehicle newer than {year}"))
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
                if (words >= 720)
                    errs.Add($"{label}/{year}: {words} words (limit 720)");
            }
        int unknownWords = WordCount(unknownPrompt.Text);
        if (unknownWords >= 720)
            errs.Add($"unknown/1985: {unknownWords} words (limit 720)");

        f.Add(("C11", "Every prompt is under 720 words (limit raised from 650 for condition, brand, traffic-flow, and pump-coupling lines)",
            errs.Count == 0, errs.Count == 0 ? "All prompts under 720 words" : Join(errs)));
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

    private static IEnumerable<(int Year, Prompt Prompt, string Label)> AllPrompts(
        Dictionary<int, Prompt> gasRun1, Dictionary<int, Prompt> gasRun2,
        Dictionary<int, Prompt> dtRun1,  Dictionary<int, Prompt> dtRun2,
        Prompt? unknownPrompt = null)
    {
        foreach (var (run, label) in new[]
        {
            (gasRun1, "gas/run1"), (gasRun2, "gas/run2"),
            (dtRun1, "downtown/run1"), (dtRun2, "downtown/run2")
        })
            foreach (var (year, prompt) in run)
                yield return (year, prompt, label);
        if (unknownPrompt is not null)
            yield return (1985, unknownPrompt, "unknown/run1");
    }

    // Empty-base populate header and the pedestrian sidewalk rule in every prompt.
    private static void DoC15(
        Dictionary<int, Prompt> gasRun1, Dictionary<int, Prompt> gasRun2,
        Dictionary<int, Prompt> dtRun1,  Dictionary<int, Prompt> dtRun2,
        Prompt unknownPrompt,
        List<(string, string, bool, string)> f)
    {
        var errs = new List<string>();
        const string populate = "populate it with the people and vehicles specified below";
        const string sidewalk = "the driving lanes stay empty of pedestrians";

        foreach (var (year, prompt, label) in AllPrompts(gasRun1, gasRun2, dtRun1, dtRun2, unknownPrompt))
        {
            if (!prompt.Text.Contains(populate))
                errs.Add($"{label}/{year}: missing populate-empty-base header");
            // An abandoned era is deserted — the PEOPLE block collapses to the
            // no-people line and carries no sidewalk rule.
            if (prompt.SceneCondition != "abandoned" && !prompt.Text.Contains(sidewalk))
                errs.Add($"{label}/{year}: missing sidewalk rule");
        }

        f.Add(("C15", "Every prompt contains the populate-empty-base header and the sidewalk rule",
            errs.Count == 0, errs.Count == 0 ? "Populate header and sidewalk rule present everywhere" : Join(errs)));
    }

    // Any prompt that has a TREES section must carry the tree-size override line.
    private static void DoC16(
        Dictionary<int, Prompt> gasRun1, Dictionary<int, Prompt> gasRun2,
        Dictionary<int, Prompt> dtRun1,  Dictionary<int, Prompt> dtRun2,
        Prompt unknownPrompt,
        List<(string, string, bool, string)> f)
    {
        var errs = new List<string>();
        const string overrideLine = "Tree sizes MUST follow this specification";

        foreach (var (year, prompt, label) in AllPrompts(gasRun1, gasRun2, dtRun1, dtRun2, unknownPrompt))
        {
            if (!prompt.Text.Contains("TREES")) continue;
            if (!prompt.Text.Contains(overrideLine))
                errs.Add($"{label}/{year}: TREES section without tree-size override line");
        }

        f.Add(("C16", "Every prompt with a TREES section contains the tree-size override line",
            errs.Count == 0, errs.Count == 0 ? "Tree-size override present in all TREES sections" : Join(errs)));
    }

    // Data validation: no specific_models entry post-dates its era.
    private static void DoC17(
        Dictionary<int, EraProfile> eras,
        List<(string, string, bool, string)> f)
    {
        var errs = new List<string>();

        foreach (var (year, era) in eras)
        {
            var models = era.Transportation.Cars.SpecificModels
                .Concat(era.Transportation.Trucks.SpecificModels);
            foreach (var model in models)
            {
                var m = System.Text.RegularExpressions.Regex.Match(model, @"^\s*(\d{4})");
                if (!m.Success)
                {
                    errs.Add($"{year}: no start year in '{model}'");
                    continue;
                }
                var start = int.Parse(m.Groups[1].Value);
                if (start > year)
                    errs.Add($"{year}: '{model}' starts {start} > era {year}");
            }
        }

        f.Add(("C17", "Every specific_models entry (cars+trucks) starts on or before its era year",
            errs.Count == 0, errs.Count == 0 ? "All model year ranges are era-valid" : Join(errs)));
    }

    private static SceneContent? ContentFor(Dictionary<int, EraProfile> eras, int year, string sceneType)
    {
        var era = eras[year];
        SceneContent? sc = null;
        if (era.SceneContent?.TryGetValue(sceneType, out sc) != true)
            era.SceneContent?.TryGetValue("default", out sc);
        return sc;
    }

    private static List<string> ExtrasLinesIn(string text, SceneContent sc) =>
        sc.Extras.Select(PromptService.StripRequiredMarker)
                 .Where(e => text.Contains($"- {e}"))
                 .ToList();

    private static readonly System.Text.RegularExpressions.Regex WindowSignsLine =
        new(@"- window signs: '[^']+', '[^']+'");

    // Window signs, sampled extras, and people_mix present in every prompt.
    private static void DoC20(
        Dictionary<int, Prompt> gasRun1, Dictionary<int, Prompt> gasRun2,
        Dictionary<int, Prompt> dtRun1,  Dictionary<int, Prompt> dtRun2,
        Dictionary<int, EraProfile> eras,
        List<(string, string, bool, string)> f)
    {
        var errs = new List<string>();

        var runs = new[]
        {
            (gasRun1, "gas_station", "gas/run1"), (gasRun2, "gas_station", "gas/run2"),
            (dtRun1, "downtown_street", "downtown/run1"), (dtRun2, "downtown_street", "downtown/run2")
        };

        foreach (var (run, sceneType, label) in runs)
            foreach (var (year, prompt) in run)
            {
                var sc = ContentFor(eras, year, sceneType);
                if (sc is null) continue;

                if (!WindowSignsLine.IsMatch(prompt.Text))
                    errs.Add($"{label}/{year}: no 'window signs:' line with two quoted signs");
                if (ExtrasLinesIn(prompt.Text, sc).Count == 0)
                    errs.Add($"{label}/{year}: no sampled extras line present");
                if (prompt.SceneCondition != "abandoned" &&
                    eras[year].PeopleMix is { Count: > 0 } mix && !mix.Any(m => prompt.Text.Contains($"- {m}")))
                    errs.Add($"{label}/{year}: no people_mix line present");
            }

        f.Add(("C20", "Every prompt has a two-sign 'window signs:' line, >=1 extras line, and a people_mix line",
            errs.Count == 0, errs.Count == 0 ? "All three sampling axes present in every prompt" : Join(errs)));
    }

    // PLACEMENT present in every prompt; within a run no pattern repeats unless the
    // relevant pool (sized by vehicle count) is exhausted.
    private static void DoC18(
        Dictionary<int, Prompt> gasRun1, Dictionary<int, Prompt> gasRun2,
        Dictionary<int, Prompt> dtRun1,  Dictionary<int, Prompt> dtRun2,
        Prompt unknownPrompt,
        List<(string, string, bool, string)> f)
    {
        var errs = new List<string>();

        // Presence in every prompt with vehicles (an abandoned era has no vehicles
        // and no PLACEMENT line by design).
        foreach (var (year, prompt, label) in AllPrompts(gasRun1, gasRun2, dtRun1, dtRun2, unknownPrompt))
            if (prompt.SelectedVehicles.Count > 0 && PlacementLine(prompt) is null)
                errs.Add($"{label}/{year}: no PLACEMENT line");

        // Per-run pattern de-duplication. Patterns can be shared between the two
        // pools and the used-set is shared across them, so replay the run's draws
        // in year order: a repeat is only legal once every pattern in that draw's
        // pool has already been used.
        void CheckRun(Dictionary<int, Prompt> run, string label)
        {
            var used = new HashSet<string>();
            foreach (var year in Years)
            {
                if (!run.TryGetValue(year, out var prompt) || prompt.SelectedVehicles.Count == 0)
                    continue;
                var line = PlacementLine(prompt);
                if (line is null) continue; // already reported by the presence check

                var pool    = GenerationContext.PlacementPoolFor(prompt.SelectedVehicles.Count);
                var pattern = pool.FirstOrDefault(line.Contains);
                if (pattern is null)
                {
                    errs.Add($"{label}/{year}: PLACEMENT line matches no pattern in its pool");
                    continue;
                }
                if (used.Contains(pattern) && pool.Any(p => !used.Contains(p)))
                    errs.Add($"{label}/{year}: pattern repeated before its pool was exhausted");
                used.Add(pattern);
            }
        }

        CheckRun(gasRun1, "gas/run1");
        CheckRun(gasRun2, "gas/run2");
        CheckRun(dtRun1,  "downtown/run1");
        CheckRun(dtRun2,  "downtown/run2");

        f.Add(("C18", "Every prompt has a PLACEMENT line; no repeated pattern per run unless the pool is exhausted",
            errs.Count == 0, errs.Count == 0 ? "Placement present and de-duplicated per pool" : Join(errs)));
    }

    private static string? PlacementLine(Prompt prompt) =>
        prompt.Text.Split('\n').FirstOrDefault(l => l.StartsWith("PLACEMENT:"))?.Trim();

    // No descriptive-adjective-as-signage leaks; {DINER_NAME} resolved and stable per run.
    private static void DoC19(
        Dictionary<int, Prompt> gasRun1, Dictionary<int, Prompt> gasRun2,
        Dictionary<int, Prompt> dtRun1,  Dictionary<int, Prompt> dtRun2,
        List<(string, string, bool, string)> f)
    {
        var errs = new List<string>();
        // 'aging'/'corporate' within two words before a business type reads as a sign.
        var adjacency = new System.Text.RegularExpressions.Regex(
            @"\b(aging|corporate)\b(?:\s+\S+){0,2}\s+(diner|bank|store|shop|market|pharmacy|cafe|salon|grocery|hardware|bakery|deli)\b",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        foreach (var (year, prompt, label) in AllPrompts(gasRun1, gasRun2, dtRun1, dtRun2))
        {
            var text = prompt.Text;
            if (text.Contains("the same local diner", StringComparison.OrdinalIgnoreCase) ||
                text.Contains("the same diner", StringComparison.OrdinalIgnoreCase))
                errs.Add($"{label}/{year}: generic 'the same diner' reference");
            if (text.Contains("{DINER_NAME}"))
                errs.Add($"{label}/{year}: unresolved {{DINER_NAME}} token");
            var adj = adjacency.Match(text);
            if (adj.Success)
                errs.Add($"{label}/{year}: descriptive adjective adjacent to business type ('{adj.Value.Trim()}')");
        }

        // Within a run the resolved diner name must be identical wherever it appears.
        void CheckName(Dictionary<int, Prompt> run, string label)
        {
            var names = run.Values
                .SelectMany(p => GenerationContext.DinerNames.Where(n => p.Text.Contains(n)))
                .Distinct()
                .ToList();
            if (names.Count > 1)
                errs.Add($"{label}: diner name differs across eras: {string.Join(", ", names)}");
        }

        CheckName(gasRun1, "gas/run1");
        CheckName(gasRun2, "gas/run2");
        CheckName(dtRun1,  "downtown/run1");
        CheckName(dtRun2,  "downtown/run2");

        f.Add(("C19", "No descriptive-as-signage leaks; {DINER_NAME} resolved and identical across a run",
            errs.Count == 0, errs.Count == 0 ? "Business names clean and diner name stable" : Join(errs)));
    }

    // Run-to-run sampling variance in extras / window signs.
    private static void DoC21(
        Dictionary<int, Prompt> gasRun1, Dictionary<int, Prompt> gasRun2,
        Dictionary<int, Prompt> dtRun1,  Dictionary<int, Prompt> dtRun2,
        Dictionary<int, EraProfile> eras,
        List<(string, string, bool, string)> f)
    {
        var errs = new List<string>();

        void Check(Dictionary<int, Prompt> run1, Dictionary<int, Prompt> run2, string sceneType, string label)
        {
            int differing = 0;
            foreach (var year in Years)
            {
                var sc = ContentFor(eras, year, sceneType);
                if (sc is null) continue;

                string Signature(Prompt p) =>
                    (WindowSignsLine.Match(p.Text).Value, string.Join("|", ExtrasLinesIn(p.Text, sc))).ToString();

                if (Signature(run1[year]) != Signature(run2[year]))
                    differing++;
            }
            if (differing < 3)
                errs.Add($"{label}: only {differing}/6 years differ in extras/window signs (need >=3)");
        }

        Check(gasRun1, gasRun2, "gas_station",     "gas_station");
        Check(dtRun1,  dtRun2,  "downtown_street", "downtown_street");

        f.Add(("C21", "Run1 vs Run2: >=3 of 6 years differ in sampled extras or window signs",
            errs.Count == 0, errs.Count == 0 ? "Sufficient sampling variance between seeds" : Join(errs)));
    }

    // Prompt length budget in characters, over every prompt including the unknown
    // fallback. Lengths are always logged so the longest blocks stay visible.
    private static void DoC22(
        Dictionary<int, Prompt> gasRun1, Dictionary<int, Prompt> gasRun2,
        Dictionary<int, Prompt> dtRun1,  Dictionary<int, Prompt> dtRun2,
        Prompt unknownPrompt,
        ILogger logger,
        List<(string, string, bool, string)> f)
    {
        var errs    = new List<string>();
        var lengths = new List<string>();

        foreach (var (year, prompt, label) in AllPrompts(gasRun1, gasRun2, dtRun1, dtRun2, unknownPrompt))
        {
            int len = prompt.Text.Length;
            lengths.Add($"{label} {year}={len}");
            if (len > MaxPromptChars)
                errs.Add($"C22 FAIL: {label} year {year} prompt is {len} chars (max {MaxPromptChars})");
        }

        logger.LogInformation("[Smoke] C22 lengths: {Lengths}", string.Join(" ", lengths));

        f.Add(("C22", $"Every prompt is at most {MaxPromptChars} characters",
            errs.Count == 0, errs.Count == 0 ? $"All prompts within {MaxPromptChars} chars" : Join(errs)));
    }

    // Conditions are gas-station-only: downtown prompts must never carry the
    // zero-out lines or any condition other than "thriving", while gas prompts
    // must honor what their sampled condition implies.
    private static void DoC23(
        Dictionary<int, Prompt> gasRun1, Dictionary<int, Prompt> gasRun2,
        Dictionary<int, Prompt> dtRun1,  Dictionary<int, Prompt> dtRun2,
        List<(string, string, bool, string)> f)
    {
        var errs = new List<string>();
        const string noVehicles = "NO vehicles anywhere";
        const string noPeople   = "NO people anywhere";

        foreach (var (run, label) in new[] { (dtRun1, "downtown/run1"), (dtRun2, "downtown/run2") })
            foreach (var (year, prompt) in run)
            {
                if (prompt.Text.Contains(noVehicles))
                    errs.Add($"{label}/{year}: contains '{noVehicles}'");
                if (prompt.Text.Contains(noPeople))
                    errs.Add($"{label}/{year}: contains '{noPeople}'");
                if (prompt.SceneCondition != "thriving")
                    errs.Add($"{label}/{year}: SceneCondition '{prompt.SceneCondition}' (expected 'thriving')");
            }

        var peopleLine = new System.Text.RegularExpressions.Regex(@"EXACTLY (\d+) people");
        foreach (var (run, label) in new[] { (gasRun1, "gas/run1"), (gasRun2, "gas/run2") })
            foreach (var (year, prompt) in run)
            {
                if (prompt.SceneCondition == "abandoned")
                {
                    if (!prompt.Text.Contains(noVehicles))
                        errs.Add($"{label}/{year}: abandoned but missing '{noVehicles}'");
                    if (!prompt.Text.Contains(noPeople))
                        errs.Add($"{label}/{year}: abandoned but missing '{noPeople}'");
                }
                else if (prompt.SceneCondition == "declining")
                {
                    var m = peopleLine.Match(prompt.Text);
                    if (!m.Success || int.Parse(m.Groups[1].Value) is < 2 or > 4)
                        errs.Add($"{label}/{year}: declining but people count is {(m.Success ? m.Groups[1].Value : "missing")} (expected 2-4)");
                    if (prompt.SelectedVehicles.Count is < 1 or > 2)
                        errs.Add($"{label}/{year}: declining but vehicle count is {prompt.SelectedVehicles.Count} (expected 1-2)");
                }
            }

        f.Add(("C23", "Conditions stay gas-station-only: downtown always thriving with no zero-out lines; gas abandoned/declining prompts honor their counts",
            errs.Count == 0, errs.Count == 0 ? "No condition leakage; gas condition counts honored" : Join(errs)));
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
