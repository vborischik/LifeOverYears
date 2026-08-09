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

    private const int MaxPromptChars = 6000; // raised from 5300: the priority order block and the per-era SIGNAGE RESTRICTION whitelist added real length

    private static readonly JsonSerializerOptions WriteJson = new() { WriteIndented = true };

    // Matches ANY unresolved {TOKEN} — template placeholders (e.g. {SCENE_BLOCK})
    // as well as business-name tokens (e.g. {DINER_NAME}) — so a new token added
    // anywhere in the future is caught automatically without updating a whitelist.
    private static readonly System.Text.RegularExpressions.Regex UnresolvedTokenPattern =
        new(@"\{[A-Z_]+\}");

    // Every business-name pool, keyed by its template token, for token-resolution checks.
    private static readonly (string Token, IReadOnlyList<string> Pool)[] BusinessNamePools =
    {
        ("{DINER_NAME}",         GenerationContext.DinerNames),
        ("{DRUGSTORE_NAME}",     GenerationContext.DrugStoreNames),
        ("{HARDWARE_NAME}",      GenerationContext.HardwareNames),
        ("{FIVE_AND_DIME_NAME}", GenerationContext.FiveAndDimeNames),
        ("{BARBER_NAME}",        GenerationContext.BarberNames),
        ("{SHOE_REPAIR_NAME}",   GenerationContext.ShoeRepairNames),
        ("{APPLIANCE_NAME}",     GenerationContext.ApplianceNames),
        ("{DRESS_SHOP_NAME}",    GenerationContext.DressShopNames),
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
        var gasScene       = MakeGasStationScene();
        var downtownScene  = MakeDowntownScene();
        var stripMallScene = MakeStripMallScene();
        var autoRepairScene = MakeAutoRepairScene();
        var unknownScene   = MakeUnknownScene();

        // Load all era profiles
        var eras = new Dictionary<int, EraProfile>();
        foreach (var year in Years)
            eras[year] = await dataService.LoadEraProfileAsync(year);

        // b) Run prompts: 3 scenes × 2 runs × 6 years
        var gasRun1 = await BuildRun(promptService, gasScene,       eras, 42,   Years);
        var gasRun2 = await BuildRun(promptService, gasScene,       eras, 1337, Years);
        var dtRun1  = await BuildRun(promptService, downtownScene,  eras, 42,   Years);
        var dtRun2  = await BuildRun(promptService, downtownScene,  eras, 1337, Years);
        var smRun1  = await BuildRun(promptService, stripMallScene, eras, 42,   Years);
        var arRun1  = await BuildRun(promptService, autoRepairScene, eras, 42,   Years);
        var smRun2  = await BuildRun(promptService, stripMallScene, eras, 1337, Years);
        var arRun2  = await BuildRun(promptService, autoRepairScene, eras, 1337, Years);

        // c) Unknown scene — 1985 only, must not throw
        var unknownCtx    = new GenerationContext { Random = new Random(42), TotalEras = 1 };
        var unknownPrompt = await promptService.BuildAsync(unknownScene, eras[1985], unknownCtx);
        // SceneType "unknown" has no dedicated key → fell back to "default" scene_content
        logger.LogWarning("[Smoke] SceneType 'unknown' fell back to default scene_content for era 1985 — fallback path exercised");

        // Save output
        await SaveRun(gasScene.SceneType,       1, gasRun1);
        await SaveRun(gasScene.SceneType,       2, gasRun2);
        await SaveRun(downtownScene.SceneType,  1, dtRun1);
        await SaveRun(downtownScene.SceneType,  2, dtRun2);
        await SaveRun(stripMallScene.SceneType, 1, smRun1);
        await SaveRun(autoRepairScene.SceneType, 1, arRun1);
        await SaveRun(stripMallScene.SceneType, 2, smRun2);
        await SaveRun(autoRepairScene.SceneType, 2, arRun2);
        await SaveRun(unknownScene.SceneType,   1, new Dictionary<int, Prompt> { { 1985, unknownPrompt } });

        // d) Checks C1–C25
        // Pass is tri-state: true PASS, false FAIL, null DISABLED. A parked check
        // reports DISABLED so it stays visible in the report — never a silent PASS.
        var findings = new List<(string Id, string Desc, bool? Pass, string Detail)>();

        DoC1 (eras,                                                            findings);
        DoC2 (gasRun1, gasRun2, dtRun1, dtRun2, smRun1, smRun2, arRun1, arRun2, unknownPrompt,  findings);
        DoC3 (gasRun1, gasRun2, dtRun1, dtRun2, smRun1, smRun2, arRun1, arRun2,                 findings);
        DoC4 (gasRun1, gasRun2, dtRun1, dtRun2, smRun1, smRun2, arRun1, arRun2, eras,          findings);
        DoC5 (gasRun1, gasRun2, dtRun1, dtRun2, smRun1, smRun2, arRun1, arRun2,                findings);
        DoC6 (gasRun1, gasRun2, gasScene,                                      findings);
        DoC7 (gasRun1, gasRun2, dtRun1, dtRun2,                                findings);
        DoC8 (gasRun1, gasRun2, dtRun1, dtRun2, eras,                          findings);
        DoC9 (gasRun1, gasScene, dtRun1, downtownScene, smRun1, stripMallScene, arRun1, autoRepairScene, findings);
        DoC10(gasRun1, gasRun2, dtRun1, dtRun2, smRun1, smRun2, arRun1, arRun2,                findings);
        DoC11(gasRun1, gasRun2, dtRun1, dtRun2, smRun1, smRun2, arRun1, arRun2, unknownPrompt, findings);
        DoC12(gasRun1, gasRun2, dtRun1, dtRun2, smRun1, smRun2, arRun1, arRun2, eras,          findings);
        DoC13(gasRun1, gasRun2, dtRun1, dtRun2, smRun1, smRun2, arRun1, arRun2, eras,          findings);
        DoC14(gasRun1, gasRun2,                                               findings);
        DoC15(gasRun1, gasRun2, dtRun1, dtRun2, smRun1, smRun2, arRun1, arRun2, unknownPrompt, findings);
        DoC16(gasRun1, gasRun2, dtRun1, dtRun2, smRun1, smRun2, arRun1, arRun2, unknownPrompt, findings);
        DoC17(eras,                                                           findings);
        DoC18(gasRun1, gasRun2, dtRun1, dtRun2, smRun1, smRun2, arRun1, arRun2, unknownPrompt, findings);
        DoC19(gasRun1, gasRun2, dtRun1, dtRun2, smRun1, smRun2, arRun1, arRun2,                findings);
        DoC20(gasRun1, gasRun2, dtRun1, dtRun2, smRun1, smRun2, arRun1, arRun2, eras,          findings);
        DoC21(gasRun1, gasRun2, dtRun1, dtRun2, smRun1, smRun2, arRun1, arRun2, eras,          findings);
        DoC22(gasRun1, gasRun2, dtRun1, dtRun2, smRun1, smRun2, arRun1, arRun2, unknownPrompt, logger, findings);
        DoC23(gasRun1, gasRun2, dtRun1, dtRun2, smRun1, smRun2, arRun1, arRun2, unknownPrompt, findings);
        DoC24(gasRun1, gasRun2, dtRun1, dtRun2, smRun1, smRun2, arRun1, arRun2,                findings);
        DoC25(gasRun1, gasRun2, dtRun1, dtRun2, smRun1, smRun2, arRun1, arRun2, eras,          findings);
        await DoC26(dataService, eras,                                       findings);
        await DoC27(dataService, gasRun1, gasRun2, dtRun1, dtRun2, smRun1, smRun2, arRun1, arRun2, findings);
        DoC28(gasRun1, gasRun2, dtRun1, dtRun2, smRun1, smRun2, arRun1, arRun2, eras,          findings);
        DoC29(findings);
        DoC30(gasRun1, gasRun2, dtRun1, dtRun2, smRun1, smRun2, arRun1, arRun2, unknownPrompt, findings);
        DoC31(dtRun1, dtRun2,                                                                  findings);
        DoC32(gasRun1, gasRun2, dtRun1, dtRun2, smRun1, smRun2, arRun1, arRun2, unknownPrompt, findings);
        DoC33(findings);
        DoC34(eras, findings);
        DoC35(eras, findings);
        DoC36(dtRun1, dtRun2, gasRun1, smRun1, arRun1, unknownPrompt, findings);
        await DoC37(promptService, dataService, gasScene, downtownScene, stripMallScene, autoRepairScene, gasRun1, findings);
        await DoC38(promptService, gasScene, downtownScene, stripMallScene, autoRepairScene,
            gasRun1, gasRun2, dtRun1, dtRun2, smRun1, smRun2, arRun1, arRun2, unknownPrompt, findings);
        DoC39(gasRun1, gasRun2, dtRun1, dtRun2, smRun1, smRun2, arRun1, arRun2, unknownPrompt, findings);
        await DoC40(dataService, gasRun1, gasRun2, dtRun1, dtRun2, smRun1, smRun2, arRun1, arRun2, unknownPrompt, findings);
        await DoC41(dataService, gasScene, downtownScene, stripMallScene, autoRepairScene, unknownScene, findings);
        await DoC42(promptService, eras, findings);

        // e) Report
        await WriteReport(findings, gasRun1, gasRun2, dtRun1, dtRun2, logger);

        int passed  = findings.Count(f => f.Pass == true);
        int failed  = findings.Count(f => f.Pass == false);
        int disabled = findings.Count(f => f.Pass is null);
        int total    = findings.Count - disabled;   // disabled checks assert nothing
        var disabledNote = disabled > 0 ? $", {disabled} disabled" : "";
        Console.WriteLine();
        Console.WriteLine($"Smoke test: {passed}/{total} checks passed{disabledNote}" +
                          (failed == 0 ? "" : " — FAILURES DETECTED"));
        Console.WriteLine("See output/smoke/report.md for full details.");
        logger.LogInformation("[Smoke] Done: {Passed}/{Total} checks passed, {Disabled} disabled",
            passed, total, disabled);

        return failed == 0 ? 0 : 1;
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

    private static SceneDna MakeStripMallScene() => new(
        Id:        "smoke-strip-mall",
        CreatedAt: "2025-01-01T00:00:00Z",
        SceneType: "strip_mall",
        Camera: new Camera(Height: "eye-level", Direction: "street-facing", Fov: 78),
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
                    Type:      "single-story retail row under a continuous overhang",
                    Position:  "along the back of the lot",
                    Stories:   1,
                    Materials: ["brick veneer", "EIFS stucco panels"],
                    Roof:      "flat with continuous overhang",
                    Setback:   "60 feet from road"),
                new Building(
                    Type:      "detached end-cap retail unit",
                    Position:  "north end of the lot",
                    Stories:   1,
                    Materials: ["brick veneer", "metal fascia panels"],
                    Roof:      "flat parapet",
                    Setback:   "40 feet from road")
            ],
            Driveways: ["north entrance apron", "south entrance apron"],
            Parking:   ""),
        Environment: new Environment(
            Terrain:   "suburban flat",
            Utilities: ["overhead power lines on wooden poles", "transformer on rear property line pole"],
            Trees:
            [
                new Tree(Position: "parking lot island near entrance", Size: "small",  Type: "crepe myrtle"),
                new Tree(Position: "landscape strip along the road",   Size: "medium", Type: "maple"),
                new Tree(Position: "back property line",                Size: "large",  Type: "oak")
            ],
            Landscape: ["parking lot islands with curbing", "landscape strip along the road frontage", "painted stall striping"]),
        ImmutableElements:
        [
            "continuous storefront overhang along the retail row",
            "pylon sign base at the road frontage",
            "parking lot islands"
        ]);

    private static SceneDna MakeAutoRepairScene() => new(
        Id:        "smoke-auto-repair",
        CreatedAt: "2025-01-01T00:00:00Z",
        SceneType: "auto_repair",
        Camera: new Camera(Height: "eye-level", Direction: "street-facing", Fov: 76),
        Geometry: new Geometry(
            Roads:
            [
                new Road(
                    Type:     "residential",
                    Lanes:    2,
                    Markings: ["center line", "crosswalk"],
                    Surface:  "asphalt")
            ],
            Sidewalks: true,
            Curbs:     true,
            Buildings:
            [
                new Building(
                    Type:      "small office with plate glass",
                    Position:  "corner of the lot",
                    Stories:   1,
                    Materials: ["brick veneer", "plate glass"],
                    Roof:      "flat parapet",
                    Setback:   "20 feet from road"),
                new Building(
                    Type:      "service bay row under one continuous roof",
                    Position:  "rear of the lot",
                    Stories:   1,
                    Materials: ["concrete block", "corrugated metal"],
                    Roof:      "flat",
                    Setback:   "40 feet from road")
            ],
            Driveways: ["corner entrance apron"],
            Parking:   "concrete apron in front of the bays"),
        Environment: new Environment(
            Terrain:   "suburban flat",
            Utilities: ["overhead power lines", "utility pole at the corner"],
            Trees:
            [
                new Tree(Position: "street corner", Size: "medium", Type: "maple"),
                new Tree(Position: "side yard",      Size: "small",  Type: "elm")
            ],
            Landscape: ["narrow grass strip along the road", "gravel edge along the apron"]),
        ImmutableElements:
        [
            "painted sign band across the parapet",
            "corner lot at the intersection",
            "concrete apron in front of the service bays"
        ]);

    private static SceneDna MakeMallScene() => new(
        Id:        "smoke-mall",
        CreatedAt: "2025-01-01T00:00:00Z",
        SceneType: "mall",
        Camera: new Camera(Height: "eye-level", Direction: "facade", Fov: 82),
        Geometry: new Geometry(
            Roads:
            [
                new Road(
                    Type:     "commercial arterial",
                    Lanes:    4,
                    Markings: ["yellow center line", "white edge lines", "turn lane arrows"],
                    Surface:  "asphalt")
            ],
            Sidewalks: false,
            Curbs:     false,
            Buildings:
            [
                new Building(
                    Type:      "enclosed mall box",
                    Position:  "rear of the lot",
                    Stories:   1,
                    Materials: ["concrete panels", "brick base course"],
                    Roof:      "flat",
                    Setback:   "150 feet from road")
            ],
            Driveways: ["main entrance apron", "side entrance apron"],
            Parking:   "large surface lot surrounding the building"),
        Environment: new Environment(
            Terrain:   "suburban flat",
            Utilities: ["tall parking lot light poles", "overhead power lines at the lot edge"],
            Trees:
            [
                new Tree(Position: "planter island near entrance", Size: "small",  Type: "honey locust"),
                new Tree(Position: "lot perimeter",                 Size: "medium", Type: "maple")
            ],
            Landscape: ["long planter islands splitting the parking rows", "painted stall striping"]),
        ImmutableElements:
        [
            "windowless end-cap facade",
            "recessed main entrance with canopy",
            "rows of tall lot light poles"
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
        var ctx     = new GenerationContext { Random = new Random(seed), TotalEras = years.Length };
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
        List<(string, string, bool?, string)> f)
    {
        var errs = new List<string>();
        string[] requiredKeys = { "downtown_street", "gas_station", "strip_mall", "auto_repair", "default" };
        string[] poolsRequiring20 = { "downtown_street", "gas_station", "strip_mall", "auto_repair" };
        const int minPoolSize = 20;

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

            if (era.PeopleMix is null || era.PeopleMix.Count < minPoolSize)
                errs.Add($"{year}: people_mix has {era.PeopleMix?.Count ?? 0} entries, expected >= {minPoolSize}");

            foreach (var key in poolsRequiring20)
                if (era.SceneContent.TryGetValue(key, out var scene) &&
                    scene.PeopleActivities.Count < minPoolSize)
                    errs.Add($"{year}: scene_content.{key}.people_activities has {scene.PeopleActivities.Count} entries, expected >= {minPoolSize}");
        }

        f.Add(("C1", "Era deserialization: scene_content has required keys, color_mode present, people pools >= 20",
            errs.Count == 0, errs.Count == 0 ? "All 6 eras OK" : Join(errs)));
    }

    private static void DoC2(
        Dictionary<int, Prompt> gasRun1, Dictionary<int, Prompt> gasRun2,
        Dictionary<int, Prompt> dtRun1,  Dictionary<int, Prompt> dtRun2,
        Dictionary<int, Prompt> smRun1,  Dictionary<int, Prompt> smRun2,
        Dictionary<int, Prompt> arRun1,  Dictionary<int, Prompt> arRun2,
        Prompt unknownPrompt,
        List<(string, string, bool?, string)> f)
    {
        var errs = new List<string>();
        var all  = gasRun1.Values.Concat(gasRun2.Values)
                          .Concat(dtRun1.Values).Concat(dtRun2.Values)
                          .Concat(smRun1.Values).Concat(smRun2.Values)
                          .Concat(arRun1.Values).Concat(arRun2.Values)
                          .Append(unknownPrompt);

        foreach (var p in all)
            foreach (System.Text.RegularExpressions.Match m in UnresolvedTokenPattern.Matches(p.Text))
                errs.Add($"year={p.Year}: found unresolved token '{m.Value}'");

        f.Add(("C2", "No unresolved {TOKEN} of any kind remains in any prompt",
            errs.Count == 0, errs.Count == 0 ? "All placeholders resolved" : Join(errs)));
    }

    private static void DoC3(
        Dictionary<int, Prompt> gasRun1, Dictionary<int, Prompt> gasRun2,
        Dictionary<int, Prompt> dtRun1,  Dictionary<int, Prompt> dtRun2,
        Dictionary<int, Prompt> smRun1,  Dictionary<int, Prompt> smRun2,
        Dictionary<int, Prompt> arRun1,  Dictionary<int, Prompt> arRun2,
        List<(string, string, bool?, string)> f)
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
        Check(smRun1,  "strip_mall/run1");
        Check(arRun1,  "auto_repair/run1");
        Check(smRun2,  "strip_mall/run2");
        Check(arRun2,  "auto_repair/run2");

        f.Add(("C3", "No vehicle model reuse within each run (dedup invariant)",
            errs.Count == 0, errs.Count == 0 ? "No duplicates in any run" : "Duplicates: " + Join(errs)));
    }

    private static void DoC4(
        Dictionary<int, Prompt> gasRun1, Dictionary<int, Prompt> gasRun2,
        Dictionary<int, Prompt> dtRun1,  Dictionary<int, Prompt> dtRun2,
        Dictionary<int, Prompt> smRun1,  Dictionary<int, Prompt> smRun2,
        Dictionary<int, Prompt> arRun1,  Dictionary<int, Prompt> arRun2,
        Dictionary<int, EraProfile> eras,
        List<(string, string, bool?, string)> f)
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
                if (sc?.Vehicles is not { } vehicleRange) continue;

                int actual = prompt.SelectedVehicles.Count;
                // Conditions apply to gas_station, downtown_street and strip_mall:
                // abandoned forces 0 vehicles, declining/squatted clamp to a small
                // range. default/unknown never sample a condition, so 0 is never
                // legal for them — a bare 0 there would be a condition leak.
                var supportsCondition = sceneType is "gas_station" or "downtown_street" or "strip_mall" or "auto_repair";
                var clamped = supportsCondition &&
                              prompt.SceneCondition is "abandoned" or "declining" or "squatted";
                if (!supportsCondition && actual == 0)
                    errs.Add($"{label}/{year}: count=0 for a scene type without conditions (condition leak)");
                else if (!clamped && (actual < vehicleRange.Min || actual > vehicleRange.Max))
                    errs.Add($"{label}/{year}: count={actual} outside [{vehicleRange.Min},{vehicleRange.Max}]");

                int linesInText = prompt.SelectedVehicles.Count(m => prompt.Text.Contains($"- {m}"));
                if (linesInText != actual)
                    errs.Add($"{label}/{year}: VEHICLES section has {linesInText} model lines, SelectedVehicles.Count={actual}");
            }
        }

        Check(gasRun1, "gas_station",     "gas_station/run1");
        Check(gasRun2, "gas_station",     "gas_station/run2");
        Check(dtRun1,  "downtown_street", "downtown_street/run1");
        Check(dtRun2,  "downtown_street", "downtown_street/run2");
        Check(smRun1,  "strip_mall",      "strip_mall/run1");
        Check(arRun1,  "auto_repair",     "auto_repair/run1");
        Check(smRun2,  "strip_mall",      "strip_mall/run2");
        Check(arRun2,  "auto_repair",     "auto_repair/run2");

        f.Add(("C4", "Vehicle count in range and VEHICLES section lines match SelectedVehicles.Count",
            errs.Count == 0, errs.Count == 0 ? "All vehicle counts correct" : Join(errs)));
    }

    private static void DoC5(
        Dictionary<int, Prompt> gasRun1, Dictionary<int, Prompt> gasRun2,
        Dictionary<int, Prompt> dtRun1,  Dictionary<int, Prompt> dtRun2,
        Dictionary<int, Prompt> smRun1,  Dictionary<int, Prompt> smRun2,
        Dictionary<int, Prompt> arRun1,  Dictionary<int, Prompt> arRun2,
        List<(string, string, bool?, string)> f)
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
        Check(smRun1,  smRun2,  "strip_mall");
        Check(arRun1,  arRun2,  "auto_repair");

        f.Add(("C5", "Run1 vs Run2: ≥3 years differ in vehicles; no year has identical full text",
            errs.Count == 0, errs.Count == 0 ? "Sufficient variance between seeds" : Join(errs)));
    }

    private static void DoC6(
        Dictionary<int, Prompt> gasRun1, Dictionary<int, Prompt> gasRun2,
        SceneDna scene,
        List<(string, string, bool?, string)> f)
    {
        var errs = new List<string>();

        void CheckLadder(Dictionary<int, Prompt> run, string label)
        {
            // 1975 (5 decades back): the small and medium trees in gasScene both
            // land in the youngest bucket, but at distinct percentages — unlike
            // the old absolute-rung ladder, the large tree does NOT clamp to the
            // same floor here (that flattening was exactly the bug being fixed).
            if (!run[1975].Text.Contains("a young tree, only about 10% of its canopy in the base image, thin trunk"))
                errs.Add($"{label}/1975: missing small-tree young-canopy phrasing (10%)");
            if (!run[1975].Text.Contains("a young tree, only about 30% of its canopy in the base image, thin trunk"))
                errs.Add($"{label}/1975: missing medium-tree young-canopy phrasing (30%)");

            // 2005 (2 decades back): the mature (large) tree reads "clearly smaller".
            if (!run[2005].Text.Contains("clearly smaller than in the base image — about 80% of its canopy there, thinner trunk"))
                errs.Add($"{label}/2005: missing mature-tree mid-life phrasing (80%)");

            // 2025 (source year, 0 decades back): the base image already shows the
            // trees at their current size, so there is no TREES section at all —
            // an instruction there could only contradict the base.
            if (run[2025].Text.Contains("TREES"))
                errs.Add($"{label}/2025: source year still emits a TREES section");
            if (run[2025].Text.Contains("Tree sizes MUST follow this specification"))
                errs.Add($"{label}/2025: source year still emits the tree-size override line");

            // The anchor is the base image each era request actually uploads —
            // never the original photo, which those calls never see.
            foreach (var year in new[] { 1975, 1985, 1995, 2005, 2015 })
            {
                if (!run[year].Text.Contains("the base image"))
                    errs.Add($"{label}/{year}: tree sizes not anchored to 'the base image'");
                if (run[year].Text.Contains("source photo"))
                    errs.Add($"{label}/{year}: tree spec still references the 'source photo'");
            }
        }

        CheckLadder(gasRun1, "gas_station/run1");
        CheckLadder(gasRun2, "gas_station/run2");

        // Every tree position+type must appear verbatim in each era that still
        // carries a TREES section. The era PRESERVE block deliberately omits
        // trees (see BuildPreserveBlock's includeTrees), so the source year —
        // which has no TREES section either — names no trees at all: the base
        // image already shows them, and there is nothing to restate.
        foreach (var tree in scene.Environment.Trees)
        {
            var expected = $"{tree.Type} tree at {tree.Position}";
            foreach (var (run, label) in new[] { (gasRun1, "gas_station/run1"), (gasRun2, "gas_station/run2") })
                foreach (var (year, prompt) in run)
                {
                    var present = prompt.Text.Contains(expected);
                    if (year == 2025 && present)
                        errs.Add($"{label}/2025: source year still names tree '{expected}'");
                    else if (year != 2025 && !present)
                        errs.Add($"{label}/{year}: missing tree '{expected}'");
                }
        }

        // A mature (large) source tree must render a distinct size label in each
        // era that has a TREES section (all but the source year).
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
                    if (year == 2025)
                    {
                        if (line is not null)
                            errs.Add($"{label}/2025: source year still carries a tree size label");
                        continue;
                    }
                    if (line is null)
                        errs.Add($"{label}/{year}: no size label line for mature tree '{matureTree.Type}'");
                    else
                        labels.Add(line[linePrefix.Length..].Trim());
                }
                if (labels.Distinct().Count() != labels.Count)
                    errs.Add($"{label}: mature tree size labels not distinct across years: {string.Join(" | ", labels)}");
            }
        }

        f.Add(("C6", "Tree canopy proportion vs. the base image (distinct per era for mature trees, size-relative), and no TREES section or tree mention in the source year",
            errs.Count == 0, errs.Count == 0 ? "Tree ladder and source-year omission correct" : Join(errs)));
    }

    private static void DoC7(
        Dictionary<int, Prompt> gasRun1, Dictionary<int, Prompt> gasRun2,
        Dictionary<int, Prompt> dtRun1,  Dictionary<int, Prompt> dtRun2,
        List<(string, string, bool?, string)> f)
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
        List<(string, string, bool?, string)> f)
    {
        var errs = new List<string>();

        // Gas station: fuel price is emitted unconditionally EXCEPT on a dead-board
        // era (abandoned/squatted), where the station shows a stripped sign with no
        // price at all — check both runs every year, skipping dead-board eras.
        foreach (var year in Years)
        {
            var price = eras[year].Transportation.Fuel.AveragePricePerGallon;
            foreach (var (run, label) in new[] { (gasRun1, "gas/run1"), (gasRun2, "gas/run2") })
            {
                if (run[year].SceneCondition is "abandoned" or "squatted") continue;
                if (!run[year].Text.Contains(price))
                    errs.Add($"{label}/{year}: fuel price '{price}' not found");
            }
        }

        // Downtown: coffee price is sampled — require at least one run per year to contain it
        foreach (var year in Years)
        {
            var coffeeStr = DowntownCoffeePrices[year];
            // A closed-down block shows no prices at all, so only live eras can
            // carry the anchor; a year derelict in both runs is legitimately empty.
            var live = new[] { dtRun1[year], dtRun2[year] }
                .Where(p => p.SceneCondition != "abandoned" && p.SceneCondition != "squatted")
                .ToList();
            if (live.Count == 0) continue;
            if (!live.Any(p => p.Text.Contains(coffeeStr)))
                errs.Add($"downtown/{year}: coffee price '{coffeeStr}' absent from all live runs (sampling miss)");
        }

        f.Add(("C8", "Gas station fuel prices always present; downtown coffee price in ≥1 run per year",
            errs.Count == 0, errs.Count == 0 ? "All price anchors found" : Join(errs)));
    }

    private static void DoC9(
        Dictionary<int, Prompt> gasRun1, SceneDna gasScene,
        Dictionary<int, Prompt> dtRun1,  SceneDna dtScene,
        Dictionary<int, Prompt> smRun1,  SceneDna smScene,
        Dictionary<int, Prompt> arRun1,  SceneDna arScene,
        List<(string, string, bool?, string)> f)
    {
        // PARKED: this check asserts the generated per-era PRESERVE block, which
        // BuildAsync no longer emits — the era path now uses the short fixed
        // instruction. Restore together with the BuildPreserveBlock call in
        // PromptService line 89 by uncommenting the body below and the f.Add
        // beneath it, then deleting the SKIP report.
        //
        // var errs = new List<string>();
        //
        // void Check(Dictionary<int, Prompt> run, SceneDna scene, string label)
        // {
        //     foreach (var (year, prompt) in run)
        //     {
        //         foreach (var b in scene.Geometry.Buildings)
        //             if (!prompt.Text.Contains(b.Type))
        //                 errs.Add($"{label}/{year}: building type '{b.Type}' not in PRESERVE");
        //         foreach (var el in scene.ImmutableElements)
        //             if (!prompt.Text.Contains(el))
        //                 errs.Add($"{label}/{year}: immutable element '{el}' not in PRESERVE");
        //     }
        // }
        //
        // Check(gasRun1, gasScene, "gas_station/run1");
        // Check(dtRun1,  dtScene,  "downtown_street/run1");
        // Check(smRun1,  smScene,  "strip_mall/run1");
        // Check(arRun1,  arScene,  "auto_repair/run1");
        //
        // f.Add(("C9", "PRESERVE block contains all building types and immutable elements verbatim",
        //     errs.Count == 0, errs.Count == 0 ? "All building types and immutable elements present" : Join(errs)));

        f.Add(("C9", "DISABLED — PRESERVE block contains all building types and immutable elements verbatim",
            null, "disabled while the short era PRESERVE is evaluated — restore together with the BuildPreserveBlock call in PromptService line 89"));
    }

    private static void DoC10(
        Dictionary<int, Prompt> gasRun1, Dictionary<int, Prompt> gasRun2,
        Dictionary<int, Prompt> dtRun1,  Dictionary<int, Prompt> dtRun2,
        Dictionary<int, Prompt> smRun1,  Dictionary<int, Prompt> smRun2,
        Dictionary<int, Prompt> arRun1,  Dictionary<int, Prompt> arRun2,
        List<(string, string, bool?, string)> f)
    {
        var errs = new List<string>();

        void Check(Dictionary<int, Prompt> run, string label)
        {
            foreach (var (year, prompt) in run)
            {
                // TEXT OVERLAY section is now removed — year is applied by a later overlay step
                if (prompt.Text.Contains("TEXT OVERLAY"))
                    errs.Add($"{label}/{year}: unexpected 'TEXT OVERLAY' section still present");

                // Year still anchors the VEHICLES block ("No vehicle newer than 1975");
                // an abandoned era has no vehicles and therefore no anchor line.
                if (prompt.SelectedVehicles.Count > 0 &&
                    !prompt.Text.Contains($"No vehicle newer than {year}"))
                    errs.Add($"{label}/{year}: 'No vehicle newer than {year}' not found");

                // The model-year restriction closes the gap a range like
                // "1975-1993 Volvo 240" leaves open — without it the generator
                // could render a late-range car while formally obeying the line.
                if (prompt.SelectedVehicles.Count > 0 &&
                    !prompt.Text.Contains($"render the {year} model year specifically"))
                    errs.Add($"{label}/{year}: missing the model-year rendering restriction for ranged vehicle listings");
            }
        }

        Check(gasRun1, "gas_station/run1");
        Check(gasRun2, "gas_station/run2");
        Check(dtRun1,  "downtown_street/run1");
        Check(dtRun2,  "downtown_street/run2");
        Check(smRun1,  "strip_mall/run1");
        Check(arRun1,  "auto_repair/run1");
        Check(smRun2,  "strip_mall/run2");
        Check(arRun2,  "auto_repair/run2");

        f.Add(("C10", "No TEXT OVERLAY section remains; year still anchors the VEHICLES block and carries the ranged-model-year restriction",
            errs.Count == 0, errs.Count == 0 ? "Overlay removed, vehicle year anchors correct, model-year restriction present" : Join(errs)));
    }

    private static void DoC11(
        Dictionary<int, Prompt> gasRun1, Dictionary<int, Prompt> gasRun2,
        Dictionary<int, Prompt> dtRun1,  Dictionary<int, Prompt> dtRun2,
        Dictionary<int, Prompt> smRun1,  Dictionary<int, Prompt> smRun2,
        Dictionary<int, Prompt> arRun1,  Dictionary<int, Prompt> arRun2,
        Prompt unknownPrompt,
        List<(string, string, bool?, string)> f)
    {
        var errs = new List<string>();
        var all  = new[]
        {
            (gasRun1, "gas/run1"), (gasRun2, "gas/run2"),
            (dtRun1, "downtown/run1"), (dtRun2, "downtown/run2"),
            (smRun1, "strip/run1"), (smRun2, "strip/run2"), (arRun1, "auto/run1"), (arRun2, "auto/run2")
        };

        // Words = whitespace tokens containing at least one letter or digit;
        // bullet dashes and em-dashes are punctuation, not words.
        int WordCount(string text) =>
            text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
                .Count(t => t.Any(char.IsLetterOrDigit));

        const int maxWords = 920; // raised from 820: the priority order block and the per-era SIGNAGE RESTRICTION whitelist added real length
        foreach (var (run, label) in all)
            foreach (var (year, prompt) in run)
            {
                int words = WordCount(prompt.Text);
                if (words >= maxWords)
                    errs.Add($"{label}/{year}: {words} words (limit {maxWords})");
            }
        int unknownWords = WordCount(unknownPrompt.Text);
        if (unknownWords >= maxWords)
            errs.Add($"unknown/1985: {unknownWords} words (limit {maxWords})");

        f.Add(("C11", $"Every prompt is under {maxWords} words",
            errs.Count == 0, errs.Count == 0 ? $"All prompts under {maxWords} words" : Join(errs)));
    }

    private static void DoC12(
        Dictionary<int, Prompt> gasRun1, Dictionary<int, Prompt> gasRun2,
        Dictionary<int, Prompt> dtRun1,  Dictionary<int, Prompt> dtRun2,
        Dictionary<int, Prompt> smRun1,  Dictionary<int, Prompt> smRun2,
        Dictionary<int, Prompt> arRun1,  Dictionary<int, Prompt> arRun2,
        Dictionary<int, EraProfile> eras,
        List<(string, string, bool?, string)> f)
    {
        var errs = new List<string>();
        var runs = new[]
        {
            (gasRun1, "gas/run1"), (gasRun2, "gas/run2"),
            (dtRun1, "downtown/run1"), (dtRun2, "downtown/run2"),
            (smRun1, "strip/run1"), (smRun2, "strip/run2"), (arRun1, "auto/run1"), (arRun2, "auto/run2")
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
        Dictionary<int, Prompt> smRun1,  Dictionary<int, Prompt> smRun2,
        Dictionary<int, Prompt> arRun1,  Dictionary<int, Prompt> arRun2,
        Dictionary<int, EraProfile> eras,
        List<(string, string, bool?, string)> f)
    {
        var errs = new List<string>();
        var runs = new[]
        {
            (gasRun1, "gas/run1"), (gasRun2, "gas/run2"),
            (dtRun1, "downtown/run1"), (dtRun2, "downtown/run2"),
            (smRun1, "strip/run1"), (smRun2, "strip/run2"), (arRun1, "auto/run1"), (arRun2, "auto/run2")
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
        List<(string, string, bool?, string)> f)
    {
        var errs = new List<string>();

        foreach (var (run, label) in new[] { (gasRun1, "gas/run1"), (gasRun2, "gas/run2") })
        {
            var text = run[2025].Text;
            // \bEVs?\b (case-sensitive) so lowercase words like "eye-level" don't false-match
            if (System.Text.RegularExpressions.Regex.IsMatch(text, @"\bEVs?\b"))
                errs.Add($"{label}/2025: contains 'EV'");
            foreach (var term in new[] { "electric", "charger", "Lightning", "e-scooter", "e-bike" })
                if (text.Contains(term, StringComparison.OrdinalIgnoreCase))
                    errs.Add($"{label}/2025: contains '{term}'");
        }

        f.Add(("C14", "Gas station 2025 prompt has no EV/electric/charger/Lightning content",
            errs.Count == 0, errs.Count == 0 ? "2025 gas prompts are fully de-electrified" : Join(errs)));
    }

    private static IEnumerable<(int Year, Prompt Prompt, string Label)> AllPrompts(
        Dictionary<int, Prompt> gasRun1, Dictionary<int, Prompt> gasRun2,
        Dictionary<int, Prompt> dtRun1,  Dictionary<int, Prompt> dtRun2,
        Dictionary<int, Prompt> smRun1,  Dictionary<int, Prompt> smRun2,
        Dictionary<int, Prompt> arRun1,  Dictionary<int, Prompt> arRun2,
        Prompt? unknownPrompt = null)
    {
        foreach (var (run, label) in new[]
        {
            (gasRun1, "gas/run1"), (gasRun2, "gas/run2"),
            (dtRun1, "downtown/run1"), (dtRun2, "downtown/run2"),
            (smRun1, "strip/run1"), (smRun2, "strip/run2"), (arRun1, "auto/run1"), (arRun2, "auto/run2")
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
        Dictionary<int, Prompt> smRun1,  Dictionary<int, Prompt> smRun2,
        Dictionary<int, Prompt> arRun1,  Dictionary<int, Prompt> arRun2,
        Prompt unknownPrompt,
        List<(string, string, bool?, string)> f)
    {
        var errs = new List<string>();
        const string populate = "populate it with the people and vehicles specified below";
        const string sidewalk = "never standing, sitting, or walking in the road or driving lanes";

        foreach (var (year, prompt, label) in AllPrompts(gasRun1, gasRun2, dtRun1, dtRun2, smRun1, smRun2, arRun1, arRun2, unknownPrompt))
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
        Dictionary<int, Prompt> smRun1,  Dictionary<int, Prompt> smRun2,
        Dictionary<int, Prompt> arRun1,  Dictionary<int, Prompt> arRun2,
        Prompt unknownPrompt,
        List<(string, string, bool?, string)> f)
    {
        var errs = new List<string>();
        const string overrideLine = "Tree sizes MUST follow this specification";

        foreach (var (year, prompt, label) in AllPrompts(gasRun1, gasRun2, dtRun1, dtRun2, smRun1, smRun2, arRun1, arRun2, unknownPrompt))
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
        List<(string, string, bool?, string)> f)
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

    // Greedy .* (not [^']+) so a sign's own text may contain an apostrophe
    // (e.g. "GENERAL TSO'S COMBO $4.25") without breaking the match — greedy
    // backtracking still finds the real ', ' separator between the two
    // quoted signs, since '.' never crosses the line's own newline.
    private static readonly System.Text.RegularExpressions.Regex WindowSignsLine =
        new(@"- window signs: '.*', '.*'");

    // Window signs, sampled extras, and people_mix present in every prompt.
    private static void DoC20(
        Dictionary<int, Prompt> gasRun1, Dictionary<int, Prompt> gasRun2,
        Dictionary<int, Prompt> dtRun1,  Dictionary<int, Prompt> dtRun2,
        Dictionary<int, Prompt> smRun1,  Dictionary<int, Prompt> smRun2,
        Dictionary<int, Prompt> arRun1,  Dictionary<int, Prompt> arRun2,
        Dictionary<int, EraProfile> eras,
        List<(string, string, bool?, string)> f)
    {
        var errs = new List<string>();

        var runs = new[]
        {
            (gasRun1, "gas_station", "gas/run1"), (gasRun2, "gas_station", "gas/run2"),
            (dtRun1, "downtown_street", "downtown/run1"), (dtRun2, "downtown_street", "downtown/run2"),
            (smRun1, "strip_mall", "strip/run1"), (smRun2, "strip_mall", "strip/run2"), (arRun1, "auto_repair", "auto/run1"), (arRun2, "auto_repair", "auto/run2")
        };

        foreach (var (run, sceneType, label) in runs)
            foreach (var (year, prompt) in run)
            {
                var sc = ContentFor(eras, year, sceneType);
                if (sc is null) continue;

                // A derelict era deliberately emits no window signs and no extras —
                // those are live-business props and contradict a closed-down block.
                var derelict = prompt.SceneCondition is "abandoned" or "squatted";
                if (!derelict && !WindowSignsLine.IsMatch(prompt.Text))
                    errs.Add($"{label}/{year}: no 'window signs:' line with two quoted signs");
                if (!derelict && ExtrasLinesIn(prompt.Text, sc).Count == 0)
                    errs.Add($"{label}/{year}: no sampled extras line present");
                if (derelict && WindowSignsLine.IsMatch(prompt.Text))
                    errs.Add($"{label}/{year}: {prompt.SceneCondition} but still has a 'window signs:' line");
                if (prompt.SceneCondition != "abandoned" &&
                    eras[year].PeopleMix is { Count: > 0 } mix && !mix.Any(m => prompt.Text.Contains($"- {m}")))
                    errs.Add($"{label}/{year}: no people_mix line present");
            }

        f.Add(("C20", "Every live prompt has a two-sign 'window signs:' line, >=1 extras line, and a people_mix line; derelict eras carry none of them",
            errs.Count == 0, errs.Count == 0 ? "All three sampling axes present in every prompt" : Join(errs)));
    }

    // PLACEMENT present in every prompt; within a run no pattern repeats unless the
    // relevant pool (sized by vehicle count) is exhausted.
    private static void DoC18(
        Dictionary<int, Prompt> gasRun1, Dictionary<int, Prompt> gasRun2,
        Dictionary<int, Prompt> dtRun1,  Dictionary<int, Prompt> dtRun2,
        Dictionary<int, Prompt> smRun1,  Dictionary<int, Prompt> smRun2,
        Dictionary<int, Prompt> arRun1,  Dictionary<int, Prompt> arRun2,
        Prompt unknownPrompt,
        List<(string, string, bool?, string)> f)
    {
        var errs = new List<string>();

        // Presence in every prompt with vehicles (an abandoned era has no vehicles
        // and no PLACEMENT line by design).
        foreach (var (year, prompt, label) in AllPrompts(gasRun1, gasRun2, dtRun1, dtRun2, smRun1, smRun2, arRun1, arRun2, unknownPrompt))
            if (prompt.SelectedVehicles.Count > 0 && PlacementLine(prompt) is null)
                errs.Add($"{label}/{year}: no PLACEMENT line");

        // Per-run pattern de-duplication. Patterns can be shared between the
        // count-based pools and the used-set is shared across them, so replay the
        // run's draws in year order: a repeat is only legal once every pattern in
        // that draw's own pool — same vehicle count AND same parking type — has
        // already been used.
        void CheckRun(Dictionary<int, Prompt> run, string label)
        {
            var used = new HashSet<string>();
            foreach (var year in Years)
            {
                if (!run.TryGetValue(year, out var prompt) || prompt.SelectedVehicles.Count == 0)
                    continue;
                var line = PlacementLine(prompt);
                if (line is null) continue; // already reported by the presence check

                var pattern = GenerationContext.AllPlacementPatternsFor(prompt.SelectedVehicles.Count)
                    .FirstOrDefault(line.Contains);
                if (pattern is null)
                {
                    errs.Add($"{label}/{year}: PLACEMENT line matches no pattern in its pool");
                    continue;
                }

                // Exhaustion is judged against the pool the draw actually came
                // from, not the union of both parking types. An era draws from
                // one type only, so street patterns left unused can never make a
                // legally-exhausted lot pool (or vice versa) look unexhausted.
                // Street and lot wordings are disjoint, so the matched pattern
                // identifies its own type.
                var onStreet = GenerationContext.PlacementPoolFor(prompt.SelectedVehicles.Count, true)
                    .Contains(pattern);
                var pool = GenerationContext.PlacementPoolFor(prompt.SelectedVehicles.Count, onStreet);

                if (used.Contains(pattern) && pool.Any(p => !used.Contains(p)))
                    errs.Add($"{label}/{year}: pattern repeated before its pool was exhausted");
                used.Add(pattern);
            }
        }

        CheckRun(gasRun1, "gas/run1");
        CheckRun(gasRun2, "gas/run2");
        CheckRun(dtRun1,  "downtown/run1");
        CheckRun(dtRun2,  "downtown/run2");
        CheckRun(smRun1,  "strip/run1");
        CheckRun(arRun1,  "auto/run1");
        CheckRun(smRun2,  "strip/run2");
        CheckRun(arRun2,  "auto/run2");

        f.Add(("C18", "Every prompt has a PLACEMENT line; no repeated pattern per run unless the pool is exhausted",
            errs.Count == 0, errs.Count == 0 ? "Placement present and de-duplicated per pool" : Join(errs)));
    }

    private static string? PlacementLine(Prompt prompt) =>
        prompt.Text.Split('\n').FirstOrDefault(l => l.StartsWith("PLACEMENT:"))?.Trim();

    // No descriptive-adjective-as-signage leaks; {DINER_NAME} resolved and stable per run.
    private static void DoC19(
        Dictionary<int, Prompt> gasRun1, Dictionary<int, Prompt> gasRun2,
        Dictionary<int, Prompt> dtRun1,  Dictionary<int, Prompt> dtRun2,
        Dictionary<int, Prompt> smRun1,  Dictionary<int, Prompt> smRun2,
        Dictionary<int, Prompt> arRun1,  Dictionary<int, Prompt> arRun2,
        List<(string, string, bool?, string)> f)
    {
        var errs = new List<string>();
        // 'aging'/'corporate' within two words before a business type reads as a sign.
        var adjacency = new System.Text.RegularExpressions.Regex(
            @"\b(aging|corporate)\b(?:\s+\S+){0,2}\s+(diner|bank|store|shop|market|pharmacy|cafe|salon|grocery|hardware|bakery|deli)\b",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        foreach (var (year, prompt, label) in AllPrompts(gasRun1, gasRun2, dtRun1, dtRun2, smRun1, smRun2, arRun1, arRun2))
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
        CheckName(smRun1,  "strip/run1");
        CheckName(arRun1,  "auto/run1");
        CheckName(smRun2,  "strip/run2");
        CheckName(arRun2,  "auto/run2");

        f.Add(("C19", "No descriptive-as-signage leaks; {DINER_NAME} resolved and identical across a run",
            errs.Count == 0, errs.Count == 0 ? "Business names clean and diner name stable" : Join(errs)));
    }

    // Run-to-run sampling variance in extras / window signs.
    private static void DoC21(
        Dictionary<int, Prompt> gasRun1, Dictionary<int, Prompt> gasRun2,
        Dictionary<int, Prompt> dtRun1,  Dictionary<int, Prompt> dtRun2,
        Dictionary<int, Prompt> smRun1,  Dictionary<int, Prompt> smRun2,
        Dictionary<int, Prompt> arRun1,  Dictionary<int, Prompt> arRun2,
        Dictionary<int, EraProfile> eras,
        List<(string, string, bool?, string)> f)
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
        Check(smRun1,  smRun2,  "strip_mall",      "strip_mall");
        Check(arRun1,  arRun2,  "auto_repair",      "auto_repair");

        f.Add(("C21", "Run1 vs Run2: >=3 of 6 years differ in sampled extras or window signs",
            errs.Count == 0, errs.Count == 0 ? "Sufficient sampling variance between seeds" : Join(errs)));
    }

    // Prompt length budget in characters, over every prompt including the unknown
    // fallback. Lengths are always logged so the longest blocks stay visible.
    private static void DoC22(
        Dictionary<int, Prompt> gasRun1, Dictionary<int, Prompt> gasRun2,
        Dictionary<int, Prompt> dtRun1,  Dictionary<int, Prompt> dtRun2,
        Dictionary<int, Prompt> smRun1,  Dictionary<int, Prompt> smRun2,
        Dictionary<int, Prompt> arRun1,  Dictionary<int, Prompt> arRun2,
        Prompt unknownPrompt,
        ILogger logger,
        List<(string, string, bool?, string)> f)
    {
        var errs    = new List<string>();
        var lengths = new List<string>();

        foreach (var (year, prompt, label) in AllPrompts(gasRun1, gasRun2, dtRun1, dtRun2, smRun1, smRun2, arRun1, arRun2, unknownPrompt))
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

    // Condition rank: thriving/busy/new/restored=0, declining=1, abandoned/squatted=2.
    private static readonly Dictionary<string, int> ConditionRank = new()
    {
        ["thriving"] = 0, ["busy"] = 0, ["new"] = 0, ["restored"] = 0,
        ["declining"] = 1,
        ["abandoned"] = 2, ["squatted"] = 2
    };

    // Conditions now apply to gas_station, downtown_street AND strip_mall.
    // Verifies: (1) default/unknown scenes (exercised by unknownPrompt, the only
    // scene type left outside supportsCondition) always stay "thriving";
    // (2) rank is monotonic across one run's eras, with the single allowed
    // exception of a gas station's final era dropping back to "new" or
    // "restored" — downtown_street and strip_mall both follow the plain
    // monotonic path with no finale exception; (3) abandoned/declining/squatted
    // carry the counts they imply, for all three scene types that support
    // conditions; (4) "squatted" and "restored" are gas-station-only finale
    // resolutions — "squatted" is never legal for downtown_street or
    // strip_mall, and "restored" may appear only on a gas station's final era.
    private static void DoC23(
        Dictionary<int, Prompt> gasRun1, Dictionary<int, Prompt> gasRun2,
        Dictionary<int, Prompt> dtRun1,  Dictionary<int, Prompt> dtRun2,
        Dictionary<int, Prompt> smRun1,  Dictionary<int, Prompt> smRun2,
        Dictionary<int, Prompt> arRun1,  Dictionary<int, Prompt> arRun2,
        Prompt unknownPrompt,
        List<(string, string, bool?, string)> f)
    {
        var errs = new List<string>();
        const string noVehicles = "NO vehicles anywhere";
        const string noPeople   = "NO people anywhere";

        if (unknownPrompt.SceneCondition != "thriving")
            errs.Add($"unknown(default)/1985: SceneCondition '{unknownPrompt.SceneCondition}' (expected 'thriving')");

        var peopleLine = new System.Text.RegularExpressions.Regex(@"EXACTLY (\d+) people");
        void CheckCounts(Dictionary<int, Prompt> run, string label)
        {
            foreach (var (year, prompt) in run)
            {
                if (prompt.SceneCondition == "abandoned")
                {
                    if (!prompt.Text.Contains(noVehicles))
                        errs.Add($"{label}/{year}: abandoned but missing '{noVehicles}'");
                    if (!prompt.Text.Contains(noPeople))
                        errs.Add($"{label}/{year}: abandoned but missing '{noPeople}'");
                }
                else if (prompt.SceneCondition is "declining" or "squatted")
                {
                    var m = peopleLine.Match(prompt.Text);
                    if (!m.Success || int.Parse(m.Groups[1].Value) is < 2 or > 4)
                        errs.Add($"{label}/{year}: {prompt.SceneCondition} but people count is {(m.Success ? m.Groups[1].Value : "missing")} (expected 2-4)");
                    var (vMin, vMax) = prompt.SceneCondition == "squatted" ? (0, 1) : (1, 2);
                    if (prompt.SelectedVehicles.Count < vMin || prompt.SelectedVehicles.Count > vMax)
                        errs.Add($"{label}/{year}: {prompt.SceneCondition} but vehicle count is {prompt.SelectedVehicles.Count} (expected {vMin}-{vMax})");
                }
            }
        }

        CheckCounts(gasRun1, "gas/run1");
        CheckCounts(gasRun2, "gas/run2");
        CheckCounts(dtRun1,  "downtown/run1");
        CheckCounts(dtRun2,  "downtown/run2");
        CheckCounts(smRun1,  "strip/run1");
        CheckCounts(arRun1,  "auto/run1");
        CheckCounts(smRun2,  "strip/run2");
        CheckCounts(arRun2,  "auto/run2");

        void CheckMonotonic(Dictionary<int, Prompt> run, string label, bool isGasStation)
        {
            var prevRank = -1;
            foreach (var year in Years)
            {
                if (!run.TryGetValue(year, out var prompt)) continue;
                var rank = ConditionRank.GetValueOrDefault(prompt.SceneCondition, 0);
                var isFinale = isGasStation && year == Years[^1];
                if (prevRank >= 0 && rank < prevRank && !isFinale)
                    errs.Add($"{label}/{year}: condition rank dropped from {prevRank} to {rank} ('{prompt.SceneCondition}') outside the gas-station finale exception");
                prevRank = rank;
            }
        }

        CheckMonotonic(gasRun1, "gas/run1", isGasStation: true);
        CheckMonotonic(gasRun2, "gas/run2", isGasStation: true);
        CheckMonotonic(dtRun1,  "downtown/run1", isGasStation: false);
        CheckMonotonic(dtRun2,  "downtown/run2", isGasStation: false);
        CheckMonotonic(smRun1,  "strip/run1", isGasStation: false);
        CheckMonotonic(arRun1,  "auto/run1", isGasStation: false);
        CheckMonotonic(smRun2,  "strip/run2", isGasStation: false);
        CheckMonotonic(arRun2,  "auto/run2", isGasStation: false);

        foreach (var (run, label) in new[]
        {
            (dtRun1, "downtown/run1"), (dtRun2, "downtown/run2"),
            (smRun1, "strip/run1"),    (smRun2, "strip/run2"), (arRun1, "auto/run1"), (arRun2, "auto/run2")
        })
            foreach (var (year, prompt) in run)
                if (prompt.SceneCondition == "squatted")
                    errs.Add($"{label}/{year}: resolved to 'squatted' (gas-station-only)");

        foreach (var (run, label) in new[] { (gasRun1, "gas/run1"), (gasRun2, "gas/run2") })
            foreach (var (year, prompt) in run)
                if (prompt.SceneCondition == "squatted" && year != Years[^1])
                    errs.Add($"{label}/{year}: 'squatted' outside the final era");

        foreach (var (run, label) in new[] { (gasRun1, "gas/run1"), (gasRun2, "gas/run2") })
            foreach (var (year, prompt) in run)
                if (prompt.SceneCondition == "restored" && year != Years[^1])
                    errs.Add($"{label}/{year}: 'restored' outside the final era");

        f.Add(("C23", "default/unknown scenes always thriving; rank monotonic per run (gas-station finale may resolve to 'new' or 'restored'); abandoned/declining/squatted counts honored for gas_station, downtown_street and strip_mall; 'squatted' only on a gas_station's final era; 'restored' only on a gas_station's final era",
            errs.Count == 0, errs.Count == 0 ? "Condition trajectory invariants hold" : Join(errs)));
    }

    // Every business-name token resolves to a value drawn from its own pool (never
    // a foreign pool's name or a leftover literal), and stays identical across
    // every era built on the same context — mirroring how PromptService calls
    // context.BusinessNameTokens() once per era on the ONE GenerationContext shared
    // across all six eras of a run.
    //
    // Only {DINER_NAME} is guaranteed to land in every sampled prompt today (its
    // storefront line is the only one price-anchored, so BuildSceneBlock's 3-of-8
    // storefront sampler always includes it); the other seven tokens currently
    // live only in 1975 downtown_street and compete for the two remaining sampled
    // slots, so they will not appear in every run. This check therefore verifies
    // the resolution MECHANISM directly (deterministic, independent of storefront
    // sampling luck) and separately verifies no cross-era mismatch or cross-pool
    // leak in whatever the sampler actually included.
    private static void DoC24(
        Dictionary<int, Prompt> gasRun1, Dictionary<int, Prompt> gasRun2,
        Dictionary<int, Prompt> dtRun1,  Dictionary<int, Prompt> dtRun2,
        Dictionary<int, Prompt> smRun1,  Dictionary<int, Prompt> smRun2,
        Dictionary<int, Prompt> arRun1,  Dictionary<int, Prompt> arRun2,
        List<(string, string, bool?, string)> f)
    {
        var errs = new List<string>();

        foreach (var seed in new[] { 7, 4242 })
        {
            var ctx    = new GenerationContext { Random = new Random(seed), TotalEras = 1 };
            var first  = ctx.BusinessNameTokens();
            var second = ctx.BusinessNameTokens(); // simulates the next era's call on the same run context

            foreach (var (token, pool) in BusinessNamePools)
            {
                if (!pool.Contains(first[token]))
                    errs.Add($"seed {seed}: {token} resolved to '{first[token]}', not a member of its own pool");
                if (first[token] != second[token])
                    errs.Add($"seed {seed}: {token} changed between calls on the same context ('{first[token]}' vs '{second[token]}') — must stay stable across eras");
            }
        }

        void CheckRun(Dictionary<int, Prompt> run, string label)
        {
            foreach (var (token, pool) in BusinessNamePools)
            {
                var found = run.Values
                    .SelectMany(p => pool.Where(n => p.Text.Contains(n)))
                    .Distinct()
                    .ToList();
                if (found.Count > 1)
                    errs.Add($"{label}: {token} resolved to different names across eras: {string.Join(", ", found)}");
            }
        }

        CheckRun(gasRun1, "gas/run1");
        CheckRun(gasRun2, "gas/run2");
        CheckRun(dtRun1,  "downtown/run1");
        CheckRun(dtRun2,  "downtown/run2");
        CheckRun(smRun1,  "strip/run1");
        CheckRun(arRun1,  "auto/run1");
        CheckRun(smRun2,  "strip/run2");
        CheckRun(arRun2,  "auto/run2");

        f.Add(("C24", "Every business-name token resolves to a member of its own pool and stays identical across all six eras of a run",
            errs.Count == 0, errs.Count == 0 ? "All 8 business tokens resolve correctly and remain stable per run" : Join(errs)));
    }

    // DECAY section presence/absence, placement, wording, and pool provenance.
    // Maps a run label to the scene type whose decay pool it must be checked
    // against. Explicit and exhaustive — an unrecognised label is a bug in this
    // test file (a new fixture added without updating this map), so it fails
    // loudly instead of silently defaulting to the wrong scene type's pool.
    private static string SceneTypeForLabel(string label) => label switch
    {
        _ when label.StartsWith("gas",      StringComparison.Ordinal) => "gas_station",
        _ when label.StartsWith("downtown", StringComparison.Ordinal) => "downtown_street",
        _ when label.StartsWith("strip",    StringComparison.Ordinal) => "strip_mall",
        _ when label.StartsWith("auto",     StringComparison.Ordinal) => "auto_repair",
        _ => throw new InvalidOperationException(
            $"DoC25: run label '{label}' has no known scene-type mapping — add it to SceneTypeForLabel")
    };

    private static void DoC25(
        Dictionary<int, Prompt> gasRun1, Dictionary<int, Prompt> gasRun2,
        Dictionary<int, Prompt> dtRun1,  Dictionary<int, Prompt> dtRun2,
        Dictionary<int, Prompt> smRun1,  Dictionary<int, Prompt> smRun2,
        Dictionary<int, Prompt> arRun1,  Dictionary<int, Prompt> arRun2,
        Dictionary<int, EraProfile> eras,
        List<(string, string, bool?, string)> f)
    {
        var errs = new List<string>();
        var runs = new[]
        {
            (gasRun1, "gas/run1"), (gasRun2, "gas/run2"),
            (dtRun1, "downtown/run1"), (dtRun2, "downtown/run2"),
            (smRun1, "strip/run1"), (smRun2, "strip/run2"), (arRun1, "auto/run1"), (arRun2, "auto/run2")
        };
        string[] forbiddenGeometryWords = { "road width", "curb position", "building footprint", "camera" };

        foreach (var (run, label) in runs)
            foreach (var (year, prompt) in run)
            {
                var decayExpected = prompt.SceneCondition is "declining" or "abandoned" or "squatted";
                var hasDecay      = prompt.Text.Contains("DECAY");

                if (decayExpected && !hasDecay)
                    errs.Add($"{label}/{year}: {prompt.SceneCondition} but no DECAY section");
                if (!decayExpected && hasDecay)
                    errs.Add($"{label}/{year}: {prompt.SceneCondition} but has a DECAY section");

                if (!decayExpected)
                {
                    var expectedMarkings = $"- road markings: {string.Join(", ", eras[year].Infrastructure.Roads.Markings.Take(3))}";
                    if (!prompt.Text.Contains(expectedMarkings))
                        errs.Add($"{label}/{year}: era road markings not verbatim for condition '{prompt.SceneCondition}'");
                }

                if (!hasDecay) continue;

                var outputFormatIdx = prompt.Text.IndexOf("OUTPUT FORMAT", StringComparison.Ordinal);
                var decayIdx        = prompt.Text.IndexOf("DECAY", StringComparison.Ordinal);
                if (outputFormatIdx >= 0 && decayIdx < outputFormatIdx)
                    errs.Add($"{label}/{year}: DECAY section appears before OUTPUT FORMAT (inside PRESERVE)");

                var treesIdx  = prompt.Text.IndexOf("TREES", decayIdx, StringComparison.Ordinal);
                var decayBody = treesIdx > decayIdx ? prompt.Text[decayIdx..treesIdx] : prompt.Text[decayIdx..];

                // Only the sampled wear entries are checked for forbidden geometry
                // terms — the block's own trailing constraint sentence legitimately
                // names "road width", "curb positions", etc. specifically to say
                // they DON'T change, so it must not trip this check.
                var poolSceneType = SceneTypeForLabel(label);
                var pool = PromptService.DecayPoolFor(poolSceneType, prompt.SceneCondition) ?? Array.Empty<string>();
                var bullets = decayBody.Split('\n')
                    .Select(l => l.Trim())
                    .Where(l => l.StartsWith("- "))
                    .Select(l => l[2..].Trim())
                    .ToList();
                if (bullets.Count == 0)
                    errs.Add($"{label}/{year}: DECAY section has no bullet lines");
                foreach (var b in bullets)
                {
                    if (!pool.Contains(b))
                        errs.Add($"{label}/{year}: DECAY bullet '{b}' not drawn from the expected pool for '{prompt.SceneCondition}'");
                    foreach (var word in forbiddenGeometryWords)
                        if (b.Contains(word, StringComparison.OrdinalIgnoreCase))
                            errs.Add($"{label}/{year}: DECAY bullet '{b}' mentions forbidden geometry term '{word}'");
                }
            }

        f.Add(("C25", "DECAY present iff condition is declining/abandoned/squatted; healthy conditions keep verbatim era road markings with no DECAY; DECAY never precedes OUTPUT FORMAT (i.e. never inside PRESERVE) and never mentions geometry terms; bullets are drawn from the correct severity pool",
            errs.Count == 0, errs.Count == 0 ? "Decay section invariants hold" : Join(errs)));
    }

    // Caption voice coverage: the caption system (CaptionService + its prompt
    // files) must stay in sync with the prompt-generation condition/scene-type
    // system as both evolve independently. A scene type or condition added on
    // one side without a matching entry on the caption side would otherwise
    // silently degrade to a generic caption at runtime.
    private static async Task DoC26(
        IDataService dataService,
        Dictionary<int, EraProfile> eras,
        List<(string, string, bool?, string)> f)
    {
        var errs = new List<string>();

        // 1. Every caption body file must load and parse. Captions are assembled
        // locally now, so this covers data/captions/ — the files GenerateAsync
        // actually reads. (data/prompts/caption-*.txt are the retired LLM system
        // prompts, kept in the repo but no longer read at runtime, so asserting
        // on them would only guard dead files.)
        const int MinBodies = 5;
        var allowedPlaceholders = new HashSet<string>(StringComparer.Ordinal)
            { "firstYear", "lastYear", "angle", "condition" };
        var placeholder = new System.Text.RegularExpressions.Regex(@"\{(\w+)\}");

        foreach (var name in CaptionService.AnglesByScene.Keys.Append("base"))
        {
            string rawBodies;
            try
            {
                rawBodies = await dataService.LoadCaptionBodiesAsync(name);
            }
            catch (Exception ex)
            {
                errs.Add($"captions/{name}.txt failed to load: {ex.Message}");
                continue;
            }

            var bodies = CaptionService.SplitBodies(rawBodies);
            if (bodies.Count < MinBodies)
                errs.Add($"captions/{name}.txt: {bodies.Count} bodies (need >= {MinBodies})");

            for (var i = 0; i < bodies.Count; i++)
            {
                var body  = bodies[i];
                var label = $"captions/{name}.txt body {i + 1}";

                // An unknown placeholder never gets substituted and ships as
                // literal braces in the posted caption.
                foreach (System.Text.RegularExpressions.Match m in placeholder.Matches(body))
                    if (!allowedPlaceholders.Contains(m.Groups[1].Value))
                        errs.Add($"{label}: unknown placeholder {m.Value}");

                // Hashtags are appended from hashtags.txt — a body carrying its
                // own would double up in the posted caption.
                if (body.Contains('#'))
                    errs.Add($"{label}: contains a hashtag; hashtags are appended separately");

                // The format ends on a question that invites comments.
                if (!body.TrimEnd().EndsWith('?'))
                    errs.Add($"{label}: does not end on a question");
            }
        }

        // 2. Every scene type with a scene_content block in the era JSONs must
        // have its own caption voice.
        var knownSceneTypes = eras.Values
            .SelectMany(e => e.SceneContent?.Keys ?? Enumerable.Empty<string>())
            .Where(k => k != "default")
            .Distinct()
            .ToList();
        foreach (var sceneType in knownSceneTypes)
            if (!CaptionService.AnglesByScene.ContainsKey(sceneType))
                errs.Add($"scene type '{sceneType}' has scene_content but no CaptionService.AnglesByScene entry");

        // 3. Pool hygiene: minimum size, no internal duplicates, no scene-specific
        // anchor duplicated in CommonAngles.
        void CheckPool(string label, IReadOnlyList<string> pool)
        {
            if (pool.Count < 4)
                errs.Add($"{label}: only {pool.Count} anchors (need >= 4)");
            var dupes = pool.GroupBy(a => a, StringComparer.OrdinalIgnoreCase)
                            .Where(g => g.Count() > 1).Select(g => g.Key).ToList();
            if (dupes.Count > 0)
                errs.Add($"{label}: duplicate anchor(s): {string.Join(", ", dupes)}");
        }

        CheckPool("CommonAngles", CaptionService.CommonAngles);
        foreach (var (sceneType, pool) in CaptionService.AnglesByScene)
        {
            CheckPool(sceneType, pool);
            var leaks = pool.Where(a => CaptionService.CommonAngles.Contains(a, StringComparer.OrdinalIgnoreCase)).ToList();
            if (leaks.Count > 0)
                errs.Add($"{sceneType}: anchor(s) duplicated in CommonAngles: {string.Join(", ", leaks)}");
        }

        // 4. AnglesFor() composition contract.
        var gasAngles         = CaptionService.AnglesFor("gas_station");
        var expectedGasAngles = CaptionService.AnglesByScene["gas_station"].Concat(CaptionService.CommonAngles).ToList();
        if (!gasAngles.SequenceEqual(expectedGasAngles))
            errs.Add("AnglesFor(\"gas_station\") != AnglesByScene[\"gas_station\"] followed by CommonAngles");

        var unknownAngles = CaptionService.AnglesFor("unknown");
        if (!unknownAngles.SequenceEqual(CaptionService.CommonAngles))
            errs.Add("AnglesFor(\"unknown\") != CommonAngles exactly");

        // 5. Cross-voice leak: forecourt vocabulary must not bleed into a main
        // street or strip mall caption.
        string[] gasWords = { "gas", "pump", "oil", "attendant" };
        foreach (var sceneType in new[] { "downtown_street", "strip_mall" })
            foreach (var anchor in CaptionService.AnglesByScene[sceneType])
                foreach (var word in gasWords)
                    if (anchor.Contains(word, StringComparison.OrdinalIgnoreCase))
                        errs.Add($"{sceneType} anchor '{anchor}' contains gas-station word '{word}'");

        // auto_repair legitimately uses "oil" (motor oil, oil stains) — a narrower
        // check without it guards against the exact confusion the scene type
        // exists to prevent: pumps, fuel, or an attendant slipping into its voice.
        string[] autoRepairGasWords = { "gas", "pump", "attendant" };
        foreach (var anchor in CaptionService.AnglesByScene["auto_repair"])
            foreach (var word in autoRepairGasWords)
                if (anchor.Contains(word, StringComparison.OrdinalIgnoreCase))
                    errs.Add($"auto_repair anchor '{anchor}' contains gas-station word '{word}'");

        // 6. Condition coverage: every condition reachable at runtime (every
        // era's allowed_scene_conditions, plus the gas-station-finale-only
        // "squatted" and "restored") must map to a real phrase, not the
        // unknown-condition fallback text.
        var reachableConditions = eras.Values
            .SelectMany(e => e.AllowedSceneConditions ?? Array.Empty<string>())
            .Append("squatted")
            .Append("restored")
            .Distinct()
            .ToList();
        foreach (var condition in reachableConditions)
            if (CaptionService.MapFinalCondition(condition) == CaptionService.UnknownConditionText)
                errs.Add($"condition '{condition}' is reachable at runtime but MapFinalCondition falls back to the unknown-condition text");

        f.Add(("C26", "Caption body files load and parse (>=5 bodies, known placeholders only, no hashtags, ends on a question); every scene_content type has a caption voice; anchor pools are well-formed and non-leaking; AnglesFor() composition holds; every reachable condition maps to a real phrase",
            errs.Count == 0, errs.Count == 0 ? "Caption body files and voice coverage hold" : Join(errs)));
    }

    // base-clean.txt (the clean-plate pass) and every era prompt must agree on
    // the 9:16 portrait canvas — a drift here means the clean base and the
    // per-year edits fight over aspect ratio.
    private static async Task DoC27(
        IDataService dataService,
        Dictionary<int, Prompt> gasRun1, Dictionary<int, Prompt> gasRun2,
        Dictionary<int, Prompt> dtRun1,  Dictionary<int, Prompt> dtRun2,
        Dictionary<int, Prompt> smRun1,  Dictionary<int, Prompt> smRun2,
        Dictionary<int, Prompt> arRun1,  Dictionary<int, Prompt> arRun2,
        List<(string, string, bool?, string)> f)
    {
        var errs = new List<string>();
        const string portraitPhrase = "TRUE 9:16 vertical portrait";

        // 1. base-clean.txt must load.
        string? baseClean = null;
        try
        {
            baseClean = await dataService.LoadPromptAsync("base-clean");
        }
        catch (Exception ex)
        {
            errs.Add($"base-clean.txt failed to load: {ex.Message}");
        }

        if (baseClean is not null)
        {
            // 2. base-clean.txt declares the exact portrait phrase.
            if (!baseClean.Contains(portraitPhrase))
                errs.Add($"base-clean.txt is missing the exact phrase '{portraitPhrase}'");

            // 4. base-clean.txt must not hedge toward a different aspect ratio.
            foreach (var forbidden in new[] { "16:9", "1:1", "4:5", "landscape", "square" })
                if (baseClean.Contains(forbidden, StringComparison.OrdinalIgnoreCase))
                    errs.Add($"base-clean.txt contains forbidden aspect-ratio term '{forbidden}'");

            // 5. base-clean.txt still keeps its cleanup contract.
            if (!baseClean.Contains("people", StringComparison.OrdinalIgnoreCase))
                errs.Add("base-clean.txt no longer mentions removing people");
            if (!baseClean.Contains("vehicle", StringComparison.OrdinalIgnoreCase))
                errs.Add("base-clean.txt no longer mentions removing vehicles");
            if (!baseClean.Contains("pixel-identical"))
                errs.Add("base-clean.txt no longer contains 'pixel-identical'");
            if (!baseClean.Contains("canvas extension"))
                errs.Add("base-clean.txt no longer contains 'canvas extension'");
        }

        // 3. The same phrase must appear in every generated prompt of every run —
        // base-clean and the era prompts must not drift apart on aspect ratio.
        var runs = new[]
        {
            (gasRun1, "gas/run1"), (gasRun2, "gas/run2"),
            (dtRun1, "downtown/run1"), (dtRun2, "downtown/run2"),
            (smRun1, "strip/run1"), (smRun2, "strip/run2"), (arRun1, "auto/run1"), (arRun2, "auto/run2")
        };
        foreach (var (run, label) in runs)
            foreach (var (year, prompt) in run)
                if (!prompt.Text.Contains(portraitPhrase))
                    errs.Add($"{label}/{year}: prompt is missing '{portraitPhrase}'");

        f.Add(("C27", "base-clean.txt loads, declares the exact 9:16 portrait phrase (and no competing aspect-ratio term), keeps its people/vehicle-removal + pixel-identical/canvas-extension cleanup contract, and every generated prompt carries the same portrait phrase",
            errs.Count == 0, errs.Count == 0 ? "base-clean/prompt aspect-ratio contract holds" : Join(errs)));
    }

    // People bullet lines (content.PeopleActivities picks and the era.PeopleMix
    // line) now share cross-era memory via context.TryUsePeopleLine — the same
    // shape UsedCarModels already gives vehicles. Verifies, per run: a line is
    // never repeated unless every other entry in its own era's pool has already
    // been used elsewhere in that run — mirrors C18's placement-pattern
    // exhaustion rule. A flat "never repeats" would be too strong given how
    // small some real pools still are (era.PeopleMix currently has as few as
    // 6-7 entries per era) before the planned pool-expansion pass.
    private static void DoC28(
        Dictionary<int, Prompt> gasRun1, Dictionary<int, Prompt> gasRun2,
        Dictionary<int, Prompt> dtRun1,  Dictionary<int, Prompt> dtRun2,
        Dictionary<int, Prompt> smRun1,  Dictionary<int, Prompt> smRun2,
        Dictionary<int, Prompt> arRun1,  Dictionary<int, Prompt> arRun2,
        Dictionary<int, EraProfile> eras,
        List<(string, string, bool?, string)> f)
    {
        var errs = new List<string>();
        var runs = new[]
        {
            (gasRun1, "gas_station", "gas/run1"), (gasRun2, "gas_station", "gas/run2"),
            (dtRun1, "downtown_street", "downtown/run1"), (dtRun2, "downtown_street", "downtown/run2"),
            (smRun1, "strip_mall", "strip/run1"), (smRun2, "strip_mall", "strip/run2"), (arRun1, "auto_repair", "auto/run1"), (arRun2, "auto_repair", "auto/run2")
        };

        foreach (var (run, sceneType, label) in runs)
        {
            // Mirrors context.UsedPeopleLines: one combined used-set for the
            // whole run, shared between people_activities and people_mix picks,
            // exactly as PromptService shares one GenerationContext per run.
            var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            void CheckLine(int year, string line, IReadOnlyList<string> pool)
            {
                if (used.Contains(line) && pool.Any(p => !used.Contains(p)))
                    errs.Add($"{label}/{year}: people line '{line}' repeated before its era pool was exhausted");
                used.Add(line);
            }

            foreach (var year in Years)
            {
                if (!run.TryGetValue(year, out var prompt)) continue;

                var sc = ContentFor(eras, year, sceneType);
                if (sc is not null)
                    foreach (var activity in sc.PeopleActivities.Where(a => prompt.Text.Contains($"- {a}")))
                        CheckLine(year, activity, sc.PeopleActivities);

                if (eras[year].PeopleMix is { Count: > 0 } mix)
                {
                    var mixLine = mix.FirstOrDefault(m => prompt.Text.Contains($"- {m}"));
                    if (mixLine is not null)
                        CheckLine(year, mixLine, mix);
                }
            }
        }

        f.Add(("C28", "People bullet lines (people_activities picks and the people_mix line) never repeat within a run unless their era's own pool is already exhausted",
            errs.Count == 0, errs.Count == 0 ? "No premature people-line repeats" : Join(errs)));
    }

    private static void DoC29(List<(string, string, bool?, string)> f)
    {
        var errs = new List<string>();
        const int totalEras = 6;

        var method = typeof(GenerationContext).GetMethod("DeclineBias",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
            ?? throw new InvalidOperationException("GenerationContext.DeclineBias not found");

        var ctx = new GenerationContext { Random = new Random(42), TotalEras = totalEras };

        double? previous = null;
        for (int i = 0; i < totalEras; i++)
        {
            ctx.BeginEra();
            var bias = (double)method.Invoke(ctx, null)!;

            if (bias < 0.0 || bias > 1.0)
                errs.Add($"eraIndex={i}: DeclineBias() {bias} is outside 0..1");

            if (previous is not null && bias < previous.Value)
                errs.Add($"eraIndex={i}: DeclineBias() {bias} is lower than previous era's {previous.Value}");

            previous = bias;
        }

        f.Add(("C29", "DeclineBias() ramps non-decreasing across the run and stays within 0..1",
            errs.Count == 0, errs.Count == 0 ? "Bias ramp OK across all eras" : Join(errs)));
    }

    // Named markers are unambiguous: Ghost text never names either chain, and
    // RadioShack's pre-1990 Generic text explicitly carries no chain name — so a
    // literal "Blockbuster"/"RadioShack" substring can only come from a Named line.
    private static void DoC30(
        Dictionary<int, Prompt> gasRun1, Dictionary<int, Prompt> gasRun2,
        Dictionary<int, Prompt> dtRun1,  Dictionary<int, Prompt> dtRun2,
        Dictionary<int, Prompt> smRun1,  Dictionary<int, Prompt> smRun2,
        Dictionary<int, Prompt> arRun1,  Dictionary<int, Prompt> arRun2,
        Prompt unknownPrompt,
        List<(string, string, bool?, string)> f)
    {
        var errs = new List<string>();

        foreach (var (year, prompt, label) in AllPrompts(gasRun1, gasRun2, dtRun1, dtRun2, smRun1, smRun2, arRun1, arRun2, unknownPrompt))
        {
            if (year >= 1990) continue;
            if (prompt.Text.Contains("Blockbuster"))
                errs.Add($"{label}/{year}: Blockbuster appears Named before 1990");
            if (prompt.Text.Contains("RadioShack"))
                errs.Add($"{label}/{year}: RadioShack appears Named before 1990");
        }

        f.Add(("C30", "Neither chain ever appears Named in a prompt for a year before 1990",
            errs.Count == 0, errs.Count == 0 ? "No pre-1990 named chain tenants" : Join(errs)));
    }

    private static void DoC31(
        Dictionary<int, Prompt> dtRun1, Dictionary<int, Prompt> dtRun2,
        List<(string, string, bool?, string)> f)
    {
        var errs = new List<string>();
        string[] markers = { "Blockbuster", "torn-ticket", "blue fascia" };

        foreach (var (run, label) in new[] { (dtRun1, "downtown/run1"), (dtRun2, "downtown/run2") })
            foreach (var (year, prompt) in run)
                foreach (var marker in markers)
                    if (prompt.Text.Contains(marker))
                        errs.Add($"{label}/{year}: found Blockbuster marker '{marker}' in a downtown_street prompt");

        f.Add(("C31", "Blockbuster never appears in a downtown_street prompt, in any form",
            errs.Count == 0, errs.Count == 0 ? "No Blockbuster content in any downtown_street prompt" : Join(errs)));
    }

    private static void DoC32(
        Dictionary<int, Prompt> gasRun1, Dictionary<int, Prompt> gasRun2,
        Dictionary<int, Prompt> dtRun1,  Dictionary<int, Prompt> dtRun2,
        Dictionary<int, Prompt> smRun1,  Dictionary<int, Prompt> smRun2,
        Dictionary<int, Prompt> arRun1,  Dictionary<int, Prompt> arRun2,
        Prompt unknownPrompt,
        List<(string, string, bool?, string)> f)
    {
        var errs = new List<string>();

        foreach (var (year, prompt, label) in AllPrompts(gasRun1, gasRun2, dtRun1, dtRun2, smRun1, smRun2, arRun1, arRun2, unknownPrompt))
        {
            if (prompt.SceneCondition is not ("abandoned" or "squatted")) continue;
            if (prompt.Text.Contains("Blockbuster"))
                errs.Add($"{label}/{year}: Blockbuster appears Named in a {prompt.SceneCondition} era");
            if (prompt.Text.Contains("RadioShack"))
                errs.Add($"{label}/{year}: RadioShack appears Named in a {prompt.SceneCondition} era");
        }

        f.Add(("C32", "Neither chain ever appears Named in an abandoned or squatted era",
            errs.Count == 0, errs.Count == 0 ? "No named chain tenants in derelict eras" : Join(errs)));
    }

    // Deterministic structural check, not a sampled-outcome assertion: for any
    // fixed seed, whichever way the once-per-run presence coin landed, that
    // presence must hold consistently across every era of the run — a chain
    // present in the run appears in exactly the eras its year/condition schedule
    // allows, and a chain absent from the run never appears at all. Condition is
    // held at "thriving" throughout so only the presence/year logic is exercised,
    // not the abandoned/squatted overrides already covered by C32.
    private static void DoC33(List<(string, string, bool?, string)> f)
    {
        var errs = new List<string>();
        var years = new[] { 1975, 1985, 1995, 2005, 2015, 2025 };

        for (int seed = 1; seed <= 20; seed++)
        {
            var ctx = new GenerationContext { Random = new Random(seed) };
            bool bbPresentInRun = ctx.BlockbusterPresent;
            bool rsPresentInRun = ctx.RadioShackPresent;

            foreach (var year in years)
            {
                var stripTenants = ctx.ResolveChainTenants(year, "strip_mall", "thriving");

                var expectBb = bbPresentInRun && year >= 1990;
                var hasBb    = stripTenants.Any(t => t.Name == "Blockbuster");
                if (hasBb != expectBb)
                    errs.Add($"seed={seed} year={year}: Blockbuster presence flicker (present-in-run={bbPresentInRun}, expected={expectBb}, actual={hasBb})");

                var expectRs  = rsPresentInRun; // RadioShack schedule covers every year while healthy (Generic/Named/Ghost)
                var hasRsStrip = stripTenants.Any(t => t.Name == "RadioShack");
                if (hasRsStrip != expectRs)
                    errs.Add($"seed={seed} year={year}: RadioShack presence flicker in strip_mall (present-in-run={rsPresentInRun}, expected={expectRs}, actual={hasRsStrip})");

                var dtTenants = ctx.ResolveChainTenants(year, "downtown_street", "thriving");
                var hasRsDt   = dtTenants.Any(t => t.Name == "RadioShack");
                if (hasRsDt != expectRs)
                    errs.Add($"seed={seed} year={year}: RadioShack presence flicker in downtown_street (present-in-run={rsPresentInRun}, expected={expectRs}, actual={hasRsDt})");
            }
        }

        f.Add(("C33", "Chain tenant presence is stable across a run: no flicker between schedule-eligible eras",
            errs.Count == 0, errs.Count == 0 ? "No presence flicker across 20 seeds x 6 eras" : Join(errs)));
    }

    // Invokes PromptService.BuildSceneBlock (private) directly via reflection with
    // a forced abandoned/squatted condition, so the derelict branch is exercised
    // deterministically for every year regardless of what condition the smoke
    // fixtures happened to sample — this targets the wiring fix itself, not just
    // ResolveChainTenants' own contract (already covered by C32/C33).
    private static string InvokeBuildSceneBlock(
        EraProfile era, SceneContent? content, string sceneType, string condition, GenerationContext context)
    {
        var method = typeof(PromptService).GetMethod("BuildSceneBlock",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)
            ?? throw new InvalidOperationException("PromptService.BuildSceneBlock not found");

        var gasSign = new GenerationContext.GasSign(GenerationContext.GasSignKind.DeadBoard, null);
        var rng = new Random(1); // unused by the derelict branch — no sampling happens there
        var args = new object?[] { era, content, sceneType, condition, gasSign, rng, context };
        return (string)method.Invoke(null, args)!;
    }

    // A derelict era must still surface a ghost sign when the run has that chain
    // and its schedule has already put it past its closing year (or the demotion
    // from Named) — that's the whole point of routing ResolveChainTenants through
    // the derelict branch instead of going silent there.
    private static void DoC34(Dictionary<int, EraProfile> eras, List<(string, string, bool?, string)> f)
    {
        var errs = new List<string>();
        string[] derelictConditions = { "abandoned", "squatted" };
        string[] sceneTypes = { "strip_mall", "downtown_street" };

        for (int seed = 1; seed <= 30; seed++)
            foreach (var sceneType in sceneTypes)
            {
                var ctx = new GenerationContext { Random = new Random(seed) };
                _ = ctx.BlockbusterPresent;
                _ = ctx.RadioShackPresent;

                foreach (var year in Years)
                foreach (var condition in derelictConditions)
                {
                    var text = InvokeBuildSceneBlock(eras[year], ContentFor(eras, year, sceneType), sceneType, condition, ctx);
                    var tenants = ctx.ResolveChainTenants(year, sceneType, condition);

                    foreach (var tenant in tenants)
                    {
                        if (tenant.Kind != GenerationContext.ChainSignKind.Ghost)
                            errs.Add($"seed={seed} year={year} scene={sceneType} condition={condition}: ResolveChainTenants returned non-Ghost kind {tenant.Kind} in a derelict era");
                        if (!text.Contains($"- {tenant.Text}"))
                            errs.Add($"seed={seed} year={year} scene={sceneType} condition={condition}: derelict block missing expected ghost line for {tenant.Name}");
                    }
                }
            }

        f.Add(("C34", "A derelict era emits the ghost line whenever the run's chain schedule calls for one",
            errs.Count == 0, errs.Count == 0 ? "Ghost lines present wherever the schedule calls for them" : Join(errs)));
    }

    // The derelict branch exists to keep live business out of a closed-down
    // scene — a Named or Generic chain line there would be exactly the
    // contradiction it's designed to prevent.
    private static void DoC35(Dictionary<int, EraProfile> eras, List<(string, string, bool?, string)> f)
    {
        var errs = new List<string>();
        string[] derelictConditions = { "abandoned", "squatted" };
        string[] sceneTypes = { "strip_mall", "downtown_street" };
        string[] namedOrGenericMarkers = { "Blockbuster", "RadioShack", "CB RADIOS", "STEREO - SPEAKERS - TAPES" };

        for (int seed = 1; seed <= 30; seed++)
            foreach (var sceneType in sceneTypes)
            {
                var ctx = new GenerationContext { Random = new Random(seed) };
                _ = ctx.BlockbusterPresent;
                _ = ctx.RadioShackPresent;

                foreach (var year in Years)
                foreach (var condition in derelictConditions)
                {
                    var text = InvokeBuildSceneBlock(eras[year], ContentFor(eras, year, sceneType), sceneType, condition, ctx);
                    foreach (var marker in namedOrGenericMarkers)
                        if (text.Contains(marker))
                            errs.Add($"seed={seed} year={year} scene={sceneType} condition={condition}: derelict block contains disallowed marker '{marker}'");
                }
            }

        f.Add(("C35", "A derelict era never emits a Named or Generic chain tenant line",
            errs.Count == 0, errs.Count == 0 ? "No Named/Generic chain content in any derelict block" : Join(errs)));
    }

    // Street-shaped placement language (sidewalk zones, "hug the curb", "side of
    // the street") must be gated on the SceneDna geometry that actually has a
    // sidewalk/on-street parking — a forecourt, apron or lot fixture must never
    // inherit it. Same abandoned/vehicle skips as C15/C18.
    private static void DoC36(
        Dictionary<int, Prompt> dtRun1, Dictionary<int, Prompt> dtRun2,
        Dictionary<int, Prompt> gasRun1, Dictionary<int, Prompt> smRun1, Dictionary<int, Prompt> arRun1,
        Prompt unknownPrompt,
        List<(string, string, bool?, string)> f)
    {
        var errs = new List<string>();

        // unknownPrompt: Sidewalks false, Parking "gravel lot" — the only
        // fixture exercising the off-street PEOPLE branch end to end.
        // ("sidewalk" itself isn't checked bare: PRESERVE always states
        // "sidewalks present/absent" regardless of geometry — checking the
        // gated zone/trailing-clause wording specifically instead.)
        if (unknownPrompt.Text.Contains("near sidewalk") || unknownPrompt.Text.Contains("stay on sidewalks"))
            errs.Add("unknown/1985: off-street prompt still uses on-street PEOPLE wording");
        if (unknownPrompt.Text.Contains("hug the curb"))
            errs.Add("unknown/1985: off-street prompt still says 'hug the curb'");
        if (unknownPrompt.Text.Contains("side of the street"))
            errs.Add("unknown/1985: off-street prompt still says 'side of the street'");
        if (!unknownPrompt.Text.Contains("stay on the lot apron"))
            errs.Add("unknown/1985: off-street prompt missing 'stay on the lot apron'");

        // dtRun1/dtRun2: Sidewalks true, "parallel street parking both sides" —
        // must keep its exact current output, unchanged by this gate.
        foreach (var (run, label) in new[] { (dtRun1, "downtown/run1"), (dtRun2, "downtown/run2") })
            foreach (var year in Years)
            {
                if (!run.TryGetValue(year, out var prompt)) continue;
                if (prompt.SceneCondition != "abandoned" && !prompt.Text.Contains("stay on sidewalks"))
                    errs.Add($"{label}/{year}: on-street prompt missing 'stay on sidewalks'");
                if (prompt.SelectedVehicles.Count > 0 && !prompt.Text.Contains("hug the curb"))
                    errs.Add($"{label}/{year}: on-street prompt missing 'hug the curb'");
            }

        // gasRun1/smRun1/arRun1: off-street parking (forecourt/lot/apron) —
        // vehicles must never hug a curb or place along "the street".
        foreach (var (run, label) in new[] { (gasRun1, "gas/run1"), (smRun1, "strip/run1"), (arRun1, "auto/run1") })
            foreach (var year in Years)
            {
                if (!run.TryGetValue(year, out var prompt)) continue;
                if (prompt.Text.Contains("hug the curb"))
                    errs.Add($"{label}/{year}: off-street prompt says 'hug the curb'");
                var line = PlacementLine(prompt);
                if (line is not null && line.Contains("side of the street"))
                    errs.Add($"{label}/{year}: off-street PLACEMENT line says 'side of the street'");
            }

        f.Add(("C36", "Street-shaped placement language (sidewalk zones, curb-hugging, PLACEMENT wording) is gated on SceneDna geometry",
            errs.Count == 0, errs.Count == 0 ? "Street language present only where geometry supports it" : Join(errs)));
    }

    // Synthetic base prompts carry the scene's geometry but never reference a
    // source photo, and the shared BuildPreserveBlock refactor left era prompts
    // byte-identical (asserted via their unchanged PRESERVE header).
    private static async Task DoC37(
        IPromptService promptService,
        IDataService dataService,
        SceneDna gasScene, SceneDna downtownScene, SceneDna stripMallScene, SceneDna autoRepairScene,
        Dictionary<int, Prompt> gasRun1,
        List<(string, string, bool?, string)> f)
    {
        var errs = new List<string>();

        const string genericPhrase = "an ordinary American location";
        var phrases = await dataService.LoadSceneTypePhrasesAsync();

        // Every scene type that has a caption voice must also have a base-prompt
        // phrase. Without this, a scene type added later silently falls back to
        // the generic wording — the exact failure the phrase exists to prevent,
        // and invisible if the hardcoded list below is all that drives the check.
        foreach (var sceneType in CaptionService.AnglesByScene.Keys)
            if (!phrases.ContainsKey(sceneType))
                errs.Add($"scene type '{sceneType}' has a caption voice but no scene-types.txt entry");

        foreach (var (scene, label) in new[]
        {
            (gasScene,        "gas_station"),
            (downtownScene,   "downtown_street"),
            (stripMallScene,  "strip_mall"),
            (autoRepairScene, "auto_repair"),
            (MakeMallScene(), "mall"),
        })
        {
            var text = await promptService.BuildBaseAsync(scene, Years[0]);

            // The base is the only prompt that builds geometry from nothing, so it
            // must name the kind of place. Without this the model infers the type
            // from immutable elements that describe a canopy or a pylon without
            // ever saying "gas station".
            if (text.Contains("{SCENE_TYPE_PHRASE}"))
                errs.Add($"{label}: base prompt still contains an unsubstituted {{SCENE_TYPE_PHRASE}}");
            if (!phrases.TryGetValue(label, out var expectedPhrase))
                errs.Add($"{label}: no scene-types.txt entry");
            else if (!text.Contains(expectedPhrase))
                errs.Add($"{label}: base prompt missing its scene type phrase '{expectedPhrase}'");
            if (text.Contains(genericPhrase))
                errs.Add($"{label}: base prompt still says '{genericPhrase}' for a known scene type");

            // The base is dated from the run's earliest year, so an undated base
            // does not render as present-day and force every early era to undo it.
            if (text.Contains("{PERIOD_BLOCK}"))
                errs.Add($"{label}: base prompt still contains an unsubstituted {{PERIOD_BLOCK}}");
            if (!text.Contains($"PERIOD — build the scene as it stood in {Years[0]}"))
                errs.Add($"{label}: base prompt is not dated to the base year {Years[0]}");

            // Geometry actually made it in, in the same shape BuildPreserveBlock emits.
            foreach (var b in scene.Geometry.Buildings)
                if (!text.Contains($"{b.Type} building at {b.Position}"))
                    errs.Add($"{label}: base prompt missing building '{b.Type}'");
            foreach (var r in scene.Geometry.Roads)
                if (!text.Contains($"{r.Type} road, {r.Lanes}-lane, {r.Surface}"))
                    errs.Add($"{label}: base prompt missing road '{r.Type}'");

            // Token substituted, and framed as construction rather than preservation.
            if (text.Contains("{GEOMETRY_BLOCK}"))
                errs.Add($"{label}: base prompt still contains an unsubstituted {{GEOMETRY_BLOCK}}");
            if (!text.Contains("BUILD THIS SCENE"))
                errs.Add($"{label}: base prompt missing the 'BUILD THIS SCENE' header");

            // Nothing may point at a photo that synthetic mode never sends.
            if (text.Contains("uploaded photo", StringComparison.OrdinalIgnoreCase))
                errs.Add($"{label}: base prompt references an 'uploaded photo'");
            if (text.Contains("source", StringComparison.OrdinalIgnoreCase))
                errs.Add($"{label}: base prompt references a 'source'");
        }

        // PARKED (second half only): the era prompts no longer carry the
        // generated PRESERVE header — they use the short fixed instruction.
        // Restore together with the BuildPreserveBlock call in PromptService
        // line 89 by uncommenting the block below and restoring the original
        // description/detail wording on the f.Add beneath it.
        //
        // The refactor that parameterised BuildPreserveBlock must not have moved
        // the era prompts' header off its exact original wording.
        // foreach (var year in Years)
        //     if (gasRun1.TryGetValue(year, out var prompt)
        //         && !prompt.Text.Contains("PRESERVE (must match source exactly)"))
        //         errs.Add($"gas/run1/{year}: era prompt lost the 'PRESERVE (must match source exactly)' header");

        // The base-prompt half above stays live and still asserts real behaviour;
        // only the era-header half is parked, so this reports PASS/FAIL as normal.
        f.Add(("C37", "Synthetic base prompts name their scene type and carry scene geometry, with no source-photo wording (era PRESERVE header assertion parked)",
            errs.Count == 0, errs.Count == 0
                ? "Synthetic base prompts well-formed; era header assertion parked while the short era PRESERVE is evaluated — restore together with the BuildPreserveBlock call in PromptService line 89"
                : Join(errs)));
    }

    // A tree's size must be stated in exactly one place per prompt: the era
    // PRESERVE block never states it (the TREES section already gives that
    // tree's size for the target era, so a "must match source exactly" tree
    // line here would freeze it at the source photo's size and contradict
    // that), while the synthetic base — which has no TREES section and no
    // photo at all — is the only place its trees get described, so it must.
    private static async Task DoC38(
        IPromptService promptService,
        SceneDna gasScene, SceneDna downtownScene, SceneDna stripMallScene, SceneDna autoRepairScene,
        Dictionary<int, Prompt> gasRun1, Dictionary<int, Prompt> gasRun2,
        Dictionary<int, Prompt> dtRun1,  Dictionary<int, Prompt> dtRun2,
        Dictionary<int, Prompt> smRun1,  Dictionary<int, Prompt> smRun2,
        Dictionary<int, Prompt> arRun1,  Dictionary<int, Prompt> arRun2,
        Prompt unknownPrompt,
        List<(string, string, bool?, string)> f)
    {
        var errs = new List<string>();

        foreach (var (year, prompt, label) in AllPrompts(gasRun1, gasRun2, dtRun1, dtRun2, smRun1, smRun2, arRun1, arRun2, unknownPrompt))
        {
            var preserveIdx = prompt.Text.IndexOf("PRESERVE (must match source exactly)", StringComparison.Ordinal);
            var outputIdx   = prompt.Text.IndexOf("OUTPUT FORMAT", StringComparison.Ordinal);
            if (preserveIdx < 0 || outputIdx < preserveIdx) continue;

            var preserveSection = prompt.Text[preserveIdx..outputIdx];
            // Word-boundary match: a naive substring check false-positives on
            // "street" (which contains "tree").
            if (System.Text.RegularExpressions.Regex.IsMatch(preserveSection, @"\btrees?\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                errs.Add($"{label}/{year}: era PRESERVE block mentions a tree — the TREES section already states its size for this era");
        }

        foreach (var (scene, label) in new[]
        {
            (gasScene,        "gas_station"),
            (downtownScene,   "downtown_street"),
            (stripMallScene,  "strip_mall"),
            (autoRepairScene, "auto_repair"),
        })
        {
            var text = await promptService.BuildBaseAsync(scene, Years[0]);
            foreach (var tree in scene.Environment.Trees)
                if (!text.Contains($"{tree.Type} tree at {tree.Position}"))
                    errs.Add($"{label}: synthetic base missing tree line for '{tree.Type}' at '{tree.Position}'");
        }

        f.Add(("C38", "A tree's size is stated in exactly one place per prompt: never in the era PRESERVE block, always in the synthetic base's geometry block",
            errs.Count == 0, errs.Count == 0 ? "No double-statement; synthetic base carries every tree the era PRESERVE block omits" : Join(errs)));
    }

    // "new" means pristine surfaces (ConditionDescriptor), but the era data's
    // ghost-sign extra is an old, weathered wall ad — left unreconciled that
    // directly contradicts "new" with no relationship stated. PromptService
    // swaps it for an explicit reconciling line whenever condition is "new";
    // this asserts neither the raw contradiction nor an unexplained mention
    // ever reaches a "new"-condition prompt.
    private static void DoC39(
        Dictionary<int, Prompt> gasRun1, Dictionary<int, Prompt> gasRun2,
        Dictionary<int, Prompt> dtRun1,  Dictionary<int, Prompt> dtRun2,
        Dictionary<int, Prompt> smRun1,  Dictionary<int, Prompt> smRun2,
        Dictionary<int, Prompt> arRun1,  Dictionary<int, Prompt> arRun2,
        Prompt unknownPrompt,
        List<(string, string, bool?, string)> f)
    {
        var errs = new List<string>();
        string[] rawContradictionMarkers = { "weathered but still clearly readable", "weathered but readable" };
        const string reconciled = "all other buildings and storefronts look newly built or recently renovated";

        foreach (var (year, prompt, label) in AllPrompts(gasRun1, gasRun2, dtRun1, dtRun2, smRun1, smRun2, arRun1, arRun2, unknownPrompt))
        {
            if (prompt.SceneCondition != "new") continue;

            foreach (var marker in rawContradictionMarkers)
                if (prompt.Text.Contains(marker))
                    errs.Add($"{label}/{year}: 'new' condition prompt still has the raw ghost-sign contradiction '{marker}'");

            if (prompt.Text.Contains("ghost sign", StringComparison.OrdinalIgnoreCase) && !prompt.Text.Contains(reconciled))
                errs.Add($"{label}/{year}: 'new' condition prompt mentions a ghost sign without the reconciling clause");
        }

        f.Add(("C39", "A 'new' condition prompt never pairs pristine surfaces with an unexplained weathered ghost sign",
            errs.Count == 0, errs.Count == 0 ? "No unreconciled ghost-sign contradiction in any 'new' condition prompt" : Join(errs)));
    }

    // image-template.txt carries the PRIORITY ORDER rule, and every era prompt's
    // SIGNAGE RESTRICTION whitelist lists exactly (no more, no fewer) the quoted
    // strings CollectSignText would pull from that prompt's own scene block —
    // invoked via reflection so this tracks the real implementation rather than
    // a hand-rolled duplicate that could drift from it. The old blanket
    // "only what appears in quotes" line must be gone everywhere.
    private static async Task DoC40(
        IDataService dataService,
        Dictionary<int, Prompt> gasRun1, Dictionary<int, Prompt> gasRun2,
        Dictionary<int, Prompt> dtRun1,  Dictionary<int, Prompt> dtRun2,
        Dictionary<int, Prompt> smRun1,  Dictionary<int, Prompt> smRun2,
        Dictionary<int, Prompt> arRun1,  Dictionary<int, Prompt> arRun2,
        Prompt unknownPrompt,
        List<(string, string, bool?, string)> f)
    {
        var errs = new List<string>();

        try
        {
            var template = await dataService.LoadPromptAsync("image-template");
            if (!template.Contains("PRIORITY ORDER — when instructions conflict, the lower number wins"))
                errs.Add("image-template.txt missing the PRIORITY ORDER header/rule line");
        }
        catch (Exception ex)
        {
            errs.Add($"image-template.txt failed to load: {ex.Message}");
        }

        var collectSignText = typeof(PromptService).GetMethod("CollectSignText",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)
            ?? throw new InvalidOperationException("PromptService.CollectSignText not found");

        foreach (var (year, prompt, label) in AllPrompts(gasRun1, gasRun2, dtRun1, dtRun2, smRun1, smRun2, arRun1, arRun2, unknownPrompt))
        {
            if (prompt.Text.Contains("Sign text is only what appears in quotes"))
                errs.Add($"{label}/{year}: old 'Sign text is only what appears in quotes' line still present");

            var transformIdx = prompt.Text.IndexOf("TRANSFORM TO", StringComparison.Ordinal);
            var peopleIdx     = prompt.Text.IndexOf("\nPEOPLE\n", StringComparison.Ordinal);
            if (transformIdx < 0 || peopleIdx < 0 || peopleIdx <= transformIdx) continue;

            var signageIdx        = prompt.Text.IndexOf("SIGNAGE RESTRICTION", transformIdx, StringComparison.Ordinal);
            var hasSignageSection = signageIdx >= 0 && signageIdx < peopleIdx;
            var sceneSection      = prompt.Text[transformIdx..(hasSignageSection ? signageIdx : peopleIdx)];

            var expected = (List<string>)collectSignText.Invoke(null, new object?[] { sceneSection })!;

            if (expected.Count == 0)
            {
                if (hasSignageSection)
                    errs.Add($"{label}/{year}: unexpected SIGNAGE RESTRICTION section with no quoted strings in the scene block");
                continue;
            }

            if (!hasSignageSection)
            {
                errs.Add($"{label}/{year}: missing SIGNAGE RESTRICTION section despite {expected.Count} quoted strings in the scene block");
                continue;
            }

            var whitelistSection = prompt.Text[signageIdx..peopleIdx];
            foreach (var s in expected)
                if (!whitelistSection.Contains($"'{s}'"))
                    errs.Add($"{label}/{year}: SIGNAGE RESTRICTION missing '{s}' from its own scene block");

            // Reverse: every listed entry must be one CollectSignText would
            // itself have produced from this same scene block — no extras.
            foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(
                whitelistSection, @"^'(.+)'$", System.Text.RegularExpressions.RegexOptions.Multiline))
                if (!expected.Contains(m.Groups[1].Value))
                    errs.Add($"{label}/{year}: SIGNAGE RESTRICTION lists '{m.Groups[1].Value}' which is not in its own scene block");
        }

        f.Add(("C40", "image-template.txt carries the PRIORITY ORDER rule; every era prompt's SIGNAGE RESTRICTION whitelist lists exactly the quoted strings from its own scene block; the old blanket quotes-only line is gone",
            errs.Count == 0, errs.Count == 0 ? "Priority order present; signage whitelist consistent everywhere; old line removed" : Join(errs)));
    }

    // End-to-end caption assembly. C26 validates the body files as text; this
    // drives the real CaptionService against them and checks the output varies:
    // captions are assembled locally now, so a stuck rotation or an unsubstituted
    // placeholder would ship straight to a post with nothing else to catch it.
    private static async Task DoC41(
        IDataService dataService,
        SceneDna gasScene, SceneDna downtownScene, SceneDna stripMallScene,
        SceneDna autoRepairScene, SceneDna unknownScene,
        List<(string, string, bool?, string)> f)
    {
        var errs = new List<string>();
        var service = new CaptionService(
            dataService, Microsoft.Extensions.Logging.Abstractions.NullLogger<CaptionService>.Instance);

        var scenes = new[]
        {
            (Scene: gasScene,        Label: "gas_station"),
            (Scene: downtownScene,   Label: "downtown_street"),
            (Scene: stripMallScene,  Label: "strip_mall"),
            (Scene: autoRepairScene, Label: "auto_repair"),
            (Scene: unknownScene,    Label: "unknown"),   // exercises the base.txt fallback
        };

        var narrative = new SceneNarrative(
            FirstYear: 1975, LastYear: 2025, FinalCondition: "abandoned",
            FirstBrand: "TEXACO", LastBrand: "SHELL", RebrandOccurred: true);

        // 1. Every scene type assembles a usable caption.
        var descriptions = new Dictionary<string, string>();
        foreach (var (scene, label) in scenes)
        {
            Caption caption;
            try
            {
                caption = await service.GenerateAsync(scene, narrative);
            }
            catch (Exception ex)
            {
                errs.Add($"{label}: GenerateAsync threw: {ex.Message}");
                continue;
            }

            var text = caption.Description;
            descriptions[label] = text;

            if (string.IsNullOrWhiteSpace(text))
                errs.Add($"{label}: empty description");
            // Any surviving brace means a placeholder never got substituted and
            // would be posted literally.
            if (text.Contains('{') || text.Contains('}'))
                errs.Add($"{label}: unsubstituted placeholder in output: {text[Math.Max(0, text.IndexOf('{'))..Math.Min(text.Length, text.IndexOf('{') + 30)]}");
            if (text.Contains("1975") is false || text.Contains("2025") is false)
                errs.Add($"{label}: narrative years missing from the assembled caption");
            if (!text.Contains(CaptionService.MapFinalCondition("abandoned")))
                errs.Add($"{label}: mapped condition phrase missing from the assembled caption");
            if (!text.TrimEnd().EndsWith('?'))
                errs.Add($"{label}: assembled caption does not end on a question");
            if (caption.Hashtags.Count == 0)
                errs.Add($"{label}: no hashtags appended");
        }

        // 2. Different scene types must not produce the same words — the whole
        // point of per-scene files is that a forecourt and a main street differ.
        var duplicates = descriptions
            .GroupBy(kv => kv.Value)
            .Where(g => g.Count() > 1)
            .Select(g => string.Join(" == ", g.Select(kv => kv.Key)))
            .ToList();
        foreach (var dupe in duplicates)
            errs.Add($"identical caption text across scene types: {dupe}");

        // 3. Weekly rotation reaches every body. index advances with the week, so
        // any run of bodyCount consecutive weeks must visit all of them — this
        // catches a rotation collapsed onto a subset.
        foreach (var (scene, label) in scenes)
        {
            var raw = await LoadBodiesFor(dataService, scene.SceneType);
            var count = CaptionService.SplitBodies(raw).Count;
            if (count == 0) { errs.Add($"{label}: no bodies to rotate"); continue; }

            var seen = Enumerable.Range(1, count)
                .Select(w => CaptionService.SelectBodyIndex(w, scene.Id, count))
                .Distinct()
                .Count();
            if (seen != count)
                errs.Add($"{label}: {count} consecutive weeks reach only {seen}/{count} bodies");
        }

        // 4. Two scenes captioned in the same week must not collapse onto one
        // body — that is what the scene-id offset exists for.
        var gasBodies = CaptionService.SplitBodies(await LoadBodiesFor(dataService, "gas_station")).Count;
        if (gasBodies > 1)
        {
            var ids = new[] { "smoke-a", "smoke-b", "smoke-c", "smoke-d", "smoke-e", "smoke-f" };
            var indices = ids.Select(id => CaptionService.SelectBodyIndex(30, id, gasBodies)).Distinct().Count();
            if (indices < 2)
                errs.Add($"same-week scene ids all map to one body ({indices} distinct across {ids.Length} ids) — the id offset is not separating scenes");
        }

        f.Add(("C41", "Caption assembly produces a complete, fully substituted caption for every scene type; scene types differ; weekly rotation reaches every body; same-week scenes are separated by the id offset",
            errs.Count == 0, errs.Count == 0 ? "Caption assembly varied and fully substituted" : Join(errs)));
    }

    // Mirrors CaptionService's own lookup: scene-specific file, else base.
    private static async Task<string> LoadBodiesFor(IDataService dataService, string? sceneType)
    {
        var name = string.IsNullOrWhiteSpace(sceneType) ? "base" : sceneType;
        try
        {
            return await dataService.LoadCaptionBodiesAsync(name);
        }
        catch (FileNotFoundException)
        {
            return await dataService.LoadCaptionBodiesAsync("base");
        }
    }

    // Packed-crowd mall scenes (crowd="packed" in the era JSON, years 1975/1985/
    // 1995) render a dense, uncountable crowd and a full lot instead of an
    // "EXACTLY N" count: PEOPLE gets the fixed crowd-wording line, VEHICLES lists
    // exactly 5 representative models with no PLACEMENT line.
    private static async Task DoC42(
        IPromptService promptService,
        Dictionary<int, EraProfile> eras,
        List<(string, string, bool?, string)> f)
    {
        var errs = new List<string>();
        const string crowdWording = "A DENSE CROWD of shoppers";
        const string lotWording   = "A FULL parking lot";

        var mallScene = MakeMallScene();
        foreach (var year in new[] { 1975, 1985, 1995 })
        {
            var ctx    = new GenerationContext { Random = new Random(42), TotalEras = 1 };
            var prompt = await promptService.BuildAsync(mallScene, eras[year], ctx);

            if (!prompt.Text.Contains(crowdWording))
                errs.Add($"mall/{year}: missing packed-crowd PEOPLE wording '{crowdWording}'");
            if (!prompt.Text.Contains(lotWording))
                errs.Add($"mall/{year}: missing packed-crowd VEHICLES wording '{lotWording}'");
            if (prompt.SelectedVehicles.Count != 5)
                errs.Add($"mall/{year}: expected exactly 5 representative vehicles, got {prompt.SelectedVehicles.Count}");
            if (prompt.Text.Contains("PLACEMENT:"))
                errs.Add($"mall/{year}: packed mode emitted a PLACEMENT line");
        }

        f.Add(("C42", "Packed-crowd mall scenes render crowd/lot wording, exactly 5 representative vehicles, no PLACEMENT line",
            errs.Count == 0, errs.Count == 0 ? "Packed crowd rendering correct across 1975/1985/1995" : Join(errs)));
    }

    // ── Report ────────────────────────────────────────────────────────────────

    private static async Task WriteReport(
        List<(string Id, string Desc, bool? Pass, string Detail)> findings,
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
            var status = pass switch { true => "✅ PASS", false => "❌ FAIL", null => "\u26D4 DISABLED" };
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
                id, pass switch { true => "PASS", false => "FAIL", null => "DISABLED" }, detail);
    }

    private static string Join(IEnumerable<string> items) => string.Join("; ", items);
}
