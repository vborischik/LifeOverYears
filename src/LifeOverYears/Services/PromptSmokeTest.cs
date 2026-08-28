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
        var cornerShopScene = MakeCornerShopScene();
        var freestandingShopScene = MakeFreestandingShopScene();
        var motelScene      = MakeMotelScene();
        var highwayUrbanScene = MakeHighwayScene("urban");
        var highwayRuralScene = MakeHighwayScene("rural");
        var highwayBuiltScene = MakeHighwayScene("urban", withBuildings: true);
        var mallScene2          = MakeMallScene();
        var shoppingCenterScene = MakeShoppingCenterScene();
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
        var csRun1  = await BuildRun(promptService, cornerShopScene, eras, 42,   Years);
        var csRun2  = await BuildRun(promptService, cornerShopScene, eras, 1337, Years);
        var mtRun1  = await BuildRun(promptService, motelScene,      eras, 42,   Years);
        var mtRun2  = await BuildRun(promptService, motelScene,      eras, 1337, Years);
        var fsRun1  = await BuildRun(promptService, freestandingShopScene, eras, 42,   Years);
        var fsRun2  = await BuildRun(promptService, freestandingShopScene, eras, 1337, Years);
        var hwUrban = await BuildRun(promptService, highwayUrbanScene, eras, 42, Years);
        var hwRural = await BuildRun(promptService, highwayRuralScene, eras, 42, Years);
        var hwBuilt = await BuildRun(promptService, highwayBuiltScene, eras, 42, Years);
        // The two packed scene types had fixtures but no run of their own, so
        // nothing walked them era by era the way every other type is walked.
        var mallRun = await BuildRun(promptService, mallScene2,          eras, 42, Years);
        var scRun   = await BuildRun(promptService, shoppingCenterScene, eras, 42, Years);

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
        await SaveRun(cornerShopScene.SceneType, 1, csRun1);
        await SaveRun(cornerShopScene.SceneType, 2, csRun2);
        await SaveRun(motelScene.SceneType,      1, mtRun1);
        await SaveRun(motelScene.SceneType,      2, mtRun2);
        await SaveRun(freestandingShopScene.SceneType, 1, fsRun1);
        await SaveRun(freestandingShopScene.SceneType, 2, fsRun2);
        // Named by content key, not scene type: both are "highway" and would
        // otherwise overwrite each other's output.
        await SaveRun("highway_urban", 1, hwUrban);
        await SaveRun("highway_rural", 1, hwRural);
        // The same urban flavor with buildings in frame — the only run that
        // carries background tenants, kept separate so both cases are readable.
        await SaveRun("highway_urban_buildings", 1, hwBuilt);
        await SaveRun(mallScene2.SceneType,          1, mallRun);
        await SaveRun(shoppingCenterScene.SceneType, 1, scRun);
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
        await DoC43(promptService, eras, gasScene, findings);
        DoC44(gasRun1, gasRun2, dtRun1, dtRun2, smRun1, smRun2, arRun1, arRun2, unknownPrompt, eras, findings);
        DoC45(eras, logger, findings);
        await DoC42(promptService, eras, findings);
        await DoC46(promptService, eras, gasRun1, gasRun2, dtRun1, dtRun2, smRun1, smRun2, arRun1, arRun2, findings);
        DoC47(eras, findings);
        await DoC48(promptService, gasScene, findings);
        await DoC49(promptService, eras, downtownScene, stripMallScene, findings);
        await DoC50(promptService, eras, downtownScene, stripMallScene, findings);
        await DoC51(promptService, eras, downtownScene, stripMallScene, findings);
        await DoC52(promptService, eras, downtownScene, stripMallScene, findings);
        await DoC53(promptService, eras, downtownScene, stripMallScene, gasScene, findings);
        await DoC54(promptService, eras, stripMallScene, findings);
        await DoC55(dataService, gasScene, findings);
        DoC56(dtRun1, dtRun2, smRun1, smRun2, gasRun1, arRun1, findings);
        await DoC57(dataService, findings);
        await DoC58(dataService, gasRun1, gasRun2, dtRun1, dtRun2, smRun1, smRun2, arRun1, arRun2, csRun1, csRun2, unknownPrompt, findings);
        DoC60(csRun1, csRun2, fsRun1, fsRun2, eras, logger, findings);
        DoC63(findings);
        await DoC64(promptService, eras, highwayUrbanScene, hwUrban, hwRural, findings);
        await DoC65(promptService, dataService, eras, highwayBuiltScene, findings);
        DoC66(eras, hwUrban, hwBuilt, findings);
        DoC67(gasRun1, dtRun1, smRun1, arRun1, csRun1, hwUrban, hwRural, hwBuilt, unknownPrompt, eras, findings);
        DoC68(gasRun1, dtRun1, hwUrban, hwRural, hwBuilt, findings);
        await DoC69(promptService, highwayUrbanScene, highwayBuiltScene, downtownScene, findings);
        await DoC70(promptService, eras, highwayUrbanScene, downtownScene, findings);
        DoC73(new (string, Dictionary<int, Prompt>)[]
        {
            ("gas_station", gasRun1), ("downtown_street/run1", dtRun1), ("downtown_street/run2", dtRun2),
            ("strip_mall/run1", smRun1), ("strip_mall/run2", smRun2),
            ("auto_repair/run1", arRun1), ("auto_repair/run2", arRun2),
            ("corner_shop/run1", csRun1), ("corner_shop/run2", csRun2),
            ("freestanding_shop/run1", fsRun1), ("freestanding_shop/run2", fsRun2),
            ("motel/run1", mtRun1), ("motel/run2", mtRun2),
            ("highway_urban", hwUrban), ("highway_rural", hwRural), ("highway_urban_buildings", hwBuilt),
            ("mall", mallRun), ("shopping_center", scRun),
        }, unknownPrompt, findings);
        await DoC74(promptService, eras, logger, findings);
        DoC62(csRun1, csRun2, gasRun1, dtRun1, smRun1, arRun1, findings);
        await DoC61(dataService, gasRun1, gasRun2, dtRun1, dtRun2, smRun1, smRun2, arRun1, arRun2, unknownPrompt, findings);
        await DoC72(dataService, logger, findings);
        await DoC71(dataService, findings);
        await DoC59(dataService, findings);

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

    private static SceneDna MakeCornerShopScene() => new(
        Id:        "smoke-corner-shop",
        CreatedAt: "2025-01-01T00:00:00Z",
        SceneType: "corner_shop",
        Camera: new Camera(Height: "eye-level", Direction: "facade", Fov: 74),
        Geometry: new Geometry(
            Roads:
            [
                new Road(
                    Type:     "residential",
                    Lanes:    2,
                    Markings: ["center line", "crosswalk at the corner"],
                    Surface:  "asphalt")
            ],
            Sidewalks: true,
            Curbs:     true,
            Buildings:
            [
                new Building(
                    Type:      "two-story corner building with a shop at street level",
                    Position:  "on the corner, facade to the sidewalk",
                    Stories:   2,
                    Materials: ["red brick", "plate glass"],
                    Roof:      "flat parapet",
                    Setback:   "at-street")
            ],
            Driveways: [],
            Parking:   "parallel street parking along the curb"),
        Environment: new Environment(
            Terrain:   "urban flat",
            Utilities: ["overhead power lines", "utility pole at the corner"],
            Trees:
            [
                new Tree(Position: "sidewalk beside the entrance", Size: "small", Type: "honey locust")
            ],
            Landscape: ["concrete sidewalk squares", "granite curb at the corner"]),
        ImmutableElements:
        [
            "sign band above the storefront",
            "blank brick side wall along the side street",
            "entrance door on the corner chamfer"
        ]);

    // Same scripted trade arc as corner_shop, on the other footprint that arc
    // supports: a standalone building set back from the road with its own
    // parking apron facing the entrance, rather than a sidewalk storefront.
    private static SceneDna MakeFreestandingShopScene() => new(
        Id:        "smoke-freestanding-shop",
        CreatedAt: "2025-01-01T00:00:00Z",
        SceneType: "freestanding_shop",
        Camera: new Camera(Height: "eye-level", Direction: "facade", Fov: 76),
        Geometry: new Geometry(
            Roads:
            [
                new Road(
                    Type:     "commercial arterial",
                    Lanes:    2,
                    Markings: ["yellow center line", "white edge lines"],
                    Surface:  "asphalt")
            ],
            Sidewalks: false,
            Curbs:     false,
            Buildings:
            [
                new Building(
                    Type:      "one-story standalone shop set back from the road",
                    Position:  "centered on its own lot, facade facing the parking apron",
                    Stories:   1,
                    Materials: ["painted concrete block", "plate glass"],
                    Roof:      "flat parapet",
                    Setback:   "deep")
            ],
            Driveways: ["paved apron directly in front of the entrance"],
            Parking:   "off-street apron facing the facade, no on-street parking"),
        Environment: new Environment(
            Terrain:   "suburban flat",
            Utilities: ["overhead power lines", "utility pole at the lot edge"],
            Trees:
            [
                new Tree(Position: "lot edge beside the ground sign", Size: "small", Type: "honey locust")
            ],
            Landscape: ["painted parking bays", "grass strip along the road frontage"]),
        ImmutableElements:
        [
            "low ground sign at the edge of the apron",
            "paved parking apron facing the entrance",
            "no neighbouring storefronts under a shared roof"
        ]);

    // Both highway fixtures carry SceneType "highway" — the flavor comes from
    // terrain alone, which is exactly the split SceneContentKey exists to make.
    private static SceneDna MakeHighwayScene(string terrain, bool withBuildings = false) => new(
        Id:        $"smoke-highway-{terrain}{(withBuildings ? "-buildings" : "")}",
        CreatedAt: "2025-01-01T00:00:00Z",
        SceneType: "highway",
        Camera: new Camera(Height: "eye-level", Direction: "street", Fov: 70),
        Geometry: new Geometry(
            Roads:
            [
                new Road(
                    Type:     terrain == "urban" ? "urban interstate" : "rural two-lane highway",
                    Lanes:    terrain == "urban" ? 6 : 2,
                    Markings: terrain == "urban"
                        ? ["white lane lines", "wide white edge lines"]
                        : ["yellow centre line", "white edge lines"],
                    Surface:  terrain == "urban" ? "concrete" : "asphalt")
            ],
            Sidewalks: false,
            Curbs:     false,
            Buildings: withBuildings
                ?
                [
                    new Building(
                        Type:      "industrial warehouse",
                        Position:  "background, beyond the far shoulder",
                        Stories:   1,
                        Materials: ["concrete panels", "corrugated metal"],
                        Roof:      "flat",
                        Setback:   "200 feet from the roadway"),
                    new Building(
                        Type:      "low industrial unit",
                        Position:  "background, further along the same side",
                        Stories:   1,
                        Materials: ["concrete block"],
                        Roof:      "flat",
                        Setback:   "260 feet from the roadway")
                ]
                : [],
            Driveways: [],
            Parking:   ""),
        Environment: new Environment(
            Terrain:   terrain,
            Utilities: terrain == "urban"
                ? ["overhead sign gantry", "tall light standards on mast arms"]
                : ["wooden utility poles along the shoulder", "transmission towers across the fields"],
            Trees:
            [
                // "background" verbatim — this is what Vision wrote for the real
                // highway photo, and it is the position the tighter growth rate
                // keys off.
                new Tree(Position: "background", Size: "medium", Type: "oak")
            ],
            Landscape: terrain == "urban"
                ? ["mown embankment", "concrete median barrier"]
                : ["open fields both sides", "gravel shoulder"]),
        ImmutableElements:
        [
            terrain == "urban" ? "guardrail along the shoulder" : "guardrail on the curve",
            "the road alignment and lane count"
        ],
        // Deliberately numbered: this is what Vision writes when it reads a sign
        // in the photo, and it is what must not survive into the prompt.
        Distinctive:
        [
            "overhead green sign gantry reading I-95 NORTH",
            "the I-80 overpass crossing above the roadway",
            "Exit 4B signage on the right shoulder",
            "mile marker 118 at the start of the curve",
            "a distinctive curved concrete overpass with exposed beams"
        ]);

    private static SceneDna MakeMotelScene() => new(
        Id:        "smoke-motel",
        CreatedAt: "2025-01-01T00:00:00Z",
        SceneType: "motel",
        Camera: new Camera(Height: "eye-level", Direction: "facade", Fov: 80),
        Geometry: new Geometry(
            Roads:
            [
                new Road(
                    Type:     "two-lane highway",
                    Lanes:    2,
                    Markings: ["yellow center line", "white edge lines"],
                    Surface:  "asphalt")
            ],
            Sidewalks: false,
            Curbs:     false,
            Buildings:
            [
                new Building(
                    Type:      "single-story motel wing of numbered guest rooms",
                    Position:  "set back behind the parking apron, long face to the road",
                    Stories:   1,
                    Materials: ["painted concrete block", "painted metal doors"],
                    Roof:      "low pitch with a shallow walkway overhang",
                    Setback:   "deep")
            ],
            Driveways: ["asphalt apron in front of the room doors"],
            Parking:   "marked bays one per room door across the frontage"),
        Environment: new Environment(
            Terrain:   "flat roadside",
            Utilities: ["overhead power lines", "utility pole at the lot edge"],
            Trees:
            [
                new Tree(Position: "lot edge beside the pylon sign", Size: "medium", Type: "silver maple")
            ],
            Landscape: ["gravel strip along the building base", "grass verge out to the road"]),
        ImmutableElements:
        [
            "uniform row of numbered guest doors",
            "paired window beside each door",
            "freestanding pylon sign out by the road"
        ]);

    private static SceneDna MakeShoppingCenterScene() => new(
        Id:        "smoke-shopping-center",
        CreatedAt: "2025-01-01T00:00:00Z",
        SceneType: "shopping_center",
        Camera: new Camera(Height: "eye-level", Direction: "facade", Fov: 80),
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
                // The stepped parapet and one oversized unit are what make this a
                // shopping_center rather than a strip_mall — see vision.txt.
                new Building(
                    Type:      "anchor store block with its own raised parapet",
                    Position:  "left of the run, set slightly forward",
                    Stories:   1,
                    Materials: ["concrete block", "brick veneer"],
                    Roof:      "flat, raised parapet",
                    Setback:   "120 feet from road"),
                new Building(
                    Type:      "inline retail block, lower parapet",
                    Position:  "right of the anchor, continuing the run",
                    Stories:   1,
                    Materials: ["concrete block", "plate glass"],
                    Roof:      "flat",
                    Setback:   "120 feet from road")
            ],
            Driveways: ["main entrance apron", "service drive at the far end"],
            Parking:   "large surface lot in front of the run"),
        Environment: new Environment(
            Terrain:   "suburban flat",
            Utilities: ["lot light poles", "overhead power lines along the road frontage"],
            Trees:
            [
                new Tree(Position: "planter island mid-lot", Size: "small",  Type: "pear"),
                new Tree(Position: "lot perimeter",          Size: "medium", Type: "maple")
            ],
            Landscape: ["planter islands between parking rows", "grass verge along the road"]),
        ImmutableElements:
        [
            "stepped parapet line across the run",
            "freestanding pylon sign at the road",
            "continuous walkway canopy in front of the inline units"
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
        string[] requiredKeys = { "downtown_street", "gas_station", "strip_mall", "auto_repair", "corner_shop", "motel", "freestanding_shop", "default" };
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
            // 1975 (5 decades back): the small tree in gasScene lands in the
            // youngest bucket and the medium one just above it — unlike the old
            // absolute-rung ladder, the large tree does NOT clamp to the same
            // floor here (that flattening was exactly the bug being fixed).
            // Percentages here are post-GrowthDamping.
            if (!run[1975].Text.Contains("a young tree, only about 15% of its canopy in the base image, thin trunk"))
                errs.Add($"{label}/1975: missing small-tree young-canopy phrasing (15%)");
            if (!run[1975].Text.Contains("clearly smaller than in the base image — about 45% of its canopy there, thinner trunk"))
                errs.Add($"{label}/1975: missing medium-tree canopy phrasing (45%)");

            // 2005 (2 decades back): the mature (large) tree is barely down.
            if (!run[2005].Text.Contains("slightly smaller than in the base image — about 90% of its canopy there"))
                errs.Add($"{label}/2005: missing mature-tree mid-life phrasing (90%)");

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
                // Not every era need differ any more. A large tree changes about
                // 4% per decade once GrowthScale is applied, and percentages
                // round to the nearest 5%, so two adjacent eras legitimately land
                // on the same figure — a mature tree that barely moves in ten
                // years is the honest result, not a bug. What this still has to
                // catch is the original failure: an absolute ladder clamping
                // every era to one rung. So require most eras to differ and the
                // ends of the run to differ from each other.
                var distinct = labels.Distinct().Count();
                if (labels.Count > 0 && (distinct < labels.Count - 1 || labels[0] == labels[^1]))
                    errs.Add($"{label}: mature tree barely varies across the run ({distinct} distinct of {labels.Count}): {string.Join(" | ", labels)}");
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
            // Derelict eras carry no sidewalk rule: abandoned collapses to the
            // no-people line, and squatted places its handful of figures
            // explicitly, so the generic zone rule has nothing to govern.
            if (prompt.SceneCondition is not ("abandoned" or "squatted") && !prompt.Text.Contains(sidewalk))
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

                // people_activities are the customers and staff of a working
                // business — a clerk restocking a cooler, someone refuelling. At a
                // dead station they contradict the condition outright, and the
                // image model resolves the contradiction by reopening the place.
                // Nothing asserted this before, which is how a squatted 2025
                // shipped with a woman refuelling and a man wiping a windshield.
                if (derelict && sc.PeopleActivities is { Count: > 0 } acts)
                    foreach (var a in acts.Where(a => prompt.Text.Contains($"- {a}")))
                        errs.Add($"{label}/{year}: {prompt.SceneCondition} but still lists live-business activity '{a}'");
                if (derelict && prompt.Text.Contains("Clothing:"))
                    errs.Add($"{label}/{year}: {prompt.SceneCondition} but still specifies clothing");
                if (derelict && prompt.Text.Contains("no one refuels without a car present"))
                    errs.Add($"{label}/{year}: {prompt.SceneCondition} but still carries the refuelling rule");
                // people_mix survives a squatted era: an ordinary passer-by still
                // walks past a dead lot, they just have no business with it. Only
                // abandoned drops the line, since it has no people at all.
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

        // The finale now resolves the arc for every scene type that supports
        // conditions, so a rank drop is legal on the last era regardless of type.
        void CheckMonotonic(Dictionary<int, Prompt> run, string label, bool isGasStation)
        {
            var prevRank = -1;
            foreach (var year in Years)
            {
                if (!run.TryGetValue(year, out var prompt)) continue;
                var rank = ConditionRank.GetValueOrDefault(prompt.SceneCondition, 0);
                var isFinale = year == Years[^1];
                if (prevRank >= 0 && rank < prevRank && !isFinale)
                    errs.Add($"{label}/{year}: condition rank dropped from {prevRank} to {rank} ('{prompt.SceneCondition}') outside the finale exception");
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

        // "restored" is a finale resolution for every condition-supporting type,
        // so it is legal only on the last era — for gas stations and the rest alike.
        foreach (var (run, label) in new[]
        {
            (gasRun1, "gas/run1"), (gasRun2, "gas/run2"),
            (dtRun1, "downtown/run1"), (dtRun2, "downtown/run2"),
            (smRun1, "strip/run1"),    (smRun2, "strip/run2"), (arRun1, "auto/run1"), (arRun2, "auto/run2")
        })
            foreach (var (year, prompt) in run)
                if (prompt.SceneCondition == "restored" && year != Years[^1])
                    errs.Add($"{label}/{year}: 'restored' outside the final era");

        f.Add(("C23", "default/unknown scenes always thriving; rank monotonic per run (the final era may resolve the arc for any condition-supporting type); abandoned/declining/squatted counts honored for gas_station, downtown_street and strip_mall; 'squatted' only on a gas_station's final era; 'restored' only on a final era",
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
        // Raised from 5 once the thin pools were filled out: five bodies means a
        // scene type repeats itself within five weeks, and three of the types
        // already carried 30. Anything below this is a pool that was started and
        // never finished.
        const int MinBodies = 15;
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

            // A duplicated body is pool size on paper only: the rotation still
            // visits it twice and the feed repeats itself sooner than the count
            // suggests.
            foreach (var dupe in bodies.GroupBy(b => b, StringComparer.OrdinalIgnoreCase)
                                       .Where(g => g.Count() > 1))
                errs.Add($"captions/{name}.txt: duplicated body — \"{dupe.Key.Split('\n')[0]}\"");

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
        // "wide patch" is the ghost form: the sign is gone, so the only thing
        // that still identifies the tenant is how much wall it left behind.
        // RadioShack's ghost says "small patch" and is allowed here.
        string[] markers = { "Blockbuster", "torn-ticket", "wide patch of less-faded wall" };

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
        // A derelict corner shop has no live sign, which is exactly what the
        // default (no name) carries — the same state ResolveCornerShop returns
        // for these conditions.
        var cornerShop = default(GenerationContext.CornerShopSign);
        var rng = new Random(1); // unused by the derelict branch — no sampling happens there
        // Derelict retail, not a highway: no background buildings to name.
        // Same for a derelict motel: the stripped-pylon state is what the resolver
        // returns for these conditions, and it is what the default carries.
        var motelSign = new GenerationContext.MotelSign(GenerationContext.MotelSignKind.DeadBoard, null);
        var args = new object?[] { era, content, sceneType, condition, gasSign, cornerShop, motelSign, false, rng, context };
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

    // Chained eras are generated on top of the previous year's finished image, so
    // the uploaded photo already has that year's people and traffic in it. If the
    // prompt does not say to clear them first, figures accumulate down the chain
    // and the last year ends up with six eras' worth of people in one frame.
    private static async Task DoC43(
        IPromptService promptService,
        Dictionary<int, EraProfile> eras,
        SceneDna gasScene,
        List<(string, string, bool?, string)> f)
    {
        var errs = new List<string>();
        const string clearing = "remove EVERY person, vehicle and bicycle already in it";
        const string emptyClaim = "The street in the source\nis empty";

        foreach (var chained in new[] { false, true })
        {
            var ctx = new GenerationContext
            {
                Random = new Random(42), TotalEras = Years.Length, Years = Years,
                ChainedFromPreviousEra = chained
            };
            foreach (var year in Years)
            {
                var prompt = await promptService.BuildAsync(gasScene, eras[year], ctx);
                var label  = $"chained={chained}/{year}";

                if (prompt.Text.Contains("{BASE_NOTE}"))
                    errs.Add($"{label}: unsubstituted {{BASE_NOTE}}");

                if (chained)
                {
                    if (!prompt.Text.Contains(clearing))
                        errs.Add($"{label}: chained prompt does not clear the previous era's people and vehicles");
                    // Claiming the source is empty is false once chained, and the
                    // model resolves the contradiction by keeping what it sees.
                    if (prompt.Text.Contains(emptyClaim))
                        errs.Add($"{label}: chained prompt still claims the source street is empty");
                }
                else
                {
                    if (!prompt.Text.Contains(emptyClaim))
                        errs.Add($"{label}: unchained prompt lost the empty-source wording");
                    if (prompt.Text.Contains(clearing))
                        errs.Add($"{label}: unchained prompt carries the chained clearing instruction");
                }
            }
        }

        f.Add(("C43", "Chained eras are told to clear the previous year's people and vehicles; unchained eras keep the empty-source wording",
            errs.Count == 0, errs.Count == 0 ? "Base note matches the chaining mode in every era" : Join(errs)));
    }

    // Weather carries the arc: living eras look like a day worth remembering,
    // and only the dead ones go grey. A run that is overcast throughout reads as
    // apocalyptic rather than nostalgic, and the loss at the end lands against
    // nothing.
    private static void DoC44(
        Dictionary<int, Prompt> gasRun1, Dictionary<int, Prompt> gasRun2,
        Dictionary<int, Prompt> dtRun1,  Dictionary<int, Prompt> dtRun2,
        Dictionary<int, Prompt> smRun1,  Dictionary<int, Prompt> smRun2,
        Dictionary<int, Prompt> arRun1,  Dictionary<int, Prompt> arRun2,
        Prompt unknownPrompt,
        Dictionary<int, EraProfile> eras,
        List<(string, string, bool?, string)> f)
    {
        var errs = new List<string>();

        foreach (var (year, prompt, label) in AllPrompts(gasRun1, gasRun2, dtRun1, dtRun2, smRun1, smRun2, arRun1, arRun2, unknownPrompt))
        {
            var light = prompt.Text.Split('\n').FirstOrDefault(l => l.StartsWith("Light:"));
            if (light is null)
            {
                errs.Add($"{label}/{year}: no Light: line");
                continue;
            }

            var derelict = prompt.SceneCondition is "abandoned" or "squatted";
            var grey     = light.Contains("grey overcast");

            if (derelict && !grey)
                errs.Add($"{label}/{year}: {prompt.SceneCondition} but light is not the subdued variant");
            if (!derelict && grey)
                errs.Add($"{label}/{year}: {prompt.SceneCondition} (a living era) is shot under grey overcast");

            // Never a mood piece: the frame has to stay a legible daytime record.
            foreach (var banned in new[] { "fog", "rain", "storm", "night", "darkness" })
                if (light.Contains(banned, StringComparison.OrdinalIgnoreCase)
                    && !light.Contains($"no {banned}", StringComparison.OrdinalIgnoreCase))
                    errs.Add($"{label}/{year}: light line calls for '{banned}'");

            // A monochrome era must not be handed colour wording.
            if (eras.TryGetValue(year, out var era)
                && era.Photography.ColorMode == "black_and_white"
                && (light.Contains("colour", StringComparison.OrdinalIgnoreCase)
                    || light.Contains("warm", StringComparison.OrdinalIgnoreCase)))
                errs.Add($"{label}/{year}: black-and-white era has colour wording in its light line");
        }

        f.Add(("C44", "Every prompt sets its light from the condition: living eras get open daylight, only derelict eras go grey, and no prompt asks for fog, rain or night",
            errs.Count == 0, errs.Count == 0 ? "Light matches condition in every prompt" : Join(errs)));
    }

    // Condition variety across seeds. The arc used to collapse: "declining"
    // forced the next era that offered "abandoned" to take it, so 2015 came out
    // derelict in ~86% of runs and every video told the same story at the same
    // moment. This measures the distribution rather than one run, because a
    // single fixture can look fine while the policy behind it is degenerate.
    private static void DoC45(
        Dictionary<int, EraProfile> eras,
        ILogger logger,
        List<(string, string, bool?, string)> f)
    {
        const int seeds = 500;
        const double maxAbandoned2015 = 0.35;
        const double minEverDeclines  = 0.70;
        const int    minTrajectories  = 20;

        var errs = new List<string>();
        var summary = new List<string>();

        foreach (var sceneType in new[] { "downtown_street", "strip_mall", "auto_repair", "gas_station" })
        {
            var abandoned2015 = 0;
            var everDeclined  = 0;
            var trajectories  = new HashSet<string>();

            for (var seed = 0; seed < seeds; seed++)
            {
                var ctx  = new GenerationContext
                    { Random = new Random(seed), TotalEras = Years.Length, Years = Years };
                var path = new List<string>();
                foreach (var year in Years)
                {
                    ctx.BeginEra();
                    path.Add(ctx.PickSceneCondition(eras[year].AllowedSceneConditions, sceneType, year));
                }

                if (path[Array.IndexOf(Years, 2015)] == "abandoned") abandoned2015++;
                if (path.Any(c => c is "declining" or "abandoned" or "squatted")) everDeclined++;
                trajectories.Add(string.Join(">", path));
            }

            var rate2015 = abandoned2015 / (double)seeds;
            var declines = everDeclined  / (double)seeds;
            summary.Add($"{sceneType}: 2015 abandoned {rate2015:P0}, ever declines {declines:P0}, {trajectories.Count} trajectories");

            if (rate2015 > maxAbandoned2015)
                errs.Add($"{sceneType}: 2015 is abandoned in {rate2015:P0} of runs (max {maxAbandoned2015:P0}) — the arc collapses to one story");
            // The opposite failure: tuning decay away entirely leaves nothing to
            // lose, and these videos exist for the loss.
            if (declines < minEverDeclines)
                errs.Add($"{sceneType}: only {declines:P0} of runs ever decline (min {minEverDeclines:P0})");
            if (trajectories.Count < minTrajectories)
                errs.Add($"{sceneType}: only {trajectories.Count} distinct trajectories across {seeds} seeds");
        }

        logger.LogInformation("[Smoke] C45 condition spread: {Summary}", string.Join(" | ", summary));

        f.Add(("C45", $"Across {seeds} seeds no scene type abandons 2015 more than {maxAbandoned2015:P0} of the time, at least {minEverDeclines:P0} of runs still decline, and trajectories stay varied",
            errs.Count == 0, errs.Count == 0 ? string.Join(" | ", summary) : Join(errs)));
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

    // Composition and Distinctive are both optional SceneDna fields (vision may
    // not always fill them), so no existing fixture sets them — this builds its
    // own scenes via `with` to exercise BuildPreserveBlock's two new lines.
    // BuildPreserveBlock is currently only reachable through BuildBaseAsync — the
    // era path (BuildAsync) substitutes the fixed ShortPreserveBlock while C9's
    // per-era geometry block stays parked (see the comment above ShortPreserveBlock).
    private static async Task DoC48(
        IPromptService promptService,
        SceneDna gasScene,
        List<(string, string, bool?, string)> f)
    {
        var errs = new List<string>();

        var distinctivePhrases = new[]
        {
            "canopy cantilevers far past its two slender columns with no support at the outer corner",
            "eight pump islands in one long row"
        };
        var distinctiveScene = gasScene with { Distinctive = distinctivePhrases };
        var distinctiveText  = await promptService.BuildBaseAsync(distinctiveScene, Years[0]);
        foreach (var phrase in distinctivePhrases)
            if (!distinctiveText.Contains(phrase))
                errs.Add($"distinctive phrase missing verbatim from prompt: '{phrase}'");

        var composition = new Composition(SubjectDistance: "close", FrameShare: "dominant", Horizon: "low");
        var compositionScene = gasScene with { Composition = composition };
        var compositionText  = await promptService.BuildBaseAsync(compositionScene, Years[0]);
        var framingLine = $"- framing: subject at {composition.SubjectDistance} range, filling a {composition.FrameShare} share of the frame, horizon {composition.Horizon}";
        if (!compositionText.Contains(framingLine))
            errs.Add($"framing line missing from prompt: '{framingLine}'");

        f.Add(("C48", "Distinctive phrases appear verbatim in the synthetic base prompt; a set Composition produces the framing line",
            errs.Count == 0, errs.Count == 0 ? "Distinctive and Composition both render correctly" : Join(errs)));
    }

    // Trajectory shape, swept over many seeds rather than the two fixture runs:
    // (1) condition worsens one step at a time — no era pair jumps rank 0 -> 2;
    // (2) a run that ever decayed resolves its arc in the finale, so it never
    // ends on "abandoned". Drives PickSceneCondition directly with each era's
    // real allowed_scene_conditions, exercising the same call order
    // PromptService uses (BeginEra then PickSceneCondition, once per era).
    private static void DoC47(
        Dictionary<int, EraProfile> eras,
        List<(string, string, bool?, string)> f)
    {
        var errs = new List<string>();
        var sceneTypes = new[] { "gas_station", "downtown_street", "strip_mall", "auto_repair" };

        foreach (var sceneType in sceneTypes)
        {
            for (var seed = 1; seed <= 40; seed++)
            {
                var ctx = new GenerationContext { Random = new Random(seed), TotalEras = Years.Length };
                var trajectory = new List<(int Year, string Condition, int Rank)>();

                foreach (var year in Years)
                {
                    ctx.BeginEra();
                    var condition = ctx.PickSceneCondition(eras[year].AllowedSceneConditions, sceneType, year);
                    trajectory.Add((year, condition, ConditionRank.GetValueOrDefault(condition, 0)));
                }

                for (var i = 1; i < trajectory.Count; i++)
                {
                    var prev = trajectory[i - 1];
                    var cur  = trajectory[i];
                    if (prev.Rank == 0 && cur.Rank == 2)
                        errs.Add($"{sceneType} seed={seed}: rank skipped 0 -> 2 between {prev.Year} ('{prev.Condition}') and {cur.Year} ('{cur.Condition}')");
                }

                // "Ever decayed" is judged before the finale: the last era is the
                // resolution, so it is exactly what must not land on 'abandoned'.
                var decayedBeforeFinale = trajectory.Take(trajectory.Count - 1).Any(t => t.Rank >= 1);
                var final = trajectory[^1];
                if (decayedBeforeFinale && final.Condition == "abandoned")
                    errs.Add($"{sceneType} seed={seed}: run decayed then ended on 'abandoned' ({final.Year})");
            }
        }

        f.Add(("C47", "Condition rank never skips 0 -> 2 between consecutive eras; a run that ever decayed never ends on 'abandoned'",
            errs.Count == 0, errs.Count == 0 ? "Trajectory steps one rank at a time and resolves across 40 seeds x 4 scene types" : Join(errs)));
    }

    // The CONDITION line is what carries the decline arc into the image. Every
    // scene type that picks a condition must print it — auto_repair once picked
    // one and silently dropped the line, because the count logic and the
    // emit-side list were two separate literals. Both now ask SupportsCondition,
    // and this check fails if they ever diverge again. mall is the counter-case:
    // it never picks a condition, so it must never print the line.
    private static async Task DoC46(
        IPromptService promptService,
        Dictionary<int, EraProfile> eras,
        Dictionary<int, Prompt> gasRun1, Dictionary<int, Prompt> gasRun2,
        Dictionary<int, Prompt> dtRun1,  Dictionary<int, Prompt> dtRun2,
        Dictionary<int, Prompt> smRun1,  Dictionary<int, Prompt> smRun2,
        Dictionary<int, Prompt> arRun1,  Dictionary<int, Prompt> arRun2,
        List<(string, string, bool?, string)> f)
    {
        var errs = new List<string>();
        const string conditionLine = "CONDITION: ";

        // These four runs are exactly the scene types SupportsCondition covers.
        foreach (var (year, prompt, label) in AllPrompts(gasRun1, gasRun2, dtRun1, dtRun2, smRun1, smRun2, arRun1, arRun2))
            if (!prompt.Text.Contains(conditionLine))
                errs.Add($"{label}/{year}: condition-bearing scene type emitted no '{conditionLine}' line");

        var mallScene = MakeMallScene();
        foreach (var year in new[] { 1975, 1995 })
        {
            var ctx    = new GenerationContext { Random = new Random(42), TotalEras = 1 };
            var prompt = await promptService.BuildAsync(mallScene, eras[year], ctx);
            if (prompt.Text.Contains(conditionLine))
                errs.Add($"mall/{year}: emitted a '{conditionLine}' line for a scene type that has no condition arc");
        }

        f.Add(("C46", "Every condition-bearing scene type prints its CONDITION line; mall (no condition arc) prints none",
            errs.Count == 0, errs.Count == 0 ? "CONDITION line present for gas_station/downtown_street/strip_mall/auto_repair, absent for mall" : Join(errs)));
    }

    // Seeds swept by the condition-arc checks below. One seed proves nothing
    // about a randomised arc — these walk enough of them that a rule which only
    // usually holds shows up as a failure.
    private static readonly int[] ArcSeeds = Enumerable.Range(1, 40).ToArray();

    // An era rewritten to offer exactly one condition, used to drive the arc to a
    // known rank instead of hoping a seed gets there. "abandoned" is mapped to
    // "squatted" inside PickSceneCondition, so this is also how a rank-2 run is
    // reached now that abandonment is disabled.
    private static EraProfile Forcing(EraProfile era, params string[] conditions) =>
        era with { AllowedSceneConditions = conditions };

    // "abandoned" is disabled: a sealed empty ruin is dead air on screen, so no
    // era of any run may land on it however the dice fall.
    private static async Task DoC49(
        IPromptService promptService,
        Dictionary<int, EraProfile> eras,
        SceneDna downtownScene, SceneDna stripMallScene,
        List<(string, string, bool?, string)> f)
    {
        var errs = new List<string>();

        foreach (var (scene, label) in new[] { (downtownScene, "downtown_street"), (stripMallScene, "strip_mall") })
            foreach (var seed in ArcSeeds)
            {
                var run = await BuildRun(promptService, scene, eras, seed, Years);
                foreach (var (year, prompt) in run)
                {
                    if (prompt.SceneCondition == "abandoned")
                        errs.Add($"{label}/seed {seed}/{year}: condition 'abandoned'");
                    if (prompt.Text.Contains("CONDITION: abandoned", StringComparison.Ordinal))
                        errs.Add($"{label}/seed {seed}/{year}: prompt carries a CONDITION: abandoned line");
                }
            }

        f.Add(("C49", $"No era of any run reaches 'abandoned' ({ArcSeeds.Length} seeds x 6 eras x 2 retail scene types)",
            errs.Count == 0, errs.Count == 0
                ? $"{ArcSeeds.Length * Years.Length * 2} era conditions sampled, none abandoned"
                : Join(errs.Take(5))));
    }

    // Squatted retail is a half-dead row, not a sealed one: cheap survivors still
    // trading, dereliction carried at ground level. The fully-closed wording is
    // what this state exists to replace, so it must not appear.
    private static async Task DoC50(
        IPromptService promptService,
        Dictionary<int, EraProfile> eras,
        SceneDna downtownScene, SceneDna stripMallScene,
        List<(string, string, bool?, string)> f)
    {
        var errs = new List<string>();
        const string fullyClosed = "every storefront closed and dark";

        // Structural: the squatted pool is the heavy pool plus the ground
        // details, while abandoned keeps the sealed pool untouched.
        foreach (var sceneType in new[] { "downtown_street", "strip_mall" })
        {
            var squattedPool = PromptService.DecayPoolFor(sceneType, "squatted") ?? [];
            foreach (var ground in PromptService.SquattedGroundDetails)
                if (!squattedPool.Contains(ground))
                    errs.Add($"{sceneType}: squatted decay pool is missing ground detail '{ground}'");

            var abandonedPool = PromptService.DecayPoolFor(sceneType, "abandoned") ?? [];
            if (PromptService.SquattedGroundDetails.Any(abandonedPool.Contains))
                errs.Add($"{sceneType}: abandoned decay pool picked up squatted ground details");
        }

        // Prompt level: every squatted retail prompt carries a surviving tenant
        // and never the fully-closed line; ground details are sampled, so they
        // are asserted across the sweep rather than per prompt.
        var groundSeen = false;
        foreach (var (scene, label) in new[] { (downtownScene, "downtown_street"), (stripMallScene, "strip_mall") })
            foreach (var seed in ArcSeeds)
            {
                var ctx = new GenerationContext { Random = new Random(seed), TotalEras = 2 };
                var prompt = await promptService.BuildAsync(
                    scene, Forcing(eras[2015], "abandoned"), ctx);

                if (prompt.SceneCondition != "squatted")
                {
                    errs.Add($"{label}/seed {seed}: forced era gave '{prompt.SceneCondition}', expected squatted");
                    continue;
                }

                if (!PromptService.PoorTenantBusinesses.Any(t => prompt.Text.Contains(t, StringComparison.Ordinal)))
                    errs.Add($"{label}/seed {seed}: squatted prompt names no surviving tenant");
                if (prompt.Text.Contains(fullyClosed, StringComparison.Ordinal))
                    errs.Add($"{label}/seed {seed}: squatted prompt still says '{fullyClosed}'");
                if (prompt.Text.Length > MaxPromptChars)
                    errs.Add($"{label}/seed {seed}: squatted prompt is {prompt.Text.Length} chars (max {MaxPromptChars})");

                groundSeen |= PromptService.SquattedGroundDetails.Any(g => prompt.Text.Contains(g, StringComparison.Ordinal));
            }

        if (!groundSeen)
            errs.Add($"no squatted prompt across {ArcSeeds.Length} seeds sampled a ground detail");

        f.Add(("C50", "Squatted retail prompts carry a surviving tenant and ground-level decay, never the fully-closed wording",
            errs.Count == 0, errs.Count == 0
                ? "survivors present, ground details reachable, fully-closed line absent"
                : Join(errs.Take(5))));
    }

    // Once a retail row has fallen to rank 2 the finale holds it there. A place
    // does not go derelict and reopen inside one era, and showing that is the one
    // beat in the arc that reads as false.
    private static async Task DoC51(
        IPromptService promptService,
        Dictionary<int, EraProfile> eras,
        SceneDna downtownScene, SceneDna stripMallScene,
        List<(string, string, bool?, string)> f)
    {
        var errs = new List<string>();

        foreach (var (scene, label) in new[] { (downtownScene, "downtown_street"), (stripMallScene, "strip_mall") })
            foreach (var seed in ArcSeeds)
            {
                var ctx = new GenerationContext { Random = new Random(seed), TotalEras = 2 };

                var preFinale = await promptService.BuildAsync(
                    scene, Forcing(eras[2015], "abandoned"), ctx);
                if (preFinale.SceneCondition != "squatted")
                {
                    errs.Add($"{label}/seed {seed}: setup era gave '{preFinale.SceneCondition}', expected rank 2");
                    continue;
                }

                var finale = await promptService.BuildAsync(scene, eras[2025], ctx);
                if (finale.SceneCondition != "squatted")
                    errs.Add($"{label}/seed {seed}: finale resurrected to '{finale.SceneCondition}'");
            }

        f.Add(("C51", "A run that reached rank 2 before the finale ends squatted — never restored or declining",
            errs.Count == 0, errs.Count == 0
                ? $"{ArcSeeds.Length * 2} rank-2 runs all held their finale at squatted"
                : Join(errs.Take(5))));
    }

    // "restored" means the same shell taken back into use, not a gut renovation:
    // a rebuilt-looking building stops being the building the run has followed.
    private static async Task DoC52(
        IPromptService promptService,
        Dictionary<int, EraProfile> eras,
        SceneDna downtownScene, SceneDna stripMallScene,
        List<(string, string, bool?, string)> f)
    {
        var errs = new List<string>();
        var restoredSeen = 0;

        foreach (var (scene, label) in new[] { (downtownScene, "downtown_street"), (stripMallScene, "strip_mall") })
            foreach (var seed in ArcSeeds)
            {
                var ctx = new GenerationContext { Random = new Random(seed), TotalEras = 2 };

                // Rank 1 only, so the finale is free to choose restored.
                await promptService.BuildAsync(scene, Forcing(eras[2005], "declining"), ctx);
                var finale = await promptService.BuildAsync(scene, eras[2025], ctx);
                if (finale.SceneCondition != "restored")
                    continue;

                restoredSeen++;
                var line = finale.Text.Split('\n')
                    .FirstOrDefault(l => l.StartsWith("CONDITION: restored", StringComparison.Ordinal));
                if (line is null)
                {
                    errs.Add($"{label}/seed {seed}: restored prompt has no CONDITION line");
                    continue;
                }
                if (!line.Contains("reoccupied", StringComparison.Ordinal))
                    errs.Add($"{label}/seed {seed}: restored descriptor does not say 'reoccupied': {line}");
                if (line.Contains("renovated appearance", StringComparison.Ordinal))
                    errs.Add($"{label}/seed {seed}: restored descriptor still says 'renovated appearance'");
                if (finale.Text.Length > MaxPromptChars)
                    errs.Add($"{label}/seed {seed}: restored prompt is {finale.Text.Length} chars (max {MaxPromptChars})");
            }

        if (restoredSeen == 0)
            errs.Add($"no finale across {ArcSeeds.Length} seeds x 2 scene types resolved to 'restored' — the branch is unreachable");

        f.Add(("C52", "The 'restored' descriptor reads as reoccupation of the same shell, not a renovation",
            errs.Count == 0, errs.Count == 0
                ? $"{restoredSeen} restored finales, all reoccupation wording"
                : Join(errs.Take(5))));
    }

    // The squatted split has to reach the people and vehicle blocks too. A row
    // whose open units are "clearly trading" alongside "nothing here is open" and
    // an empty lot is the contradiction the whole state was reworked to remove —
    // and the image model resolves such a conflict by picking one side.
    private static async Task DoC53(
        IPromptService promptService,
        Dictionary<int, EraProfile> eras,
        SceneDna downtownScene, SceneDna stripMallScene, SceneDna gasScene,
        List<(string, string, bool?, string)> f)
    {
        var errs = new List<string>();
        const string deadLine = "No staff, no customers, nobody working — nothing here is open.";
        const string noVehicles = "NO vehicles anywhere";

        foreach (var (scene, label) in new[] { (downtownScene, "downtown_street"), (stripMallScene, "strip_mall") })
            foreach (var seed in ArcSeeds)
            {
                var ctx = new GenerationContext { Random = new Random(seed), TotalEras = 2 };
                var p = await promptService.BuildAsync(scene, Forcing(eras[2015], "abandoned"), ctx);
                if (p.SceneCondition != "squatted") continue;

                if (p.Text.Contains(deadLine, StringComparison.Ordinal))
                    errs.Add($"{label}/seed {seed}: trading row still carries the dead-forecourt people line");
                if (p.Text.Contains(noVehicles, StringComparison.Ordinal))
                    errs.Add($"{label}/seed {seed}: trading row has an empty lot");
                if (p.SelectedVehicles.Count is < 2 or > 3)
                    errs.Add($"{label}/seed {seed}: {p.SelectedVehicles.Count} vehicles, expected 2-3");

                var people = PeopleTotal(p.Text);
                if (people is null || people < 4 || people > 6)
                    errs.Add($"{label}/seed {seed}: people total {people?.ToString() ?? "absent"}, expected 4-6");

                // "of these, N ..." plus "the rest ..." already partitions the
                // total; a third figure bullet describes people the count has no
                // room for.
                var figureLines = PeopleSection(p.Text)
                    .Split('\n')
                    .Count(l => l.StartsWith("- ", StringComparison.Ordinal));
                if (figureLines != 2)
                    errs.Add($"{label}/seed {seed}: {figureLines} figure lines in the people block, expected 2");
            }

        // The forecourt keeps the dead treatment, and its enumerated figures must
        // sum to the exact total it states — a plural people_mix entry there
        // describes more people than the count allows.
        foreach (var seed in ArcSeeds)
        {
            var ctx = new GenerationContext { Random = new Random(seed), TotalEras = 2 };
            var p = await promptService.BuildAsync(gasScene, Forcing(eras[2015], "abandoned"), ctx);
            if (p.SceneCondition != "squatted") continue;

            if (!p.Text.Contains(deadLine, StringComparison.Ordinal))
                errs.Add($"gas_station/seed {seed}: squatted forecourt lost its dead-forecourt people line");

            var passerBy = p.Text.Split('\n')
                .FirstOrDefault(l => l.Contains("passing by along the far edge", StringComparison.Ordinal));
            if (passerBy is null)
                errs.Add($"gas_station/seed {seed}: no passer-by line");
            else
            {
                var figure = passerBy.TrimStart('-', ' ');
                if (!PromptService.IsSinglePersonForTests(figure))
                    errs.Add($"gas_station/seed {seed}: passer-by describes more than one person: {figure}");
            }
        }

        f.Add(("C53", "Squatted retail gets trading-row people and vehicles; the squatted forecourt stays dead and its figures sum to its stated total",
            errs.Count == 0, errs.Count == 0
                ? "retail rows populated and parked, forecourts dead with single-figure passers-by"
                : Join(errs.Take(5))));
    }

    private static string TreesSection(string promptText)
    {
        var i = promptText.IndexOf("TREES\n", StringComparison.Ordinal);
        if (i < 0) return "";
        var j = promptText.IndexOf("\n\n", i, StringComparison.Ordinal);
        return j < 0 ? promptText[i..] : promptText[i..j];
    }

    private static string PeopleSection(string promptText)
    {
        var i = promptText.IndexOf("PEOPLE\n", StringComparison.Ordinal);
        if (i < 0) return "";
        var j = promptText.IndexOf("\n\n", i, StringComparison.Ordinal);
        return j < 0 ? promptText[i..] : promptText[i..j];
    }

    // "EXACTLY N people TOTAL" — the count the prompt commits to.
    private static int? PeopleTotal(string promptText)
    {
        var m = System.Text.RegularExpressions.Regex.Match(promptText, @"EXACTLY (\d+) people TOTAL");
        return m.Success ? int.Parse(m.Groups[1].Value) : null;
    }

    // Chained eras edit the PREVIOUS era's image, not the shared base, so a tree
    // instruction phrased as a fraction of the base is read against the wrong
    // picture and compounds: each step cuts the canopy again instead of growing
    // it. Walking forward in time, every era after the first must ask for growth
    // against what it was actually given.
    private static async Task DoC54(
        IPromptService promptService,
        Dictionary<int, EraProfile> eras,
        SceneDna stripMallScene,
        List<(string, string, bool?, string)> f)
    {
        var errs = new List<string>();

        var ctx = new GenerationContext
        {
            Random = new Random(42), TotalEras = Years.Length, Years = Years,
            ChainedFromPreviousEra = true
        };

        // One decade of growth, as a percentage of the image being edited. Depends
        // only on the recorded size, so it is the same at every step of the run.
        // Each rate carries PromptService.GrowthDamping, then rounds to 5%.
        var expected = new Dictionary<string, string>
        {
            ["small"]  = "145%",   // 0.95 / 0.62, then GrowthScale
            ["medium"] = "115%",   // 0.95 / 0.78, then GrowthScale
            ["large"]  = "105%",   // 0.95 / 0.90, then GrowthScale
        };

        foreach (var year in Years)
        {
            var prompt = await promptService.BuildAsync(stripMallScene, eras[year], ctx);
            var isFirst = year == Years[0];

            // Scoped to the section: "uploaded photo" also appears in the chained
            // base note at the top of every prompt.
            var trees = TreesSection(prompt.Text);

            if (isFirst)
            {
                // Era one really is edited from the base, so it keeps the
                // shrink-from-base wording.
                if (!trees.Contains("of its canopy there", StringComparison.Ordinal))
                    errs.Add($"{year}: first chained era has no tree sizing at all");
                if (trees.Contains("uploaded photo", StringComparison.Ordinal))
                    errs.Add($"{year}: first chained era compares to the uploaded photo, but it is edited from the base");
                continue;
            }

            if (trees.Length == 0)
            {
                errs.Add($"{year}: chained era has no TREES section — its trees stay at the previous era's size");
                continue;
            }
            if (trees.Contains("in the base image", StringComparison.Ordinal))
                errs.Add($"{year}: chained era still sizes trees against the base image");
            if (!trees.Contains("larger than in the uploaded photo", StringComparison.Ordinal))
                errs.Add($"{year}: chained era does not ask for growth against the uploaded photo");
            if (trees.Contains("smaller than in the uploaded photo", StringComparison.Ordinal))
                errs.Add($"{year}: chained era shrinks trees while moving forward in time");

            foreach (var tree in stripMallScene.Environment.Trees)
            {
                var line = trees.Split('\n')
                    .FirstOrDefault(l => l.StartsWith($"- {tree.Type} tree at {tree.Position}:", StringComparison.Ordinal));
                if (line is null)
                {
                    errs.Add($"{year}: no tree line for {tree.Type}");
                    continue;
                }
                if (!line.Contains(expected[tree.Size], StringComparison.Ordinal))
                    errs.Add($"{year}: {tree.Size} tree should grow to {expected[tree.Size]} per decade, got: {line.Trim()}");
            }
        }

        f.Add(("C54", "Chained eras size trees as growth against the uploaded previous era, never as a fraction of the base",
            errs.Count == 0, errs.Count == 0
                ? "every era after the first grows its trees by the per-decade ratio for its size"
                : Join(errs.Take(5))));
    }

    // A batch run normally finishes through 'collect', not inside Pipeline, so
    // the caption tail has to work from nothing but the run folder. Everything
    // it needs must survive the process that built the prompts: the arc facts
    // live in the GenerationContext and cannot be recomputed later.
    private static async Task DoC55(
        IDataService dataService,
        SceneDna gasScene,
        List<(string, string, bool?, string)> f)
    {
        var errs = new List<string>();
        var runRoot = Path.Combine(Path.GetTempPath(), "loy-caption-tail-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(runRoot);

        try
        {
            var narrative = new SceneNarrative(
                FirstYear: 1975, LastYear: 2025, FinalCondition: "squatted",
                FirstBrand: "Texaco", LastBrand: "Sinclair", RebrandOccurred: true);

            await CaptionRunner.SaveNarrativeAsync(runRoot, narrative);
            var readBack = await CaptionRunner.ReadNarrativeAsync(runRoot);
            if (readBack != narrative)
                errs.Add($"narrative.json did not round-trip: wrote {narrative}, read {readBack?.ToString() ?? "null"}");

            // scene.json is written by RunService with the same Web casing; the
            // reader has to agree with it or collect silently skips captioning.
            await File.WriteAllTextAsync(
                Path.Combine(runRoot, "scene.json"),
                JsonSerializer.Serialize(gasScene, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
            var scene = await CaptionRunner.ReadSceneAsync(runRoot);
            if (scene is null)
                errs.Add("scene.json did not deserialize");
            else if (scene.SceneType != gasScene.SceneType)
                errs.Add($"scene.json round-tripped to sceneType '{scene.SceneType}', expected '{gasScene.SceneType}'");

            if (errs.Count == 0)
            {
                var captions = new CaptionService(
                    dataService, Microsoft.Extensions.Logging.Abstractions.NullLogger<CaptionService>.Instance);
                var written = await CaptionRunner.WriteAsync(
                    captions, scene!, narrative, runRoot,
                    Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance);

                var captionPath = Path.Combine(runRoot, "caption.txt");
                if (!written || !File.Exists(captionPath))
                    errs.Add("caption.txt was not written from run-folder state alone");
                else
                {
                    var text = await File.ReadAllTextAsync(captionPath);
                    if (text.Contains('{') || text.Contains('}'))
                        errs.Add("caption.txt still contains an unsubstituted placeholder");
                    if (!text.Contains("1975", StringComparison.Ordinal) || !text.Contains("2025", StringComparison.Ordinal))
                        errs.Add("caption.txt does not carry the run's first and last year");
                    if (!text.Contains('#'))
                        errs.Add("caption.txt has no hashtags appended");
                }
            }
        }
        finally
        {
            try { Directory.Delete(runRoot, recursive: true); } catch { /* temp dir, best effort */ }
        }

        f.Add(("C55", "The caption tail runs from run-folder state alone, so a resumed batch run is captioned too",
            errs.Count == 0, errs.Count == 0
                ? "narrative.json and scene.json round-trip; caption.txt written with years and hashtags"
                : Join(errs)));
    }

    // Downtown and strip-mall utilities go underground from 2015 on, and every
    // image the era is handed still shows poles: the shared base is built in the
    // first era, and a chained era edits the previous decade's frame. Listing
    // "conduits below grade" under utilities does not take a pole out of the
    // picture on its own — without the explicit removal line the wires simply
    // carry through to the newest frames. The other scene types keep theirs: a
    // forecourt or a repair yard on the edge of town never got undergrounded.
    private static void DoC56(
        Dictionary<int, Prompt> dtRun1, Dictionary<int, Prompt> dtRun2,
        Dictionary<int, Prompt> smRun1, Dictionary<int, Prompt> smRun2,
        Dictionary<int, Prompt> gasRun1, Dictionary<int, Prompt> arRun1,
        List<(string, string, bool?, string)> f)
    {
        var errs = new List<string>();
        // Matched on the instruction, not on a description of the end state:
        // the utilities pool already says there are no poles, and only this line
        // acts on the poles that are in the picture being edited.
        const string removal = "are gone in this era: remove every";
        int[] undergroundYears = { 2015, 2025 };

        void CheckBuried(Dictionary<int, Prompt> run, string label)
        {
            foreach (var (year, prompt) in run)
            {
                var buried = undergroundYears.Contains(year);
                var hasRemoval = prompt.Text.Contains(removal, StringComparison.Ordinal);

                if (buried && !hasRemoval)
                    errs.Add($"{label}/{year}: utilities are undergrounded but the prompt never asks for the poles to go");
                if (!buried && hasRemoval)
                    errs.Add($"{label}/{year}: pre-undergrounding era already removes the poles");
                if (buried && prompt.Text.Contains("overhead power lines", StringComparison.OrdinalIgnoreCase))
                    errs.Add($"{label}/{year}: still asks for overhead power lines");
            }
        }

        CheckBuried(dtRun1, "downtown_street/run1");
        CheckBuried(dtRun2, "downtown_street/run2");
        CheckBuried(smRun1, "strip_mall/run1");
        CheckBuried(smRun2, "strip_mall/run2");

        foreach (var (run, label) in new[] { (gasRun1, "gas_station/run1"), (arRun1, "auto_repair/run1") })
            foreach (var year in undergroundYears)
                if (run[year].Text.Contains(removal, StringComparison.Ordinal))
                    errs.Add($"{label}/{year}: scene type has no undergrounding, but the poles are removed anyway");

        f.Add(("C56", "Downtown and strip-mall poles and wires are explicitly removed from 2015 on; other scene types keep theirs",
            errs.Count == 0, errs.Count == 0
                ? "wires stay through 2005, then go underground on main street and at the strip mall only"
                : Join(errs)));
    }

    // A "NN%" suffix in hashtags.txt boosts one tag out of the sampled pool and
    // onto its own roll — #nostalgia is the reach tag for this account, and at
    // pool odds it showed up in roughly one post in eight. The suffix must never
    // reach a caption, and a winning roll spends a sampled slot rather than
    // making the post longer.
    private static async Task DoC57(
        IDataService dataService,
        List<(string, string, bool?, string)> f)
    {
        var errs = new List<string>();
        var lines = await dataService.LoadHashtagsAsync();

        var weighted = lines.Where(l => System.Text.RegularExpressions.Regex.IsMatch(l, @"\s\d{1,3}%$")).ToList();
        if (weighted.Count == 0)
            errs.Add("hashtags.txt carries no weighted tag — the boost is silently off");

        const int draws = 4000;
        const int expectedPct = 70, tolerancePct = 4;
        var pinned = lines.Where(l => !weighted.Contains(l)).Take(3).ToList();
        var hits = 0;

        for (var i = 0; i < draws; i++)
        {
            var tags = CaptionService.SelectHashtags(lines);

            if (tags.Count != 5)
                errs.Add($"draw {i}: {tags.Count} tags, expected 5 — a boosted tag must spend a sampled slot");
            if (tags.Distinct().Count() != tags.Count)
                errs.Add($"draw {i}: duplicate tag in {string.Join(" ", tags)}");
            if (!tags.Take(3).SequenceEqual(pinned))
                errs.Add($"draw {i}: pinned tags missing or reordered: {string.Join(" ", tags)}");
            if (tags.Any(t => t.Contains('%')))
                errs.Add($"draw {i}: a weight suffix leaked into the caption: {string.Join(" ", tags)}");
            if (tags.Contains("#nostalgia"))
                hits++;

            if (errs.Count > 0) break;   // one bad draw is the whole story
        }

        if (errs.Count == 0)
        {
            var pct = hits * 100.0 / draws;
            if (Math.Abs(pct - expectedPct) > tolerancePct)
                errs.Add($"#nostalgia landed in {pct:F1}% of {draws} captions, expected {expectedPct}% ±{tolerancePct}");
        }

        f.Add(("C57", "A weighted hashtag (#nostalgia 70%) hits its declared share of captions, spends a sampled slot, and never ships its weight suffix",
            errs.Count == 0, errs.Count == 0
                ? $"#nostalgia in ~{hits * 100.0 / draws:F1}% of {draws} draws; pinned set and tag count unchanged"
                : Join(errs)));
    }

    // Period details are era-pool text applied to a geometry that never agreed to
    // hold them. Without an explicit way out the model plants every listed prop
    // somewhere — the failure Vlad hit was a bench standing in the road — so the
    // escape clause has to be in every prompt, in the scene block where the
    // details are listed, and the priority order has to agree that geometry wins.
    private static async Task DoC58(
        IDataService dataService,
        Dictionary<int, Prompt> gasRun1, Dictionary<int, Prompt> gasRun2,
        Dictionary<int, Prompt> dtRun1,  Dictionary<int, Prompt> dtRun2,
        Dictionary<int, Prompt> smRun1,  Dictionary<int, Prompt> smRun2,
        Dictionary<int, Prompt> arRun1,  Dictionary<int, Prompt> arRun2,
        Dictionary<int, Prompt> csRun1,  Dictionary<int, Prompt> csRun2,
        Prompt unknownPrompt,
        List<(string, string, bool?, string)> f)
    {
        var errs = new List<string>();

        var template = await dataService.LoadPromptAsync("image-template");
        if (!template.Contains("one with nowhere to go is left out", StringComparison.Ordinal))
            errs.Add("image-template.txt: PRIORITY ORDER no longer subordinates period details to the geometry");

        void Check(Prompt prompt, string label)
        {
            var text = prompt.Text;
            if (!text.Contains(PromptService.PlacementRule, StringComparison.Ordinal))
            {
                errs.Add($"{label}: scene block carries no placement rule — every listed detail reads as mandatory");
                return;
            }

            // It belongs to PERIOD DETAILS, not to the signage whitelist that
            // follows: past the restriction it reads as a rule about sign text.
            var rule = text.IndexOf(PromptService.PlacementRule, StringComparison.Ordinal);
            var restriction = text.IndexOf("SIGNAGE RESTRICTION", StringComparison.Ordinal);
            if (restriction >= 0 && rule > restriction)
                errs.Add($"{label}: placement rule sits after the SIGNAGE RESTRICTION block");
        }

        foreach (var (run, label) in new[]
        {
            (gasRun1, "gas_station/run1"), (gasRun2, "gas_station/run2"),
            (dtRun1,  "downtown_street/run1"), (dtRun2, "downtown_street/run2"),
            (smRun1,  "strip_mall/run1"), (smRun2, "strip_mall/run2"),
            (arRun1,  "auto_repair/run1"), (arRun2, "auto_repair/run2"),
            (csRun1,  "corner_shop/run1"), (csRun2, "corner_shop/run2"),
        })
            foreach (var (year, prompt) in run)
                Check(prompt, $"{label}/{year}");

        Check(unknownPrompt, "unknown");

        f.Add(("C58", "Period details are conditional on the geometry: every prompt states that a detail with no plausible place is left out, and nothing is placed in the roadway",
            errs.Count == 0, errs.Count == 0
                ? "placement rule present in every era prompt, ahead of the signage whitelist"
                : Join(errs)));
    }

    // YouTube titles are a separate pool from the caption bodies and get posted
    // straight into a field with a hard length limit, so every line in every
    // title file has to stand on its own: it must substitute completely, stay
    // non-empty, and fit. A title is one line and carries only the two year
    // placeholders — {angle}/{condition} are too long to survive here.
    private static async Task DoC59(
        IDataService dataService,
        List<(string, string, bool?, string)> f)
    {
        const int youTubeTitleLimit = 100;
        var errs = new List<string>();
        var summary = new List<string>();

        // base plus every scene type that has its own angle vocabulary. Derived
        // from AnglesByScene rather than listed by hand: this list had already
        // gone stale once, and a scene type missing here has no titles to post.
        var names = CaptionService.AnglesByScene.Keys.Append("base").ToArray();

        foreach (var name in names)
        {
            string raw;
            try
            {
                raw = await dataService.LoadTitleTemplatesAsync(name);
            }
            catch (Exception ex)
            {
                errs.Add($"{name}: LoadTitleTemplatesAsync threw: {ex.Message}");
                continue;
            }

            var titles = CaptionService.SplitTitles(raw);
            if (titles.Count == 0)
            {
                errs.Add($"{name}: no title lines");
                continue;
            }

            foreach (var dupe in titles.GroupBy(t => t, StringComparer.OrdinalIgnoreCase)
                                       .Where(g => g.Count() > 1))
                errs.Add($"{name}: duplicated title — \"{dupe.Key}\"");

            const int minTitles = 12;
            if (titles.Count < minTitles)
                errs.Add($"{name}: {titles.Count} titles (need >= {minTitles}) — the title is the most visible line of a post");

            var longest = 0;
            foreach (var template in titles)
            {
                var title = CaptionService.SubstituteTitle(template, 1975, 2025);

                if (string.IsNullOrWhiteSpace(title))
                    errs.Add($"{name}: empty title after substitution: \"{template}\"");
                // Any surviving brace means a placeholder would be posted literally.
                if (title.Contains('{') || title.Contains('}'))
                    errs.Add($"{name}: unsubstituted placeholder in title: \"{title}\"");
                if (title.Length > youTubeTitleLimit)
                    errs.Add($"{name}: title is {title.Length} chars, over the {youTubeTitleLimit}-char limit: \"{title}\"");

                longest = Math.Max(longest, title.Length);
            }

            summary.Add($"{name}: {titles.Count} titles, longest {longest}");
        }

        f.Add(("C59", "Title templates load for base and every scene type; every line substitutes with no leftover placeholder and stays non-empty and inside YouTube's 100-char limit",
            errs.Count == 0, errs.Count == 0 ? string.Join(" | ", summary) : Join(errs)));
    }

    // The corner shop is the one scene type with a scripted ending: the shop the
    // block walked to becomes the liquor store it drives past. Everything here
    // guards that arc, because the parts that carry it are spread across three
    // files — the name pools, the sign builder and the era content — and any one
    // of them going quiet would leave a scene that still renders, just without
    // the story. Its own budget checks live here too: C11 and C22 take a fixed
    // list of runs, and this scene type is guarded where it is built instead.
    private static void DoC60(
        Dictionary<int, Prompt> csRun1, Dictionary<int, Prompt> csRun2,
        Dictionary<int, Prompt> fsRun1, Dictionary<int, Prompt> fsRun2,
        Dictionary<int, EraProfile> eras,
        ILogger logger,
        List<(string, string, bool?, string)> f)
    {
        var errs = new List<string>();
        const int maxWords = 920;

        int WordCount(string text) =>
            text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
                .Count(t => t.Any(char.IsLetterOrDigit));

        void Check(Dictionary<int, Prompt> run, string label)
        {
            var rank = 0;

            foreach (var year in Years)
            {
                var prompt = run[year];
                var text   = prompt.Text;
                var live   = prompt.SceneCondition is not ("abandoned" or "squatted");
                var where  = $"{label}/{year}";

                if (WordCount(text) >= maxWords)
                    errs.Add($"{where}: {WordCount(text)} words (limit {maxWords})");
                if (text.Length > MaxPromptChars)
                    errs.Add($"{where}: {text.Length} chars (max {MaxPromptChars})");

                // A corner shop never comes back: the era that reopens it
                // renovated is the one beat this scene type must not play.
                if (prompt.SceneCondition is "restored" or "new" && year != Years[0])
                    errs.Add($"{where}: condition '{prompt.SceneCondition}' — the arc only goes down");

                var thisRank = prompt.SceneCondition switch
                {
                    "declining"               => 1,
                    "abandoned" or "squatted" => 2,
                    _                         => 0
                };
                if (thisRank < rank)
                    errs.Add($"{where}: condition recovered from rank {rank} to {thisRank}");
                rank = Math.Max(rank, thisRank);

                var liquor   = text.Contains("a neighbourhood liquor store", StringComparison.Ordinal);
                var original = text.Contains("a neighbourhood grocery", StringComparison.Ordinal)
                            || text.Contains("a neighbourhood pharmacy", StringComparison.Ordinal);

                if (!live)
                {
                    // Closed means closed: a live sign here would light up a shop
                    // the same prompt has just boarded over.
                    if (liquor || original)
                        errs.Add($"{where}: derelict era still hangs a live shop sign");
                    continue;
                }

                if (year >= GenerationContext.LiquorFromYear)
                {
                    if (!liquor)
                        errs.Add($"{where}: no liquor store sign from {GenerationContext.LiquorFromYear} on");
                    if (original)
                        errs.Add($"{where}: still selling groceries after the turnover");
                    // The ghost of the old name is what makes it the same building.
                    if (!text.Contains("the previous business's lettering still ghosts", StringComparison.Ordinal))
                        errs.Add($"{where}: liquor era does not carry the previous owner's ghost sign");
                    if (!text.Contains("regulars of this store, not shoppers", StringComparison.Ordinal)
                        && !text.Contains("NO people anywhere", StringComparison.Ordinal))
                        errs.Add($"{where}: liquor era still draws ordinary shoppers");
                }
                else
                {
                    if (!original)
                        errs.Add($"{where}: no grocery or pharmacy sign before the turnover");
                    if (liquor)
                        errs.Add($"{where}: liquor store appears before {GenerationContext.LiquorFromYear}");
                }
            }

            // One shop, one identity: the trade it opens with is the trade it
            // keeps until the turnover, so a run must not drift between grocery
            // and pharmacy on the way there.
            var kinds = Years
                .Where(y => y < GenerationContext.LiquorFromYear)
                .Select(y => run[y].Text.Contains("a neighbourhood grocery", StringComparison.Ordinal) ? "grocery"
                           : run[y].Text.Contains("a neighbourhood pharmacy", StringComparison.Ordinal) ? "pharmacy"
                           : null)
                .Where(k => k is not null)
                .Distinct()
                .ToList();
            if (kinds.Count > 1)
                errs.Add($"{label}: the shop changes trade mid-run: {string.Join(", ", kinds)}");
        }

        Check(csRun1, "corner_shop/run1");
        Check(csRun2, "corner_shop/run2");
        Check(fsRun1, "freestanding_shop/run1");
        Check(fsRun2, "freestanding_shop/run2");

        // Two fixtures cannot show what the policy does, only what it did twice.
        // The floor and the ceiling are distribution claims, so they are measured
        // across seeds the way C45 measures the general condition spread. Swept
        // for both scene types that share this scripted arc — corner_shop and
        // freestanding_shop resolve PickSceneCondition through the exact same
        // code (GenerationContext.cs:210), so a bug that only shows up for one
        // sceneType string is exactly the kind of thing this doubles up to catch.
        const int seeds = 500;
        const double minClosedFinale = 0.25, maxClosedFinale = 0.45;
        var summaries = new List<string>();

        foreach (var sceneType in new[] { "corner_shop", "freestanding_shop" })
        {
            var closedEarly = 0;
            var repairedLate = 0;
            var closedFinale = 0;

            for (var seed = 0; seed < seeds; seed++)
            {
                var ctx = new GenerationContext
                    { Random = new Random(seed), TotalEras = Years.Length, Years = Years };
                var path = new List<string>();
                foreach (var year in Years)
                {
                    ctx.BeginEra();
                    path.Add(ctx.PickSceneCondition(eras[year].AllowedSceneConditions, sceneType, year));
                }

                for (var i = 0; i < Years.Length; i++)
                {
                    var derelict = path[i] is "abandoned" or "squatted";
                    var healthy  = path[i] is not ("declining" or "abandoned" or "squatted");

                    // Boarded up before the last era would hide the turnover.
                    if (derelict && i < Years.Length - 1) closedEarly++;
                    // In good repair after the decline is supposed to have started.
                    if (healthy && Years[i] >= GenerationContext.DeclineFromYear) repairedLate++;
                }
                if (path[^1] is "abandoned" or "squatted") closedFinale++;
            }

            if (closedEarly > 0)
                errs.Add($"{sceneType}: {closedEarly} of {seeds} seeds board the shop up before the last era — the liquor store is never seen");
            if (repairedLate > 0)
                errs.Add($"{sceneType}: {repairedLate} of {seeds} seeds still show it in good repair from {GenerationContext.DeclineFromYear} on");

            var closedRate = closedFinale / (double)seeds;
            if (closedRate < minClosedFinale || closedRate > maxClosedFinale)
                errs.Add($"{sceneType}: the shop ends boarded up in {closedRate:P0} of runs, expected {minClosedFinale:P0}-{maxClosedFinale:P0}");

            logger.LogInformation(
                "[Smoke] C60 {SceneType} spread across {Seeds} seeds: ends boarded {Rate:P0}, never derelict before the last era",
                sceneType, seeds, closedRate);
            summaries.Add($"{sceneType} ends boarded {closedRate * 100.0:F0}% of the time");
        }

        f.Add(("C60", "The corner shop and the freestanding shop each open as one grocery or pharmacy, turn over to a liquor store from 2015 with the old name ghosting above it, draw regulars rather than shoppers after that, and never recover",
            errs.Count == 0, errs.Count == 0
                ? $"all four runs hold the trade arc, the decline and the prompt budgets; across {seeds} seeds each, {string.Join("; ", summaries)}, never earlier"
                : Join(errs)));
    }

    // The named liquor store belongs to the shared corner_shop/freestanding_shop
    // turnover arc and nothing else: no other scene type resolves a sign name at
    // all — a declining row mentions an unnamed liquor store and stops there. So
    // the live invariant is: corner_shop always draws from liquor_urban,
    // freestanding_shop always draws from liquor_suburban, and no other scene
    // type ever renders a name from either pool.
    //
    // liquor_suburban used to be wired-but-dormant before freestanding_shop
    // existed — corner_shop is always the narrow pre-war frontage, so nothing
    // reached the suburban register. freestanding_shop is by definition the
    // wider unit with a parking apron, so it is the live suburban caller now.
    // strip_mall and shopping_center stay mapped in LiquorKeysFor for whenever
    // one of them gains a named liquor store, but neither calls the resolver
    // today — that half of the mapping assertion below is still intent, not
    // live behaviour, and is labelled as such.
    private static async Task DoC61(
        IDataService dataService,
        Dictionary<int, Prompt> gasRun1, Dictionary<int, Prompt> gasRun2,
        Dictionary<int, Prompt> dtRun1,  Dictionary<int, Prompt> dtRun2,
        Dictionary<int, Prompt> smRun1,  Dictionary<int, Prompt> smRun2,
        Dictionary<int, Prompt> arRun1,  Dictionary<int, Prompt> arRun2,
        Prompt unknownPrompt,
        List<(string, string, bool?, string)> f)
    {
        var errs = new List<string>();

        IReadOnlyDictionary<string, IReadOnlyList<string>> names;
        try
        {
            names = await dataService.LoadCornerShopNamesAsync();
        }
        catch (Exception ex)
        {
            f.Add(("C61", "corner_shop draws from liquor_urban and freestanding_shop draws from liquor_suburban; no other scene type renders a liquor name",
                false, $"LoadCornerShopNamesAsync threw: {ex.Message}"));
            return;
        }

        // 1. Both pools load from corner-shop-liquor-names.txt and stay healthy.
        const int minPerPool = 20;
        var urban    = names.TryGetValue(GenerationContext.LiquorUrbanKey,    out var u) ? u : Array.Empty<string>();
        var suburban = names.TryGetValue(GenerationContext.LiquorSuburbanKey, out var s) ? s : Array.Empty<string>();

        if (urban.Count == 0)    errs.Add($"{GenerationContext.LiquorUrbanKey} did not load");
        if (suburban.Count == 0) errs.Add($"{GenerationContext.LiquorSuburbanKey} did not load");
        if (urban.Count is > 0 and < minPerPool)
            errs.Add($"{GenerationContext.LiquorUrbanKey} has only {urban.Count} names, expected at least {minPerPool}");
        if (suburban.Count is > 0 and < minPerPool)
            errs.Add($"{GenerationContext.LiquorSuburbanKey} has only {suburban.Count} names, expected at least {minPerPool}");

        // A flat "liquor" key left behind would silently be dead data.
        if (names.ContainsKey("liquor"))
            errs.Add("the old flat 'liquor' key is still in the file — it is no longer read");

        // 2. No name in both pools, and none duplicated inside one — a duplicate
        // shrinks the pool invisibly, same as the caption pools.
        foreach (var shared in urban.Intersect(suburban, StringComparer.OrdinalIgnoreCase).OrderBy(n => n))
            errs.Add($"\"{shared}\" appears in both liquor pools");
        foreach (var dupe in urban.GroupBy(n => n, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1))
            errs.Add($"{GenerationContext.LiquorUrbanKey} lists \"{dupe.Key}\" {dupe.Count()} times");
        foreach (var dupe in suburban.GroupBy(n => n, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1))
            errs.Add($"{GenerationContext.LiquorSuburbanKey} lists \"{dupe.Key}\" {dupe.Count()} times");

        // 3. Live behaviour: corner_shop resolves a name and it is always urban;
        // freestanding_shop resolves a name and it is always suburban. Same
        // sweep, opposite register, run for both since they share one resolver.
        const int seeds = 60;
        var drawnUrban    = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var drawnSuburban = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var seed = 1; seed <= seeds; seed++)
        {
            var csCtx = new GenerationContext
            {
                Random = new Random(seed), TotalEras = Years.Length, Years = Years
            };
            // LiquorFromYear onward is the era that actually carries a liquor name.
            var csSign = csCtx.ResolveCornerShop(
                names, GenerationContext.LiquorFromYear, "declining", "corner_shop");

            if (csSign.Name is null)
            {
                errs.Add($"corner_shop/seed {seed}: no liquor name resolved");
            }
            else
            {
                drawnUrban.Add(csSign.Name);
                if (suburban.Contains(csSign.Name, StringComparer.OrdinalIgnoreCase))
                    errs.Add($"corner_shop drew suburban name \"{csSign.Name}\" (seed {seed}) — the frontage is always the narrow pre-war one");
                else if (!urban.Contains(csSign.Name, StringComparer.OrdinalIgnoreCase))
                    errs.Add($"corner_shop drew \"{csSign.Name}\", which is in neither liquor pool (seed {seed})");
            }

            var fsCtx = new GenerationContext
            {
                Random = new Random(seed), TotalEras = Years.Length, Years = Years
            };
            var fsSign = fsCtx.ResolveCornerShop(
                names, GenerationContext.LiquorFromYear, "declining", "freestanding_shop");

            if (fsSign.Name is null)
            {
                errs.Add($"freestanding_shop/seed {seed}: no liquor name resolved");
            }
            else
            {
                drawnSuburban.Add(fsSign.Name);
                if (urban.Contains(fsSign.Name, StringComparer.OrdinalIgnoreCase))
                    errs.Add($"freestanding_shop drew urban name \"{fsSign.Name}\" (seed {seed}) — the footprint is always the wider unit with an apron");
                else if (!suburban.Contains(fsSign.Name, StringComparer.OrdinalIgnoreCase))
                    errs.Add($"freestanding_shop drew \"{fsSign.Name}\", which is in neither liquor pool (seed {seed})");
            }
        }
        if (drawnUrban.Count < 5)
            errs.Add($"corner_shop: only {drawnUrban.Count} distinct names across {seeds} seeds");
        if (drawnSuburban.Count < 5)
            errs.Add($"freestanding_shop: only {drawnSuburban.Count} distinct names across {seeds} seeds");

        // 4. Live behaviour: no other scene type renders a liquor name. Checked
        // against the generated prompts rather than the resolver, because this is
        // a property of where PromptService calls it from, not of the mapping.
        // Case-sensitive: the names are sign text in capitals, while the era pools
        // describe an unnamed liquor store in lower-case prose.
        var allLiquorNames = urban.Concat(suburban).ToList();
        void CheckNoName(Prompt prompt, string label)
        {
            foreach (var name in allLiquorNames)
                if (prompt.Text.Contains(name, StringComparison.Ordinal))
                    errs.Add($"{label}: renders liquor name \"{name}\" — only corner_shop or freestanding_shop may carry one");
        }

        foreach (var (run, label) in new[]
        {
            (gasRun1, "gas/run1"), (gasRun2, "gas/run2"),
            (dtRun1,  "downtown/run1"), (dtRun2, "downtown/run2"),
            (smRun1,  "strip_mall/run1"), (smRun2, "strip_mall/run2"),
            (arRun1,  "auto_repair/run1"), (arRun2, "auto_repair/run2"),
        })
            foreach (var (year, prompt) in run)
                CheckNoName(prompt, $"{label}/{year}");

        CheckNoName(unknownPrompt, "unknown");

        // 5. Mapping correctness. corner_shop and freestanding_shop are asserted
        // as live callers (both actually reach the resolver, checked in 3 above);
        // strip_mall and shopping_center are asserted only on LiquorKeysFor,
        // since neither calls ResolveCornerShop today — that pairing stays
        // intent, not observed behaviour.
        foreach (var scene in new[] { "downtown_street", "corner_shop" })
            if (!GenerationContext.LiquorKeysFor(scene).SequenceEqual(new[] { GenerationContext.LiquorUrbanKey }))
                errs.Add($"LiquorKeysFor(\"{scene}\") no longer maps to the urban pool");
        foreach (var scene in new[] { "strip_mall", "shopping_center", "freestanding_shop" })
            if (!GenerationContext.LiquorKeysFor(scene).SequenceEqual(new[] { GenerationContext.LiquorSuburbanKey }))
                errs.Add($"LiquorKeysFor(\"{scene}\") no longer maps to the suburban pool");
        if (GenerationContext.LiquorKeysFor("gas_station").Count != 2)
            errs.Add("LiquorKeysFor no longer falls back to both pools for an unlisted scene type");

        f.Add(("C61", "corner_shop always draws from liquor_urban and freestanding_shop always draws from liquor_suburban; no other scene type renders a liquor name; strip_mall/shopping_center stay mapped to suburban for later, still unused today",
            errs.Count == 0, errs.Count == 0
                ? $"urban {urban.Count}, suburban {suburban.Count}, no overlap; corner_shop drew {drawnUrban.Count} distinct urban names and freestanding_shop drew {drawnSuburban.Count} distinct suburban names across {seeds} seeds each; no liquor name in any other scene type"
                : Join(errs)));
    }

    // C61 only checks pool membership — every drawn name is in the right
    // register, never in the wrong one. It says nothing about the shape of the
    // randomness itself: which names actually come up, how evenly, whether the
    // origin-kind coin flip is really 50/50 in practice. This check exists to
    // make that visible rather than just asserted — the full per-name table for
    // both scene types is logged so it can be read straight out of a
    // --smoke-prompts run, not just inferred from a pass/fail line.
    private static async Task DoC72(
        IDataService dataService,
        ILogger logger,
        List<(string, string, bool?, string)> f)
    {
        var errs = new List<string>();

        IReadOnlyDictionary<string, IReadOnlyList<string>> names;
        try
        {
            names = await dataService.LoadCornerShopNamesAsync();
        }
        catch (Exception ex)
        {
            f.Add(("C72", "Liquor-name and origin-kind randomness for corner_shop and freestanding_shop is visible and healthy across many seeds",
                false, $"LoadCornerShopNamesAsync threw: {ex.Message}"));
            return;
        }

        const int seeds = 300;

        // Draws (sceneType, urban vs suburban pool) and logs the full
        // distribution — which brand names actually come up, and how the
        // grocery/pharmacy coin flip landed — for one scene type.
        (int Distinct, double CoveragePct) SweepAndLog(string sceneType, IReadOnlyList<string> pool, IReadOnlyList<string> otherRegister, string registerLabel)
        {
            var nameCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var groceryCount = 0;
            var pharmacyCount = 0;

            for (var seed = 1; seed <= seeds; seed++)
            {
                var ctx = new GenerationContext
                {
                    Random = new Random(seed), TotalEras = Years.Length, Years = Years
                };
                // LiquorFromYear is the era that actually carries the liquor name;
                // condition "declining" keeps the shop alive long enough to have one.
                var sign = ctx.ResolveCornerShop(names, GenerationContext.LiquorFromYear, "declining", sceneType);

                if (ctx.CornerShopOriginKind == GenerationContext.CornerShopKind.Grocery)
                    groceryCount++;
                else
                    pharmacyCount++;

                if (sign.Name is null)
                {
                    errs.Add($"{sceneType}/seed {seed}: no liquor name resolved");
                    continue;
                }
                if (otherRegister.Contains(sign.Name, StringComparer.OrdinalIgnoreCase))
                    errs.Add($"{sceneType}/seed {seed}: drew \"{sign.Name}\" from the wrong register");

                nameCounts[sign.Name] = nameCounts.GetValueOrDefault(sign.Name) + 1;
            }

            var table = string.Join(" | ", nameCounts
                .OrderByDescending(kv => kv.Value)
                .ThenBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase)
                .Select(kv => $"{kv.Key}={kv.Value}"));

            logger.LogInformation(
                "[Smoke] C72 {SceneType} liquor draws across {Seeds} seeds ({Register} pool, origin {Grocery} grocery / {Pharmacy} pharmacy): {Table}",
                sceneType, seeds, registerLabel, groceryCount, pharmacyCount, table);

            var distinct = nameCounts.Count;
            var coverage = pool.Count == 0 ? 0.0 : distinct * 100.0 / pool.Count;

            // A healthy pool gets sampled close to fully over this many draws —
            // coupon-collector math says a 37-name pool converges well inside 300
            // seeds. A low number here means something is silently narrowing the
            // pool (a filter bug, a broken seed) rather than the RNG being unlucky.
            if (coverage < 85.0)
                errs.Add($"{sceneType}: only {distinct}/{pool.Count} names hit ({coverage:F0}%) across {seeds} seeds — the pool is not being sampled evenly");

            // The origin-kind pick is a plain coin flip (Random.Next(2)); over 300
            // seeds a real 50/50 split should not land anywhere near all-one-side.
            var minSide = Math.Min(groceryCount, pharmacyCount);
            if (minSide < seeds * 0.35)
                errs.Add($"{sceneType}: origin kind split {groceryCount} grocery / {pharmacyCount} pharmacy is too lopsided for a 50/50 pick");

            return (distinct, coverage);
        }

        var urban    = names.TryGetValue(GenerationContext.LiquorUrbanKey,    out var u) ? u : Array.Empty<string>();
        var suburban = names.TryGetValue(GenerationContext.LiquorSuburbanKey, out var s) ? s : Array.Empty<string>();

        var (csDistinct, csCoverage) = SweepAndLog("corner_shop", urban, suburban, "urban");
        var (fsDistinct, fsCoverage) = SweepAndLog("freestanding_shop", suburban, urban, "suburban");

        f.Add(("C72", "Liquor-name and origin-kind randomness for corner_shop and freestanding_shop is visible and healthy across many seeds",
            errs.Count == 0, errs.Count == 0
                ? $"corner_shop: {csDistinct}/{urban.Count} urban names hit ({csCoverage:F0}%); freestanding_shop: {fsDistinct}/{suburban.Count} suburban names hit ({fsCoverage:F0}%) — full tables in the log above"
                : Join(errs)));
    }

    // A tree that is only ever given a size reads as dead, and era chaining makes
    // that permanent — so the corner shop's street tree has to say it is alive in
    // every era, and its state has to track the shop's own arc rather than sitting
    // frozen. The other scene types deliberately carry no state clause: there is no
    // scripted arc for it to follow there and no prompt budget to spend on it.
    private static void DoC62(
        Dictionary<int, Prompt> csRun1, Dictionary<int, Prompt> csRun2,
        Dictionary<int, Prompt> gasRun1, Dictionary<int, Prompt> dtRun1,
        Dictionary<int, Prompt> smRun1,  Dictionary<int, Prompt> arRun1,
        List<(string, string, bool?, string)> f)
    {
        var errs = new List<string>();

        const string healthy   = "in full summer leaf";
        const string declining = "in leaf but untrimmed";
        const string derelict  = "half of it dead";
        var states = new[] { healthy, declining, derelict };

        var seen = new HashSet<string>();

        void CheckCorner(Dictionary<int, Prompt> run, string label)
        {
            foreach (var (year, prompt) in run)
            {
                var text = prompt.Text;
                // 2025 unchained shows the trees at base size already, so the whole
                // section is dropped — nothing to assert for that era.
                if (!text.Contains("\nTREES\n", StringComparison.Ordinal))
                    continue;

                var where   = $"{label}/{year}";
                var present = states.Where(st => text.Contains(st, StringComparison.Ordinal)).ToList();

                if (present.Count == 0)
                {
                    errs.Add($"{where}: TREES states a size but never says the tree is alive");
                    continue;
                }
                if (present.Count > 1)
                    errs.Add($"{where}: TREES carries {present.Count} conflicting tree states");
                seen.Add(present[0]);

                // The tree's state has to agree with the era it is standing in.
                var expected = prompt.SceneCondition switch
                {
                    "abandoned" or "squatted" => derelict,
                    "declining"               => declining,
                    _                         => healthy,
                };
                if (!text.Contains(expected, StringComparison.Ordinal))
                    errs.Add($"{where}: condition '{prompt.SceneCondition}' but the tree does not read as \"{expected}\"");

                // Growth must still be there — the state clause is an addition to
                // the size line, not a replacement for it.
                if (!text.Contains("% of its canopy", StringComparison.Ordinal))
                    errs.Add($"{where}: tree state replaced the canopy sizing instead of adding to it");
            }
        }

        CheckCorner(csRun1, "corner_shop/run1");
        CheckCorner(csRun2, "corner_shop/run2");

        // Across the two fixture runs the arc has to actually move — a tree stuck
        // on one state for every era is the bug this check exists for.
        if (seen.Count < 2)
            errs.Add($"the tree holds one state ({string.Join(", ", seen)}) across every corner_shop era — the arc is not moving");

        // Scoping: no other scene type spends words on a tree state.
        foreach (var (run, label) in new[]
        {
            (gasRun1, "gas"), (dtRun1, "downtown"), (smRun1, "strip_mall"), (arRun1, "auto_repair"),
        })
            foreach (var (year, prompt) in run)
                foreach (var st in states)
                    if (prompt.Text.Contains(st, StringComparison.Ordinal))
                        errs.Add($"{label}/{year}: carries the corner_shop tree state \"{st}\"");

        f.Add(("C62", "The corner shop's street tree reads as a living tree in every era and its state follows the shop's arc (leafy while trading, untrimmed while declining, half dead once derelict); other scene types carry no tree state",
            errs.Count == 0, errs.Count == 0
                ? $"corner_shop tree moves through {seen.Count} states across the run, canopy sizing intact, no other scene type affected"
                : Join(errs)));
    }

    // One Vision-facing scene type, two content vocabularies. The split is a
    // pure function of terrain, so it is checked as one — including the values
    // Vision can emit that nobody planned for: a rural highway is the safe
    // default because an unrecognised terrain on an open road is far more often
    // country than city, and the urban content assumes a skyline and barrier
    // walls that would be wrong to invent.
    private static void DoC63(List<(string, string, bool?, string)> f)
    {
        var errs = new List<string>();

        void Expect(string sceneType, string? terrain, string want)
        {
            var got = SceneContentKey.Resolve(sceneType, terrain);
            if (got != want)
                errs.Add($"Resolve(\"{sceneType}\", {terrain ?? "null"}) = \"{got}\", expected \"{want}\"");
        }

        // Suburban and industrial corridors are built like the urban flavor —
        // multiple lanes, barriers, warehouses — not like a road through farmland.
        foreach (var terrain in new[] { "urban", "suburban", "industrial" })
            Expect("highway", terrain, "highway_urban");

        // Only literal "rural" and anything Vision was not supposed to emit.
        // "URBAN" is here on purpose: the match is ordinal, and Vision's schema
        // emits these values lowercase.
        foreach (var terrain in new[] { "rural", "", "URBAN", "Suburban", "anything-else" })
            Expect("highway", terrain, "highway_rural");
        Expect("highway", null, "highway_rural");

        // Every other scene type is its own key: the structural type and the
        // content key only diverge for highway.
        foreach (var sceneType in new[] { "gas_station", "downtown_street", "strip_mall", "auto_repair", "corner_shop", "mall", "shopping_center", "default", "unknown" })
        {
            Expect(sceneType, "urban", sceneType);
            Expect(sceneType, null, sceneType);
        }

        f.Add(("C63", "SceneContentKey splits highway into urban/rural content keys by terrain — urban, suburban and industrial all take the corridor flavor, only rural and unrecognized or missing values take the countryside one — and leaves every other scene type unchanged",
            errs.Count == 0, errs.Count == 0 ? "content key resolution holds for every terrain and scene type" : Join(errs)));
    }

    // A highway is the first scene type with no storefront and no stationary
    // traffic, and both of those are worded into blocks written for lots and
    // shopfronts. The vocabulary is what breaks: a parked car on an interstate
    // or a shop front on the shoulder both render as a different place
    // entirely, so the wording is asserted rather than assumed. The traffic arc
    // is the content half — urban fills up and goes packed, rural never does.
    private static async Task DoC64(
        IPromptService promptService,
        Dictionary<int, EraProfile> eras,
        SceneDna highwayUrbanScene,
        Dictionary<int, Prompt> hwUrban,
        Dictionary<int, Prompt> hwRural,
        List<(string, string, bool?, string)> f)
    {
        var errs = new List<string>();

        string VehiclesSection(string text)
        {
            var start = text.IndexOf("VEHICLES\n", StringComparison.Ordinal);
            if (start < 0) return "";
            var end = text.IndexOf("\n\n", start, StringComparison.Ordinal);
            return end < 0 ? text[start..] : text[start..end];
        }

        foreach (var (run, label) in new[] { (hwUrban, "highway_urban"), (hwRural, "highway_rural") })
            foreach (var (year, prompt) in run)
            {
                var vehicles = VehiclesSection(prompt.Text);
                if (vehicles.Length == 0)
                {
                    errs.Add($"{label}/{year}: no VEHICLES section");
                    continue;
                }

                foreach (var word in new[] { "parked", "curb", "stall" })
                    if (vehicles.Contains(word, StringComparison.OrdinalIgnoreCase))
                        errs.Add($"{label}/{year}: vehicles section says '{word}' — this is moving traffic");
                if (vehicles.Contains("PLACEMENT:", StringComparison.Ordinal))
                    errs.Add($"{label}/{year}: vehicles section carries a PLACEMENT line");

                // Nobody is on foot, but the road is not deserted either.
                if (prompt.Text.Contains("completely deserted", StringComparison.Ordinal))
                    errs.Add($"{label}/{year}: reads as deserted rather than as a road with no pedestrians");

                foreach (var chain in new[] { "Blockbuster", "RadioShack" })
                    if (prompt.Text.Contains(chain, StringComparison.Ordinal))
                        errs.Add($"{label}/{year}: chain tenant '{chain}' on a highway");
                if (prompt.Text.Contains("window signs:", StringComparison.Ordinal))
                    errs.Add($"{label}/{year}: emits a window-signs line");
            }

        // Traffic arc: urban fills up and stops being countable, rural never
        // does. Asserted on the wording each path actually emits.
        foreach (var year in new[] { 1975, 1985, 1995 })
            if (!hwUrban[year].Text.Contains("EXACTLY", StringComparison.Ordinal))
                errs.Add($"highway_urban/{year}: expected an exact vehicle count before the traffic fills in");
        foreach (var year in new[] { 2005, 2015, 2025 })
        {
            if (!hwUrban[year].Text.Contains("DENSE MOVING TRAFFIC", StringComparison.Ordinal))
                errs.Add($"highway_urban/{year}: expected packed traffic from 2005 on");
            if (hwUrban[year].Text.Contains("EXACTLY", StringComparison.Ordinal))
                errs.Add($"highway_urban/{year}: still gives an exact vehicle count while packed");
        }

        foreach (var (year, prompt) in hwRural)
        {
            if (prompt.Text.Contains("DENSE MOVING TRAFFIC", StringComparison.Ordinal))
                errs.Add($"highway_rural/{year}: rural traffic went packed — the volume never grew out here");
            var m = System.Text.RegularExpressions.Regex.Match(prompt.Text, @"EXACTLY (\d+) period vehicle");
            if (!m.Success)
                errs.Add($"highway_rural/{year}: no exact vehicle count");
            else if (int.Parse(m.Groups[1].Value) is var n && (n < 1 || n > 3))
                errs.Add($"highway_rural/{year}: {n} vehicles, expected 1-3");
        }

        // Real highway data ships with empty storefronts, so the guard would
        // pass on an empty pool whether or not it was there. Feed it a stub era
        // that does carry them: that is the regression this catches.
        var era = eras[1985];
        var content = era.SceneContent!["highway_urban"];
        var stubbed = new Dictionary<string, SceneContent>(era.SceneContent!)
        {
            ["highway_urban"] = content with
            {
                Storefronts = ["SMOKE-STOREFRONT-MARKER visible from the roadway"],
                WindowSigns = ["SMOKE-WINDOW-SIGN"],
            }
        };
        var stubEra = era with { SceneContent = stubbed };
        var stubCtx = new GenerationContext { Random = new Random(7), TotalEras = 1 };
        var stubPrompt = await promptService.BuildAsync(highwayUrbanScene, stubEra, stubCtx);

        // Only the storefront/architecture/signage pool is guarded. Window signs
        // ride the shared path and stay there deliberately: highway data ships
        // them empty, so the line never appears (asserted on the real runs
        // above) without a second branch to keep in sync.
        if (stubPrompt.Text.Contains("SMOKE-STOREFRONT-MARKER", StringComparison.Ordinal))
            errs.Add("storefront content reached a highway prompt");

        f.Add(("C64", "A highway prompt describes moving traffic and no storefronts: no parked/curb/stall wording, no PLACEMENT line, no shop content even when the era offers some, and the urban flavor goes packed from 2005 while the rural one never does",
            errs.Count == 0, errs.Count == 0
                ? "both flavors hold the traffic wording and the density arc"
                : Join(errs)));
    }

    // Vision reads the signs in the photo, so Distinctive comes back with the
    // route number and exit mile printed on them — and that line is repeated
    // verbatim into every era of the run. A number that is right for the photo
    // is a falsifiable claim about 1975, and wrong in the way a comment section
    // catches. The sign has to survive as geometry; the numbering does not.
    private static async Task DoC65(
        IPromptService promptService,
        IDataService dataService,
        Dictionary<int, EraProfile> eras,
        SceneDna highwayScene,
        List<(string, string, bool?, string)> f)
    {
        var errs = new List<string>();

        // Distinctive only reaches a prompt through the synthetic base — the era
        // prompts run the short fixed PRESERVE block — so that is where this is
        // asserted, plus every era prompt as a regression guard in case the
        // generated block is switched back on.
        var texts = new List<(string Label, string Text)>
        {
            ("base_synthetic", await promptService.BuildBaseAsync(highwayScene, Years[0])),
        };
        var ctx = new GenerationContext { Random = new Random(11), TotalEras = Years.Length, Years = Years };
        foreach (var year in Years)
            texts.Add(($"era/{year}", (await promptService.BuildAsync(highwayScene, eras[year], ctx)).Text));

        var numbered = new (string Name, System.Text.RegularExpressions.Regex Pattern)[]
        {
            ("interstate route", new(@"\bI-\d+\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase)),
            ("exit number",      new(@"\bexit\s*\d+", System.Text.RegularExpressions.RegexOptions.IgnoreCase)),
            ("route number",     new(@"\b(?:route|rte\.?|hwy\.?|highway|us)\s*-?\s*\d+\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase)),
            ("mile marker",      new(@"\bmile\s*(?:marker|post)?\s*\d+\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase)),
        };

        foreach (var (label, text) in texts)
            foreach (var (name, pattern) in numbered)
            {
                var m = pattern.Match(text);
                if (m.Success)
                    errs.Add($"{label}: carries a {name} — \"{m.Value}\"");
            }

        // The point is genericization, not deletion: the gantry is real geometry
        // and has to stay, or it appears and vanishes between decades.
        var basePrompt = texts[0].Text;
        if (!basePrompt.Contains("green directional guide sign overhead", StringComparison.Ordinal))
            errs.Add("base_synthetic: the overhead sign was dropped instead of genericized");
        // A landmark with no numbering in it is not signage and must survive intact.
        if (!basePrompt.Contains("a distinctive curved concrete overpass with exposed beams", StringComparison.Ordinal))
            errs.Add("base_synthetic: an unnumbered physical landmark was filtered out");

        f.Add(("C65", "Route numbers, exit numbers and mile markers are genericized out of Distinctive while the sign itself and every unnumbered landmark survive",
            errs.Count == 0, errs.Count == 0
                ? "numbered route signage genericized; gantry and landmark kept"
                : Join(errs)));
    }

    // A highway with buildings in frame has to age them, or six decades pass
    // with the background untouched and the run reads as a still. A highway with
    // nothing in frame must stay empty: inventing a business on an open road is
    // the failure this branch is gated to avoid.
    private static void DoC66(
        Dictionary<int, EraProfile> eras,
        Dictionary<int, Prompt> hwOpenRoad,
        Dictionary<int, Prompt> hwWithBuildings,
        List<(string, string, bool?, string)> f)
    {
        var errs = new List<string>();

        int TenantLines(string text, IReadOnlyList<string>? pool) =>
            pool is null ? 0 : pool.Count(t => text.Contains(t, StringComparison.Ordinal));

        foreach (var year in Years)
        {
            var pool = eras[year].SceneContent?["highway_urban"].BackgroundTenants;
            if (pool is not { Count: > 0 })
            {
                errs.Add($"{year}: highway_urban has no background_tenants pool");
                continue;
            }

            var withBuildings = TenantLines(hwWithBuildings[year].Text, pool);
            if (withBuildings != 1)
                errs.Add($"{year}: a highway with buildings in frame emitted {withBuildings} background tenant lines, expected exactly 1");

            var openRoad = TenantLines(hwOpenRoad[year].Text, pool);
            if (openRoad != 0)
                errs.Add($"{year}: an open-road highway named {openRoad} background businesses");
        }

        f.Add(("C66", "A highway names exactly one generic background business per era when buildings are in frame, and none at all on open road",
            errs.Count == 0, errs.Count == 0
                ? "background tenants follow the buildings, one per era, never on an empty road"
                : Join(errs)));
    }

    // Every prompt has to say what may be readable in the frame. The whitelist
    // covers scenes that quote their signage; the scene type that quotes nothing
    // used to get no restriction at all, and the model filled that silence by
    // reading the road — a place name on a guide sign, then a skyline to match
    // it, then a city that was never in the photograph. So the absence of a
    // whitelist has to be stated as one.
    //
    // The other half is upstream: a prompt must not ask for lettering it cannot
    // specify. "with exit numbering" or "a name across the front" is an
    // instruction to invent text, and no downstream restriction fully undoes it.
    private static void DoC67(
        Dictionary<int, Prompt> gasRun1, Dictionary<int, Prompt> dtRun1,
        Dictionary<int, Prompt> smRun1,  Dictionary<int, Prompt> arRun1,
        Dictionary<int, Prompt> csRun1,
        Dictionary<int, Prompt> hwUrban, Dictionary<int, Prompt> hwRural,
        Dictionary<int, Prompt> hwBuilt,
        Prompt unknownPrompt,
        Dictionary<int, EraProfile> eras,
        List<(string, string, bool?, string)> f)
    {
        var errs = new List<string>();

        // 1. No prompt may ship without a statement about readable text.
        var all = new List<(string Label, Prompt Prompt)> { ("unknown", unknownPrompt) };
        foreach (var (run, label) in new[]
        {
            (gasRun1, "gas_station"), (dtRun1, "downtown_street"), (smRun1, "strip_mall"),
            (arRun1, "auto_repair"), (csRun1, "corner_shop"),
            (hwUrban, "highway_urban"), (hwRural, "highway_rural"), (hwBuilt, "highway_urban_buildings"),
        })
            foreach (var (year, prompt) in run)
                all.Add(($"{label}/{year}", prompt));

        foreach (var (label, prompt) in all)
        {
            var hasWhitelist   = prompt.Text.Contains("The only readable text anywhere in the image is:", StringComparison.Ordinal);
            var hasNoTextRule  = prompt.Text.Contains("NO readable text anywhere", StringComparison.Ordinal)
                              || prompt.Text.Contains("No sign text anywhere", StringComparison.Ordinal)
                              || prompt.Text.Contains("do not turn words from this prompt into signage", StringComparison.OrdinalIgnoreCase);
            if (!hasWhitelist && !hasNoTextRule)
                errs.Add($"{label}: no signage restriction at all — lettering is unconstrained");
        }

        // 2. A highway names no place and asks for no numbered signage. Checked
        // on the prompt text rather than the data so a new entry anywhere in the
        // pipeline is caught.
        var geographic = new (string Name, System.Text.RegularExpressions.Regex Pattern)[]
        {
            ("skyline",          new(@"\bskylines?\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase)),
            ("downtown",         new(@"\bdowntown\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase)),
            ("exit numbering",   new(@"\bexit\s*(?:number|numbering|sign)", System.Text.RegularExpressions.RegexOptions.IgnoreCase)),
            ("destination name", new(@"\bdestination\s+name", System.Text.RegularExpressions.RegexOptions.IgnoreCase)),
        };

        foreach (var (run, label) in new[]
        {
            (hwUrban, "highway_urban"), (hwRural, "highway_rural"), (hwBuilt, "highway_urban_buildings"),
        })
            foreach (var (year, prompt) in run)
            {
                // The restriction itself legitimately names what must not appear.
                var body = prompt.Text.Split("SIGNAGE RESTRICTION", StringSplitOptions.None)[0];
                foreach (var (name, pattern) in geographic)
                {
                    var m = pattern.Match(body);
                    if (m.Success)
                        errs.Add($"{label}/{year}: asks for {name} — \"{m.Value}\"");
                }
            }

        // 3. Background trades name a building and a sign technology, never a
        // readable business name.
        foreach (var year in Years)
            foreach (var key in new[] { "highway_urban", "highway_rural" })
                foreach (var tenant in eras[year].SceneContent?[key].BackgroundTenants ?? [])
                {
                    // Matched on markers, not on whole phrases: the wording of
                    // these entries is meant to vary, but every one of them has
                    // to say somewhere that the text cannot be read.
                    string[] illegible =
                    {
                        "to read", "past reading", "illegible", "unreadable", "indistinct",
                        "no legible", "no readable", "no sign at all", "to resolve", "to make out",
                        "not readable", "not legible", "neither legible",
                    };
                    var legible = illegible.Any(m => tenant.Contains(m, StringComparison.OrdinalIgnoreCase));
                    if (!legible)
                        errs.Add($"{year}/{key}: background trade invites a readable name — \"{tenant}\"");
                }

        f.Add(("C67", "Every prompt states what may be read in the frame — a whitelist or an explicit none — and a highway asks for no skyline, no exit numbering and no legible business name",
            errs.Count == 0, errs.Count == 0
                ? "signage is constrained in every prompt; highway invents no geography"
                : Join(errs)));
    }

    // The blanket "NO readable text anywhere" was read as "no text": the gantry
    // came back as an empty dark rectangle in every era, which looks broken
    // rather than distant. A highway states the sign positively — green face,
    // white border, illegible legend — so the object survives and only the words
    // are withheld. Every other scene type keeps the blanket rule.
    private static void DoC68(
        Dictionary<int, Prompt> gasRun1, Dictionary<int, Prompt> dtRun1,
        Dictionary<int, Prompt> hwUrban, Dictionary<int, Prompt> hwRural,
        Dictionary<int, Prompt> hwBuilt,
        List<(string, string, bool?, string)> f)
    {
        var errs = new List<string>();
        const string blanket = "NO readable text anywhere";
        const string greenFace = "keeps its green face and white border";
        const string softShapes = "soft white shapes that never resolve into words";

        foreach (var (run, label) in new[]
        {
            (hwUrban, "highway_urban"), (hwRural, "highway_rural"), (hwBuilt, "highway_urban_buildings"),
        })
            foreach (var (year, prompt) in run)
            {
                if (prompt.Text.Contains(blanket, StringComparison.Ordinal))
                    errs.Add($"{label}/{year}: still carries the blanket no-text block — the guide sign renders blank");
                if (!prompt.Text.Contains(greenFace, StringComparison.Ordinal))
                    errs.Add($"{label}/{year}: guide sign is not described as keeping its face");
                if (!prompt.Text.Contains(softShapes, StringComparison.Ordinal))
                    errs.Add($"{label}/{year}: legend is not held to unreadable shapes");
                // The whole point is that the words stay withheld.
                foreach (var phrase in new[] { "no place names", "no route or exit numbers" })
                    if (!prompt.Text.Contains(phrase, StringComparison.OrdinalIgnoreCase))
                        errs.Add($"{label}/{year}: signage block dropped \"{phrase}\"");
                // Nothing may describe the sign as turned away or unreadable as
                // an object — that is what produced the empty rectangle.
                foreach (var kill in new[] { "angled away", "angled too far", "unreadable from here" })
                    if (prompt.Text.Contains(kill, StringComparison.OrdinalIgnoreCase))
                        errs.Add($"{label}/{year}: a period detail deletes the sign — \"{kill}\"");
            }

        // A scene that quotes no signage and is not a highway keeps the blanket
        // rule; one that quotes its signage keeps the whitelist.
        foreach (var (run, label) in new[] { (gasRun1, "gas_station"), (dtRun1, "downtown_street") })
            foreach (var (year, prompt) in run)
                if (prompt.Text.Contains(greenFace, StringComparison.Ordinal))
                    errs.Add($"{label}/{year}: got the highway signage variant");

        f.Add(("C68", "A highway keeps its guide sign as a green-faced object with an illegible legend instead of the blanket no-text block; no period detail turns the sign away, and no other scene type gets the highway variant",
            errs.Count == 0, errs.Count == 0
                ? "highway signage stays visible and wordless; other scene types unchanged"
                : Join(errs)));
    }

    // Era architecture is scene-blind: it names commercial styles and storefront
    // materials for whatever is being built. On a scene with no buildings that
    // is an instruction to invent some, and the synthetic base came back with a
    // retail row behind the median of an empty interstate. Where nothing stands,
    // the prompt has to describe the frontage instead — otherwise the model
    // fills the gap on its own.
    private static async Task DoC69(
        IPromptService promptService,
        SceneDna highwayOpenRoad,
        SceneDna highwayWithBuildings,
        SceneDna downtownScene,
        List<(string, string, bool?, string)> f)
    {
        var errs = new List<string>();
        string[] architectureLines = { "- commercial architecture:", "- building materials:" };

        var openRoad = await promptService.BuildBaseAsync(highwayOpenRoad, Years[0]);
        foreach (var line in architectureLines)
            if (openRoad.Contains(line, StringComparison.Ordinal))
                errs.Add($"open-road base still emits \"{line.Trim()}\" with no buildings in the scene");
        if (!openRoad.Contains("roadway frontage", StringComparison.Ordinal))
            errs.Add("open-road base names no frontage — nothing describes what borders the road");
        if (!openRoad.Contains("no buildings anywhere in the frame", StringComparison.Ordinal))
            errs.Add("open-road base does not state that nothing is built");

        // The same scene type with real buildings keeps the architecture lines:
        // the gate is the building list, not the scene type.
        var withBuildings = await promptService.BuildBaseAsync(highwayWithBuildings, Years[0]);
        foreach (var line in architectureLines)
            if (!withBuildings.Contains(line, StringComparison.Ordinal))
                errs.Add($"highway with buildings lost \"{line.Trim()}\"");

        var downtown = await promptService.BuildBaseAsync(downtownScene, Years[0]);
        foreach (var line in architectureLines)
            if (!downtown.Contains(line, StringComparison.Ordinal))
                errs.Add($"downtown base lost \"{line.Trim()}\"");

        f.Add(("C69", "The synthetic base emits architecture only where buildings exist; an open-road scene gets a frontage description and an explicit nothing-is-built line instead",
            errs.Count == 0, errs.Count == 0
                ? "architecture follows the building list, not the scene type"
                : Join(errs)));
    }

    // A background tree sized by the ordinary per-size rate tripled across the
    // run and ended up covering the sign gantry it stands behind. Distance
    // compresses growth as the frame sees it, so a background tree grows on its
    // own flatter curve — and the ceiling is what this asserts, because the
    // failure was visual dominance in the last era, not the rate itself.
    private static async Task DoC70(
        IPromptService promptService,
        Dictionary<int, EraProfile> eras,
        SceneDna highwayScene,
        SceneDna downtownScene,
        List<(string, string, bool?, string)> f)
    {
        var errs = new List<string>();
        const int ceilingPct = 165;   // 160% plus one 5% rounding step

        var canopy = new System.Text.RegularExpressions.Regex(
            @"about (\d+)% of its canopy",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        int? FirstCanopyPct(string promptText)
        {
            var trees = TreesSection(promptText);
            if (trees.Length == 0) return null;
            var m = canopy.Match(trees);
            return m.Success ? int.Parse(m.Groups[1].Value) : null;
        }

        // Unchained: every era states a fraction of the base image, which is the
        // source year. Growth across the whole run is therefore the reciprocal
        // of the earliest era's fraction.
        var unchained = new GenerationContext { Random = new Random(5), TotalEras = Years.Length, Years = Years };
        var firstEraPct = 0;
        foreach (var year in Years)
        {
            var prompt = await promptService.BuildAsync(highwayScene, eras[year], unchained);
            if (year != Years[0]) continue;
            firstEraPct = FirstCanopyPct(prompt.Text) ?? 0;
        }
        if (firstEraPct == 0)
            errs.Add("unchained: the first era states no canopy fraction for the background tree");
        else
        {
            var acrossRun = (int)Math.Round(100.0 / firstEraPct * 100);
            if (acrossRun > ceilingPct)
                errs.Add($"unchained: background tree ends the run at {acrossRun}% of its first-era canopy " +
                         $"(first era {firstEraPct}% of base, ceiling {ceilingPct}%)");
        }

        // Chained: each era states growth against the era before it, so the run
        // total is the product of the steps. The two paths are inverses, so this
        // must land on the same ceiling.
        var chained = new GenerationContext
        {
            Random = new Random(5), TotalEras = Years.Length, Years = Years,
            ChainedFromPreviousEra = true
        };
        double product = 1.0;
        var steps = new List<string>();
        foreach (var year in Years)
        {
            var prompt = await promptService.BuildAsync(highwayScene, eras[year], chained);
            if (year == Years[0]) continue;      // first era is edited from the base
            var pct = FirstCanopyPct(prompt.Text);
            if (pct is null)
            {
                errs.Add($"chained/{year}: no canopy percentage for the background tree");
                continue;
            }
            steps.Add($"{year}:{pct}%");
            product *= pct.Value / 100.0;
            if (pct > 125)
                errs.Add($"chained/{year}: background tree grows {pct}% in a single decade");
        }
        var chainedTotal = (int)Math.Round(product * 100);
        if (chainedTotal > ceilingPct)
            errs.Add($"chained: background tree reaches {chainedTotal}% across the run (ceiling {ceilingPct}%): {string.Join(" ", steps)}");

        // The point of the separate rate is that it is flatter than a kerbside
        // tree of the SAME recorded size — without this the check would pass on a
        // run where every tree was slowed down. Matched on the medium tree by
        // name: comparing against whichever tree happens to be listed first
        // compares against a large one, which is legitimately flatter than any
        // background rate and would make this assertion meaningless.
        var foreground = new GenerationContext { Random = new Random(5), TotalEras = Years.Length, Years = Years };
        var downtownText = (await promptService.BuildAsync(downtownScene, eras[Years[0]], foreground)).Text;
        var mediumTree = downtownScene.Environment.Trees
            .FirstOrDefault(t => t.Size.Equals("medium", StringComparison.OrdinalIgnoreCase));
        if (mediumTree is null)
            errs.Add("the comparison scene has no medium tree to measure the background rate against");
        else
        {
            var line = TreesSection(downtownText).Split('\n')
                .FirstOrDefault(l => l.StartsWith($"- {mediumTree.Type} tree at {mediumTree.Position}:", StringComparison.Ordinal));
            var m = line is null ? null : canopy.Match(line);
            if (m is null || !m.Success)
                errs.Add("could not read the kerbside medium tree's canopy percentage");
            else
            {
                // Backward path: a HIGHER remaining fraction means the tree
                // shrank less, i.e. it grows flatter.
                var kerbside = int.Parse(m.Groups[1].Value);
                if (firstEraPct > 0 && kerbside >= firstEraPct)
                    errs.Add($"a kerbside medium tree keeps {kerbside}% of its canopy in the first era and the " +
                             $"background one {firstEraPct}% — the background rate is not actually flatter");
            }
        }

        f.Add(("C70", $"A background tree grows on a flatter curve than a kerbside one and ends the run at no more than ~{ceilingPct}% of its first-era canopy, in both the chained and unchained paths",
            errs.Count == 0, errs.Count == 0
                ? $"background tree: first era {firstEraPct}% of base, {chainedTotal}% across the chained run ({string.Join(" ", steps)})"
                : Join(errs)));
    }

    // A motel flag is sign text with a date on it: putting a chain on the pylon in
    // a year it did not exist is the one error this data can produce, and it is
    // invisible unless the eligibility window is checked against the era actually
    // being rendered. Swept over seeds and driven through ResolveMotelSign
    // directly, so the result does not depend on what the two fixtures sampled.
    private static async Task DoC71(
        IDataService dataService,
        List<(string, string, bool?, string)> f)
    {
        var errs = new List<string>();

        IReadOnlyList<(string Name, int From, int To)> brands;
        try
        {
            brands = await dataService.LoadMotelBrandsAsync();
        }
        catch (Exception ex)
        {
            f.Add(("C71", "Every motel flag is a chain that existed in the era it is rendered in",
                false, $"LoadMotelBrandsAsync threw: {ex.Message}"));
            return;
        }

        if (brands.Count == 0)
            errs.Add("motel-brands.txt loaded no brands");
        foreach (var b in brands.Where(b => b.From > b.To))
            errs.Add($"\"{b.Name}\" has an inverted year window ({b.From}-{b.To})");
        foreach (var dupe in brands.GroupBy(b => b.Name, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1))
            errs.Add($"motel-brands.txt lists \"{dupe.Key}\" {dupe.Count()} times");

        const int seeds = 120;
        var flagged = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var reflaggedRuns = 0;

        for (var seed = 1; seed <= seeds; seed++)
        {
            var ctx = new GenerationContext
            {
                Random = new Random(seed), TotalEras = Years.Length, Years = Years
            };
            var perYear = new List<string?>();

            foreach (var year in Years)
            {
                ctx.BeginEra();
                var sign = ctx.ResolveMotelSign(brands, year, "thriving");

                if (sign.Kind != GenerationContext.MotelSignKind.Flagged)
                {
                    errs.Add($"seed {seed}/{year}: a trading motel rendered a stripped pylon");
                    continue;
                }
                if (sign.Brand is null)
                {
                    errs.Add($"seed {seed}/{year}: no flag resolved for a trading motel");
                    continue;
                }

                perYear.Add(sign.Brand);
                flagged.Add(sign.Brand);

                // The whole point: the chain has to have existed in this year.
                var match = brands.FirstOrDefault(b =>
                    string.Equals(b.Name, sign.Brand, StringComparison.OrdinalIgnoreCase));
                if (match.Name is null)
                    errs.Add($"seed {seed}/{year}: flag \"{sign.Brand}\" is not in motel-brands.txt");
                else if (year < match.From || year > match.To)
                    errs.Add($"seed {seed}/{year}: \"{sign.Brand}\" ran {match.From}-{match.To} — wrong era");
            }

            if (perYear.Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1)
                reflaggedRuns++;
        }

        // A dead motel shows the stripped frame regardless of the plan.
        foreach (var condition in new[] { "abandoned", "squatted" })
        {
            var ctx = new GenerationContext
            {
                Random = new Random(7), TotalEras = Years.Length, Years = Years
            };
            ctx.BeginEra();
            var sign = ctx.ResolveMotelSign(brands, 2015, condition);
            if (sign.Kind != GenerationContext.MotelSignKind.DeadBoard || sign.Brand is not null)
                errs.Add($"condition '{condition}' still hangs a lit flag on the pylon");
        }

        // The timeline has to actually move: a motel that never reflags across
        // fifty years means the plan collapsed to one brand and the era windows
        // are doing nothing.
        var reflagRate = reflaggedRuns * 1.0 / seeds;
        if (reflagRate < 0.30)
            errs.Add($"only {reflagRate:P0} of runs ever reflag — the brand timeline is not moving");

        // And the pool has to be wide enough to be worth having.
        if (flagged.Count < 8)
            errs.Add($"only {flagged.Count} distinct flags across {seeds} seeds");

        f.Add(("C71", "Every motel flag is a chain that existed in the era it is rendered in, a derelict motel shows a stripped pylon instead, and the flag actually changes across a run",
            errs.Count == 0, errs.Count == 0
                ? $"{brands.Count} chains, {flagged.Count} distinct flags across {seeds} seeds, {reflagRate:P0} of runs reflag, every flag inside its own year window"
                : Join(errs)));
    }

    // The Meta line is a rewrite of a finished prompt, so it inherits everything
    // the builder decided and only restates it. What it has to guarantee is the
    // part the builder is free to ignore: no person paired with alcohol or with
    // nowhere to be, no count Meta will not honour, and the sign that dates the
    // frame still present. Run over the real prompts of every scene type,
    // because the wording it has to remove is generated, not written by hand.
    private static void DoC73(
        IReadOnlyList<(string Label, Dictionary<int, Prompt> Run)> runs,
        Prompt unknownPrompt,
        List<(string, string, bool?, string)> f)
    {
        var errs = new List<string>();

        // Everything that reads as a person drinking or with nowhere to be. The
        // trade itself is fine — a liquor store is a shop, and its sign is the
        // whole point of the corner-shop arc — so shop nouns are not listed.
        string[] banned =
        {
            "bottle in a paper bag", "bottle in a black plastic bag", "sharing a bottle",
            "already drinking", "drink out of sight", "nothing to do",
            "nobody lives here", "no tents", "bedding", "shopping carts",
            "regulars of this store", "out of the wind", "squatted",
        };

        var rewritten = 0;
        var all = runs
            .SelectMany(r => r.Run.Select(kv => ($"{r.Label}/{kv.Key}", kv.Value)))
            .Append(("unknown", unknownPrompt))
            .ToList();

        foreach (var (where, prompt) in all)
            {
                var meta = ShortPromptWriter.Rewrite(prompt.Text);
                rewritten++;

                foreach (var phrase in banned)
                    if (meta.Contains(phrase, StringComparison.OrdinalIgnoreCase))
                        errs.Add($"{where}: rewritten prompt still contains \"{phrase}\"");

                // Above three, an exact count comes back as something else, so
                // the rewrite must have turned it into a small-group phrase.
                var m = System.Text.RegularExpressions.Regex.Match(meta, @"^(\d+) people,", System.Text.RegularExpressions.RegexOptions.Multiline);
                if (m.Success && int.Parse(m.Groups[1].Value) > 3)
                    errs.Add($"{where}: rewritten prompt still asks for {m.Groups[1].Value} people by count");

                var vehicles = System.Text.RegularExpressions.Regex.Match(meta, @"EXACTLY (\d+) vehicle");
                if (vehicles.Success && int.Parse(vehicles.Groups[1].Value) > 2)
                    errs.Add($"{where}: rewritten prompt lists {vehicles.Groups[1].Value} vehicles (max 2)");

                // Structure the hand user depends on.
                foreach (var heading in new[] { "TRANSFORM TO", "PEOPLE", "VEHICLES" })
                    if (!meta.Contains(heading, StringComparison.Ordinal))
                        errs.Add($"{where}: rewritten prompt lost the {heading} section");

                // The sign dates the frame; losing it to a word filter is the
                // failure this guards, and it happened once already.
                if (prompt.Text.Contains("- main sign:", StringComparison.Ordinal)
                    && !meta.Contains("main sign", StringComparison.Ordinal))
                    errs.Add($"{where}: the main sign was filtered out of the rewrite");

                // The whole reason this line exists. A rewrite that grew would
                // mean the section builders had started adding rather than
                // restating, which is how the two lines drift apart.
                if (meta.Length >= prompt.Text.Length)
                    errs.Add($"{where}: the short prompt is not shorter ({meta.Length} vs {prompt.Text.Length} chars)");
            }

        f.Add(("C73", "The Meta rewrite of every era prompt drops the alcohol and nowhere-to-be wording, holds people to a small group and vehicles to two, and keeps the main sign",
            errs.Count == 0, errs.Count == 0
                ? $"{rewritten} prompts rewritten clean across every scene type"
                : Join(errs.Take(5))));
    }

    // Ten hand-written fixtures test ten shapes, and every rule in the builder
    // has been written against those same ten. What they do not contain is the
    // shape nobody thought of: a corner shop with no trees, a strip mall with a
    // sidewalk, a highway with an empty buildings array. SceneDnaFactory makes
    // those from a seed with no photo and no Vision call, and this walks a fresh
    // scene for every type across several seeds looking only for the failures a
    // fixture cannot show — a throw, a blown budget, a placeholder left in.
    //
    // It doubles as the standing proof that a synthetic base needs no Vision at
    // all: every prompt here, base included, is built from generated text.
    private static async Task DoC74(
        IPromptService promptService,
        Dictionary<int, EraProfile> eras,
        ILogger logger,
        List<(string, string, bool?, string)> f)
    {
        var errs = new List<string>();
        const int seedsPerType = 6;
        const int maxWords = 920;

        int WordCount(string t) =>
            t.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
             .Count(x => x.Any(char.IsLetterOrDigit));

        var built = 0;
        var noTrees = 0;
        var noBuildings = 0;

        foreach (var sceneType in SceneDnaFactory.SceneTypes)
            for (var seed = 0; seed < seedsPerType; seed++)
            {
                SceneDna scene;
                try
                {
                    scene = SceneDnaFactory.Create(sceneType, seed);
                }
                catch (Exception ex)
                {
                    errs.Add($"{sceneType}/seed {seed}: generating the scene threw: {ex.Message}");
                    continue;
                }

                if (scene.Environment.Trees.Count == 0) noTrees++;
                if (scene.Geometry.Buildings.Count == 0) noBuildings++;

                // The synthetic base is the whole point: no photo went into it.
                try
                {
                    var basePrompt = await promptService.BuildBaseAsync(scene, Years[0]);
                    if (basePrompt.Contains('{') || basePrompt.Contains('}'))
                        errs.Add($"{sceneType}/seed {seed}: base prompt has an unsubstituted placeholder");
                    if (basePrompt.Length < 200)
                        errs.Add($"{sceneType}/seed {seed}: base prompt is only {basePrompt.Length} chars");
                }
                catch (Exception ex)
                {
                    errs.Add($"{sceneType}/seed {seed}: BuildBaseAsync threw: {ex.Message}");
                    continue;
                }

                var ctx = new GenerationContext
                    { Random = new Random(seed), TotalEras = Years.Length, Years = Years };
                foreach (var year in Years)
                {
                    try
                    {
                        var prompt = await promptService.BuildAsync(scene, eras[year], ctx);
                        built++;

                        var where = $"{sceneType}/seed {seed}/{year}";
                        if (prompt.Text.Contains('{') || prompt.Text.Contains('}'))
                            errs.Add($"{where}: unsubstituted placeholder in the prompt");
                        if (prompt.Text.Length > MaxPromptChars)
                            errs.Add($"{where}: {prompt.Text.Length} chars (max {MaxPromptChars})");
                        if (WordCount(prompt.Text) >= maxWords)
                        {
                            errs.Add($"{where}: {WordCount(prompt.Text)} words (limit {maxWords})");
                            // Written out because a generated scene cannot be
                            // reproduced by opening a fixture — the seed is the
                            // only handle on it, and the prompt is what shows
                            // which combination got long.
                            var dump = Path.Combine("output", "smoke", "oversize");
                            Directory.CreateDirectory(dump);
                            await File.WriteAllTextAsync(
                                Path.Combine(dump, $"{sceneType}-seed{seed}-{year}.txt"), prompt.Text);
                        }

                        // The short line has to survive an unfamiliar shape too.
                        var shortForm = ShortPromptWriter.Rewrite(prompt.Text);
                        if (shortForm.Length >= prompt.Text.Length)
                            errs.Add($"{where}: the short prompt is not shorter");
                    }
                    catch (Exception ex)
                    {
                        errs.Add($"{sceneType}/seed {seed}/{year}: BuildAsync threw: {ex.Message}");
                    }
                }

                if (errs.Count > 12) break;   // the first dozen say what is wrong
            }

        // Reported because a generator that stopped producing these would make
        // the check pass while testing nothing new.
        logger.LogInformation(
            "[Smoke] C74 generated scenes: {Built} prompts over {Types} scene types x {Seeds} seeds; " +
            "{NoTrees} scenes with no trees, {NoBuildings} with no buildings",
            built, SceneDnaFactory.SceneTypes.Count, seedsPerType, noTrees, noBuildings);

        if (noTrees == 0)
            errs.Add("no generated scene came out without trees — the tree-free path is untested");
        if (noBuildings == 0)
            errs.Add("no generated scene came out without buildings — the open-road path is untested");

        f.Add(("C74", $"Prompts and a synthetic base build from generated SceneDna for every scene type over {seedsPerType} seeds, with no photo and no Vision call, inside the same budgets",
            errs.Count == 0, errs.Count == 0
                ? $"{built} prompts from generated scenes; {noTrees} tree-free and {noBuildings} building-free shapes covered"
                : Join(errs.Take(6))));
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
