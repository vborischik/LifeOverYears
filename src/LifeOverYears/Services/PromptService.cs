using System.Text;
using LifeOverYears.Models;
using LifeOverYears.Services.Interfaces;
using Microsoft.Extensions.Logging;

namespace LifeOverYears.Services;

public sealed class PromptService : IPromptService
{
    // Cap on the doubled first-era crowd: "EXACTLY N people" stops being
    // followed reliably by the image model much above this.
    private const int FirstEraPeopleCap = 30;

    // Short-form PRESERVE for the era path. BuildAsync always edits an existing
    // image — base_synthetic.png in synthetic mode, the cleaned photo in clean
    // mode — so the geometry is already in the pixels, and restating it in text
    // competes with the image rather than reinforcing it.
    private const string ShortPreserveBlock = """
        PRESERVE
        Keep the uploaded image's composition, camera angle, geometry, buildings,
        roads and permanent structures exactly as they are. Do not redraw,
        reposition or restyle them. Change only what the sections below ask for.
        """;

    // The uploaded image is empty when every era edits the shared base, but
    // already populated when eras are chained — the previous year's people and
    // vehicles are still in it and must be cleared before this year's are placed.
    private const string FreshBaseNote =
        "Use the uploaded photo as the exact base composition. The street in the source\n" +
        "is empty — populate it with the people and vehicles specified below.";

    private const string ChainedBaseNote =
        "Use the uploaded photo as the exact base composition. It shows this same place in\n" +
        "an earlier year: first remove EVERY person, vehicle and bicycle already in it, so\n" +
        "the street is empty, then populate it with the people and vehicles specified below.\n" +
        "Keep none of the earlier year's figures or traffic.";

    private readonly IDataService _data;
    private readonly ILogger<PromptService> _logger;

    public PromptService(IDataService data, ILogger<PromptService> logger)
    {
        _data = data;
        _logger = logger;
    }

    public async Task<Prompt> BuildAsync(SceneDna sceneDna, EraProfile eraProfile, GenerationContext context)
    {
        _logger.LogInformation("Building prompt for SceneDna {Id}, year {Year}", sceneDna.Id, eraProfile.Year);

        var template = await _data.LoadPromptAsync("image-template");
        var rng      = context.Random;
        var year     = eraProfile.Year;
        var eraIndex = context.BeginEra();

        var sceneType    = sceneDna.SceneType ?? "default";
        var sceneContent = ResolveSceneContent(eraProfile, sceneType);
        if (sceneContent is null)
            _logger.LogWarning("No scene_content in era {Year} for scene type '{SceneType}' — building generic scene block",
                year, sceneType);

        var isGasStation    = sceneType == "gas_station";
        var hasSidewalks    = sceneDna.Geometry.Sidewalks;
        var onStreetParking = IsOnStreetParking(sceneDna.Geometry.Parking);
        var supportsCondition = SupportsCondition(sceneType);

        var condition = supportsCondition
            ? context.PickSceneCondition(eraProfile.AllowedSceneConditions, sceneType)
            : "thriving";
        _logger.LogInformation(
            "Scene condition for SceneDna {Id}, year {Year} (era {Index}): {Condition}",
            sceneDna.Id, year, eraIndex, condition);

        // "packed" scenes (the enclosed mall) render a dense, uncountable crowd
        // and a full lot instead of an exact count — People/Vehicles ranges are
        // not even present in their era JSON, so no count is derived from them
        // and the first-era doubling below does not apply.
        var isPacked = sceneContent?.Crowd == "packed";

        int peopleCount;
        int vehicleCount;
        if (isPacked)
        {
            peopleCount  = 0;
            vehicleCount = 0;
        }
        else
        {
            var peopleRange  = sceneContent?.People   ?? new CountRange(10, 15);
            var vehicleRange = sceneContent?.Vehicles ?? new CountRange(4, 6);
            peopleCount  = rng.Next(peopleRange.Min, peopleRange.Max + 1);
            vehicleCount = rng.Next(vehicleRange.Min, vehicleRange.Max + 1);

            // The opening era sets the "before" that everything after is measured
            // against; a sparse street there reads as empty rather than nostalgic.
            if (context.IsFirstEra)
                peopleCount = Math.Min(peopleCount * 2, FirstEraPeopleCap);
        }

        if (supportsCondition && condition == "abandoned")
        {
            peopleCount  = 0;
            vehicleCount = 0;
        }
        else if (supportsCondition && condition == "squatted")
        {
            // The two drinkers, plus one passer-by who is only walking through.
            peopleCount  = 3;
            vehicleCount = rng.Next(0, 2);   // nothing, or one long-dead wreck
        }
        else if (supportsCondition && condition == "declining")
        {
            peopleCount  = rng.Next(2, 5);
            vehicleCount = rng.Next(1, 3);
        }

        // Packed mode only needs a handful of representative models named up
        // close — pulling a full vehicleCount would exhaust the era's pool for
        // no visual benefit, since the rest of the lot is described, not listed.
        // A squatted era has no working traffic: anything on the lot is a wreck
        // dumped years ago, described rather than drawn from the era's model pool
        // (those are current, running cars — exactly what must not appear here).
        var isSquatted    = supportsCondition && condition == "squatted";
        var derelictCount = isSquatted ? vehicleCount : 0;

        var vehicles = isSquatted
            ? new List<(string Model, string? Color)>()
            : PickVehicles(eraProfile, context, isPacked ? 5 : vehicleCount, _logger);
        // An abandoned era has no vehicles and no PLACEMENT line — don't consume a
        // placement pattern from the run's pool for it. A packed lot has no gaps
        // to arrange either, so it skips placement the same way.
        var placement = !isPacked && vehicles.Count > 0 ? context.NextPlacement(vehicles.Count, onStreetParking) : "";
        var gasSign   = isGasStation ? await ResolveGasSignAsync(context, year, condition) : default;

        var sceneBlock = BuildSceneBlock(eraProfile, sceneContent, sceneType, condition, gasSign, rng, context);
        sceneBlock = AppendSignageRestriction(sceneBlock);

        var text = template
            // EXPERIMENT: the generated per-era geometry block is parked while the
            // short fixed form is evaluated. To restore it, uncomment the line
            // below and drop the ShortPreserveBlock line. BuildPreserveBlock and
            // its includeTrees path stay live — BuildBaseAsync still needs them.
            // .Replace("{PRESERVE_BLOCK}",    BuildPreserveBlock(sceneDna, "PRESERVE (must match source exactly)", "Keep this location instantly recognizable.", includeTrees: false))
            .Replace("{BASE_NOTE}",         context.ChainedFromPreviousEra ? ChainedBaseNote : FreshBaseNote)
            .Replace("{PRESERVE_BLOCK}",    ShortPreserveBlock)
            .Replace("{SCENE_BLOCK}",       sceneBlock)
            .Replace("{PEOPLE_BLOCK}",      BuildPeopleBlock(eraProfile, sceneContent, peopleCount, isGasStation, hasSidewalks, rng, context, isPacked, isSquatted, IsDecliningRetail(condition, sceneType)))
            .Replace("{VEHICLES_BLOCK}",    BuildVehiclesBlock(vehicles, year, placement, isGasStation, onStreetParking, isPacked, derelictCount))
            .Replace("{ENVIRONMENT_BLOCK}", BuildEnvironmentBlock(sceneDna, eraProfile, year, sceneType, condition, rng))
            .Replace("{STYLE_BLOCK}",       BuildStyleBlock(eraProfile.Photography, condition));

        // Scene content refers to recurring businesses (diner, drug store, etc.) by
        // token so the same name persists across every era of a run; resolve them
        // last, over the fully assembled text, so tokens landing in any era JSON
        // field (storefronts, window_signs, extras, people_activities, narrative)
        // are covered regardless of which block builder emitted them.
        foreach (var (token, name) in context.BusinessNameTokens())
            text = text.Replace(token, name);

        return new Prompt(
            Id:               Guid.NewGuid().ToString(),
            SceneDnaId:       sceneDna.Id,
            Year:             year,
            Text:             text,
            SelectedVehicles: vehicles.Select(v => v.Model).ToList(),
            CreatedAt:        DateTimeOffset.UtcNow.ToString("o"),
            SceneCondition:   condition);
    }

    // Synthetic base: the scene built from SceneDna text alone, with no source
    // photo anywhere in the request. Same geometry rundown as an era prompt, but
    // framed as construction rather than preservation.
    public async Task<string> BuildBaseAsync(SceneDna sceneDna, int baseYear)
    {
        _logger.LogInformation("Building synthetic base prompt for SceneDna {Id}, base year {Year}",
            sceneDna.Id, baseYear);

        var template = await _data.LoadPromptAsync("base-synthetic");

        // The base builds geometry from nothing, so it is the one prompt that has
        // to say what kind of place this is. Without it the model sees a generic
        // street and has to infer the type from the immutable elements — which
        // describe a canopy or a pylon without ever naming a station.
        const string fallbackKey = "unknown";
        var phrases   = await _data.LoadSceneTypePhrasesAsync();
        var sceneType = sceneDna.SceneType;
        var usedKey   = !string.IsNullOrWhiteSpace(sceneType) && phrases.ContainsKey(sceneType)
            ? sceneType
            : fallbackKey;

        if (!phrases.TryGetValue(usedKey, out var phrase))
            throw new InvalidOperationException(
                $"scene-types.txt has no '{fallbackKey}' entry to fall back to (scene type '{sceneType}')");

        if (!string.Equals(usedKey, sceneType, StringComparison.OrdinalIgnoreCase))
            _logger.LogInformation(
                "Synthetic base: scene type '{SceneType}' has no scene-types.txt entry — using '{Key}'",
                sceneType ?? "(none)", usedKey);
        else
            _logger.LogInformation("Synthetic base: using scene type phrase '{Key}'", usedKey);

        // Undated, the base renders as present-day — fresh asphalt, modern poles,
        // current storefront systems — and every early era then has to undo that.
        // The run's years are known up front, so build it in the earliest one and
        // let the eras age it forward.
        var baseEra = await _data.LoadEraProfileAsync(baseYear);

        var text = template
            .Replace("{SCENE_TYPE_PHRASE}", phrase)
            .Replace("{PERIOD_BLOCK}",      BuildBasePeriodBlock(baseEra))
            .Replace("{GEOMETRY_BLOCK}",
                BuildPreserveBlock(sceneDna, "BUILD THIS SCENE", "", includeTrees: true));

        _logger.LogInformation("Synthetic base prompt built: id={Id} length={Length}",
            sceneDna.Id, text.Length);
        return text;
    }

    // Period for the synthetic base, taken from the base year's era profile rather
    // than hardcoded, so the run's own years drive it. Only permanent fabric is
    // described: the base carries no people, vehicles or signage to date.
    private static string BuildBasePeriodBlock(EraProfile era)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"PERIOD — build the scene as it stood in {era.Year}");
        sb.AppendLine($"Everything permanent must be plausible for {era.Year}; nothing may look newer.");
        sb.AppendLine($"- commercial architecture: {Join(era.Architecture.Commercial.Styles.Take(2).ToList())}");
        sb.AppendLine($"- building materials: {Join(era.Architecture.Commercial.Materials.Take(3).ToList())}");
        sb.AppendLine($"- road surface: {Join(era.Infrastructure.Roads.Materials.Take(2).ToList())}");
        sb.AppendLine($"- road markings: {Join(era.Infrastructure.Roads.Markings.Take(3).ToList())}");
        sb.AppendLine($"- utilities: {Join(era.Infrastructure.Utilities.Characteristics.Take(2).ToList())}");
        sb.Append("No later-era construction: no LED fixtures, no aluminium-and-glass curtain wall, no modern bollards or plastic sign boxes.");
        return sb.ToString();
    }

    // Run-wide gas-station sign spec: one brand held across the run (with at most
    // one rebrand), a dead stripped board when abandoned/squatted, and a fresh
    // brand on a rebuilt finale. Brand-timeline state lives on GenerationContext.
    private async Task<GenerationContext.GasSign> ResolveGasSignAsync(
        GenerationContext context, int year, string condition)
    {
        try
        {
            var brands = await _data.LoadGasBrandsAsync();
            return context.ResolveGasSign(brands, year, condition);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not load gas brands file — falling back to era JSON gas_brands");
            // Null brand → BuildSceneBlock falls back to an independent-style sign.
            return new GenerationContext.GasSign(GenerationContext.GasSignKind.Branded, null);
        }
    }

    // Vision writes Parking as free text ("parallel street parking both sides",
    // "gravel lot", "concrete apron in front of the bays"). Anything that does not
    // name the street or the curb is treated as off-street: forecourts, aprons and
    // lots are the common case and must not inherit parallel-parking language.
    private static bool IsOnStreetParking(string? parking)
    {
        if (string.IsNullOrWhiteSpace(parking)) return false;
        var p = parking.ToLowerInvariant();
        return p.Contains("street") || p.Contains("curb")
            || p.Contains("parallel") || p.Contains("meter");
    }

    private static SceneContent? ResolveSceneContent(EraProfile era, string sceneType)
    {
        if (era.SceneContent is null)
            return null;
        if (era.SceneContent.TryGetValue(sceneType, out var content))
            return content;
        return era.SceneContent.TryGetValue("default", out var fallback) ? fallback : null;
    }

    // The geometry rundown is identical whether we are telling the model to
    // preserve an existing photo or to build the scene from nothing; only the
    // framing differs, so header and closing are supplied by the caller. A
    // closing of "" omits the trailing line entirely.
    //
    // includeTrees gates whether this block states each tree's size on its own:
    // the era PRESERVE block (BuildAsync) passes false, because the TREES
    // section already gives that tree's size for the target era — a "must
    // match source exactly" claim here would freeze it at the source photo's
    // size and contradict a TREES line rendering it smaller for an earlier
    // decade. The synthetic base (BuildBaseAsync) passes true: there is no
    // photo and no separate TREES section for it, so this is the only place
    // the trees get described at all.
    private static string BuildPreserveBlock(SceneDna s, string header, string closing, bool includeTrees)
    {
        var sb = new StringBuilder();
        sb.AppendLine(header);
        // Some SceneDna directions already read "…-facing"; avoid "facing street-facing".
        var facingClause = s.Camera.Direction.Contains("facing", StringComparison.OrdinalIgnoreCase)
            ? s.Camera.Direction
            : $"facing {s.Camera.Direction}";
        sb.AppendLine($"- camera: {s.Camera.Height}, {facingClause}, fov {s.Camera.Fov}");
        if (s.Composition is { } comp)
            sb.AppendLine($"- framing: subject at {comp.SubjectDistance} range, filling a {comp.FrameShare} share of the frame, horizon {comp.Horizon}");
        foreach (var r in s.Geometry.Roads)
            sb.AppendLine($"- {r.Type} road, {r.Lanes}-lane, {r.Surface}");
        sb.AppendLine($"- sidewalks {(s.Geometry.Sidewalks ? "present" : "absent")}, curbs {(s.Geometry.Curbs ? "present" : "absent")}");
        if (s.Geometry.Driveways.Count > 0 || !string.IsNullOrWhiteSpace(s.Geometry.Parking))
        {
            var parts = new List<string>();
            if (s.Geometry.Driveways.Count > 0)
                parts.Add($"driveways: {string.Join(", ", s.Geometry.Driveways)}");
            if (!string.IsNullOrWhiteSpace(s.Geometry.Parking))
                parts.Add($"parking: {s.Geometry.Parking}");
            sb.AppendLine($"- {string.Join("; ", parts)}");
        }
        foreach (var b in s.Geometry.Buildings)
            sb.AppendLine($"- {b.Type} building at {b.Position}, {b.Stories} {(b.Stories == 1 ? "story" : "stories")}, {Join(b.Materials)}, {b.Roof} roof, {b.Setback} setback");
        if (s.Environment.Utilities.Count > 0)
            sb.AppendLine($"- utilities: {string.Join(", ", s.Environment.Utilities)} — keep their exact positions and geometry, but render them as period-appropriate infrastructure for the target year");
        var landscape = includeTrees ? s.Environment.Landscape : DropTreeMentions(s.Environment.Landscape);
        if (landscape.Count > 0)
            sb.AppendLine($"- landscape: {string.Join(", ", landscape)}");
        if (includeTrees)
            foreach (var tree in s.Environment.Trees)
                sb.AppendLine($"- {tree.Type} tree at {tree.Position}, {tree.Size} size");
        var immutable = CleanImmutableElements(s.ImmutableElements);
        if (!includeTrees) immutable = DropTreeMentions(immutable);
        if (immutable.Count > 0)
            sb.AppendLine($"- immutable elements: {string.Join(", ", immutable)}");
        var distinctive = s.Distinctive ?? [];
        if (distinctive.Count > 0)
            sb.AppendLine($"- specific to this exact place, reproduce faithfully: {string.Join("; ", distinctive)}");
        if (closing.Length > 0)
        {
            sb.Append(closing);
            return sb.ToString();
        }
        // No closing line: drop the trailing newline so the block does not
        // inject a blank line where it is substituted.
        return sb.ToString().TrimEnd('\r', '\n');
    }

    // Vision writes Landscape and ImmutableElements as free text, so a tree could
    // plausibly turn up there too ("mature oak tree", "tree pits") rather than
    // only in the structured Trees array — drop any such mention when trees are
    // excluded, so the TREES section stays the one place that states a tree's
    // condition. Word-boundary match: a naive substring check false-positives
    // on "street" (which contains "tree").
    private static List<string> DropTreeMentions(IReadOnlyList<string> elements) =>
        elements.Where(e => !System.Text.RegularExpressions.Regex.IsMatch(e, @"\btrees?\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase)).ToList();

    // Vision output sometimes embeds its own label ("permanent landmarks: none") inside
    // an element — strip it so the PRESERVE line carries a single label, and drop
    // none/empty values entirely.
    private static List<string> CleanImmutableElements(IReadOnlyList<string> elements)
    {
        const string prefix = "permanent landmarks:";
        return elements
            .Select(e =>
            {
                var v = e.Trim();
                if (v.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    v = v[prefix.Length..].Trim();
                return v;
            })
            .Where(v => v.Length > 0 && !v.Equals("none", StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    // Concepts that overlap between scene_content storefronts and era gas station
    // characteristics — at most one sampled line per concept.
    private static readonly string[] DetailConcepts =
    {
        "price sign", "pole sign", "service bay", "pump", "canopy", "oil can", "convenience store", "vending", "tire"
    };

    internal static string StripRequiredMarker(string item) =>
        System.Text.RegularExpressions.Regex.Replace(item, @"\s*[—–-]\s*REQUIRED.*$", "");

    private static string TruncateToSentences(string text, int maxSentences)
    {
        var sentences = text.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (sentences.Length <= maxSentences)
            return text;
        return string.Join(". ", sentences.Take(maxSentences)) + ".";
    }

    private static string? ConceptOf(string item)
    {
        var normalized = item.Replace("-", " ").ToLowerInvariant();
        return DetailConcepts.FirstOrDefault(normalized.Contains);
    }

    // Short appearance/upkeep descriptor per sampled scene condition. Affects how
    // the place looks, never its geometry (kept out of the PRESERVE block).
    private static string ConditionDescriptor(string condition) => condition switch
    {
        "thriving"  => "well-maintained, freshly painted, active business, clean lot",
        "busy"      => "customers present, high activity, all pumps in use",
        "new"       => "recently built appearance, pristine surfaces, new signage",
        "declining" => "faded paint, minor wear, aging signage, sparse activity",
        "abandoned" => "closed business, boarded windows, weeds through pavement cracks, weathered surfaces",
        // No shelters, carts or belongings: nobody lives here. The place is dead
        // and someone has just walked in to drink out of sight.
        "squatted"  => "closed and derelict, boarded entrance, weeds through the pavement, litter blown against the kerb",
        "restored"  => "renovated appearance, modern updates on original structure",
        _           => "well-maintained, freshly painted, active business, clean lot"
    };

    // A "new" condition means pristine surfaces — but the era data's ghost-sign
    // extra (an old painted wall ad, "weathered but still clearly readable")
    // exists to give even a freshly built era some history, and left as-is it
    // just contradicts "new" outright with no relationship stated. Swap it for
    // an explicit reconciling line instead of leaving both claims side by side.
    private static string ReconcileWithNewCondition(string extra, string condition)
    {
        if (condition != "new") return extra;
        if (!extra.Contains("ghost-sign", StringComparison.OrdinalIgnoreCase) &&
            !extra.Contains("ghost sign", StringComparison.OrdinalIgnoreCase))
            return extra;
        return "one pre-existing older brick wall carries a weathered ghost sign; all other buildings and storefronts look newly built or recently renovated";
    }

    // Physical wear that sells a place as run-down. Surfaces, finishes and
    // litter only — never road width, curb lines, building footprints or
    // camera, which stay fixed by the PRESERVE block. Visibility is internal
    // (not private) so the smoke suite can verify pool membership directly —
    // same pattern as StripRequiredMarker below.
    internal static readonly string[] DecayModerate =
    {
        "mismatched asphalt patches, cracks at the joints",
        "chalky faded lane paint, ghost lines in the tracks",
        "chipped, stained curbs, weeds in the gutter",
        "faded storefront paint, one window papered over",
        "sun-bleached signage, a burnt-out sign letter",
        "curb litter, an overflowing trash can"
    };

    internal static readonly string[] DecayHeavy =
    {
        "potholes, alligator cracking, gravel at the edges",
        "lane markings nearly gone, faint paint traces",
        "weeds through pavement cracks, the curb",
        "boarded windows, glass cracked or missing",
        "rusted signage askew, lettering broken off",
        "graffiti on boarded panels, lower wall",
        "trash along storefronts, a dumped mattress at the curb"
    };

    // A main street decays differently from a forecourt: the tells are shutters,
    // painted-over sign bands and ghost signs on brick, with upper floors often
    // still lived in above dead ground-floor storefronts.
    internal static readonly string[] DowntownDecayModerate =
    {
        "a faded painted wall ad showing through the brick",
        "one storefront papered over from inside, a handwritten sign taped to the glass",
        "a sun-bleached awning, torn at one corner",
        "peeling paint along the storefront cornice",
        "an old sign band painted over, the previous lettering still faintly readable",
        "cracked sidewalk squares, weeds at the tree pits"
    };

    internal static readonly string[] DowntownDecayHeavy =
    {
        "roll-down steel security shutters closed over the storefronts",
        "an accordion scissor gate padlocked across a doorway",
        "a sign band painted flat white, all lettering stripped off",
        "a ghost sign for a long-gone business bleeding through the brickwork",
        "graffiti tags across the lower brick and boarded panels",
        "upper-floor windows still curtained above dead ground-floor storefronts",
        "a torn vinyl banner sagging from the facade",
        "rust streaks below the old cornice bolts",
        "a collapsed awning frame hanging loose over the sidewalk",
        "plywood over a broken transom window, the glass long gone"
    };

    // A strip mall decays by losing tenants: blank sign panels, FOR RENT glass
    // and a downward tenant mix, with the parking apron going to seed.
    internal static readonly string[] StripMallDecayModerate =
    {
        "two blank white sign panels where tenants moved out",
        "a FOR RENT sign taped inside a dark storefront window",
        "faded mansard shingles patched in mismatched colors",
        "paper flyers taped inside the glass of one unit",
        "parking stripes worn down to faint outlines",
        "weeds along the base of the storefront walkway"
    };

    internal static readonly string[] StripMallDecayHeavy =
    {
        "every sign panel blank or removed, empty frames left on the pylon",
        "storefronts boarded with plywood behind bent security grilles",
        "grass and saplings pushing up through the parking apron",
        "the pylon sign stripped back to a rusted frame",
        "shopping carts abandoned across the empty lot",
        "shattered storefront glass swept into the walkway corners"
    };

    // An independent auto repair shop decays through its own working surfaces:
    // the bay doors, the sign band, the apron — never geometry, and never a
    // feature (pump island, canopy) that might not exist in a given SceneDna.
    internal static readonly string[] AutoRepairDecayModerate =
    {
        "one service bay door rolled down while the other stays open",
        "oil stains spreading dark across the concrete apron",
        "a stack of worn tires leaning against the side wall",
        "the painted sign band faded, letters flaking at the edges",
        "a hand-lettered service sign taped inside the office glass",
        "weeds at the apron edges and along the base of the building"
    };

    internal static readonly string[] AutoRepairDecayHeavy =
    {
        "every bay door rolled down, dented and streaked with rust",
        "plywood over the office plate glass",
        "graffiti tagged across the painted sign band",
        "roofing material torn loose along the parapet, debris at the roof edge",
        "weeds and grass pushing through cracks in the concrete apron",
        "a wrecked car with its hood off left sitting at a bay door"
    };

    // Pool selection lives here (not at the call site) so the smoke test can
    // assert against exactly the pool the builder would have used.
    internal static string[]? DecayPoolFor(string sceneType, string condition) => condition switch
    {
        "declining" => sceneType switch
        {
            "downtown_street" => DowntownDecayModerate,
            "strip_mall"      => StripMallDecayModerate,
            "auto_repair"     => AutoRepairDecayModerate,
            _                 => DecayModerate
        },
        "abandoned" or "squatted" => sceneType switch
        {
            "downtown_street" => DowntownDecayHeavy,
            "strip_mall"      => StripMallDecayHeavy,
            "auto_repair"     => AutoRepairDecayHeavy,
            _                 => DecayHeavy
        },
        _ => null
    };

    // Two or three concrete details per era, sampled so consecutive eras of the
    // same run don't repeat the same wording.
    // Gas stations, downtown streets, strip malls and auto repair shops all
    // carry a condition arc — decline is the story these runs are for. Only
    // default/unknown scenes stay thriving and use their base ranges untouched.
    // Single source of truth: the count logic and the CONDITION line both ask
    // here, so they cannot drift apart.
    private static bool SupportsCondition(string sceneType) =>
        sceneType is "gas_station" or "downtown_street" or "strip_mall" or "auto_repair";

    // A retail row that is failing but not yet dead — the only state where a
    // surviving tenant makes sense. Squatted and abandoned stay fully closed.
    private static bool IsDecliningRetail(string condition, string sceneType) =>
        condition == "declining" && sceneType is "downtown_street" or "strip_mall";

    private static string BuildDecayBlock(string condition, string sceneType, Random rng)
    {
        var pool = DecayPoolFor(sceneType, condition);
        if (pool is null)
            return "";

        var picks = Sample(pool, condition == "declining" ? 2 : 3, rng);
        var sb = new StringBuilder();
        sb.AppendLine();
        sb.AppendLine("DECAY");
        foreach (var p in picks)
            sb.AppendLine($"- {p}");
        sb.Append("Wear is surfaces only — road, curbs, footprints, camera unchanged.");
        return sb.ToString().TrimEnd();
    }

    private static string BuildSceneBlock(EraProfile era, SceneContent? content, string sceneType, string condition, GenerationContext.GasSign gasSign, Random rng, GenerationContext context)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"TRANSFORM TO {era.Year}");
        var intro = TruncateToSentences(era.Description, 1);
        sb.AppendLine(content is null ? intro : $"{intro} {content.Narrative}");

        var isGasStation = sceneType == "gas_station";

        // Scene atmosphere — condition affects appearance/upkeep only, never the
        // physical geometry in the PRESERVE block.
        if (SupportsCondition(sceneType))
        {
            sb.AppendLine();
            sb.AppendLine($"CONDITION: {condition} — {ConditionDescriptor(condition)}");
        }

        sb.AppendLine();
        sb.AppendLine("PERIOD DETAILS");

        // A closed-down block must not advertise. The era's scene_content lists
        // live businesses, promos and street props; emitting them alongside
        // "CONDITION: abandoned" is a direct contradiction, and the image model
        // resolves it by rendering the businesses. Derelict eras get their own
        // short block instead, and skip window signs and extras entirely.
        if (condition is "abandoned" or "squatted")
        {
            sb.AppendLine("- every storefront closed and dark — no lit signs, no menu boards, no open businesses");
            sb.AppendLine("- faded remains of old signage still on the facade, lettering weathered and partly missing");
            sb.AppendLine("- plywood over the ground-floor windows, doors chained shut");
            if (isGasStation)
                sb.AppendLine("- main sign: a bare stripped sign frame — rusted metal posts, an empty price panel with no digits, no lit letters, no logo, all branding gone");
            // A ghost sign is exactly the remnant this branch already describes —
            // only Ghost belongs here. Named/Generic would be live business, which
            // is what this branch exists to keep out of a derelict scene.
            foreach (var tenant in context.ResolveChainTenants(era.Year, sceneType, condition))
                if (tenant.Kind == GenerationContext.ChainSignKind.Ghost)
                    sb.AppendLine($"- {tenant.Text}");
            sb.Append("No sign text anywhere except weathered remnants — do not turn words from this prompt into signage.");
            return sb.ToString();
        }

        // A half-dead retail row is not uniformly dark. Two tenants outlast
        // everything else in a failing American strip — the liquor store and the
        // tax/check-cashing office — and their lit windows are what makes the
        // dark units either side read as loss rather than as night.
        if (IsDecliningRetail(condition, sceneType))
        {
            sb.AppendLine("- a liquor store still open, its lit window the brightest thing in the row");
            sb.AppendLine("- a tax preparation and check-cashing office still open a few doors along, plain lettering, bars on the window");
            sb.AppendLine("- most of the other units dark behind the glass, several papered over from the inside");
        }

        var pool = new List<string>();
        if (content is not null)
            pool.AddRange(content.Storefronts);
        // The two survivors are stated explicitly above; drop any storefront the
        // era pool offers for the same businesses so they are not listed twice.
        if (IsDecliningRetail(condition, sceneType))
            pool.RemoveAll(s => s.Contains("liquor", StringComparison.OrdinalIgnoreCase)
                             || s.Contains("tax ", StringComparison.OrdinalIgnoreCase)
                             || s.Contains("check cashing", StringComparison.OrdinalIgnoreCase)
                             || s.Contains("check-cashing", StringComparison.OrdinalIgnoreCase));
        else
        {
            pool.AddRange(era.Architecture.Commercial.Characteristics);
            pool.AddRange(era.Business.Signage.Characteristics);
        }
        if (isGasStation)
            pool.AddRange(era.Architecture.GasStations.Characteristics);

        var usedConcepts = new HashSet<string>();
        var picks        = new List<string>();
        var target       = 3;

        // The fuel price and main sign lines below cover these concepts.
        if (isGasStation)
        {
            usedConcepts.Add("price sign");
            usedConcepts.Add("pole sign");
            target--;
        }
        else
        {
            // Always anchor the era with a period price where the pool offers one.
            foreach (var item in pool.Where(i => i.Contains('¢') || i.Contains('$')).Take(2))
            {
                picks.Add(item);
                var concept = ConceptOf(item);
                if (concept is not null) usedConcepts.Add(concept);
            }
        }

        foreach (var item in Sample(pool, pool.Count, rng))
        {
            if (picks.Count >= target) break;
            if (picks.Contains(item)) continue;
            var concept = ConceptOf(item);
            if (concept is not null && !usedConcepts.Add(concept)) continue;
            picks.Add(item);
        }

        // Storefront picks need the same "new" reconciliation the extras get: the
        // pool carries ghost-sign entries ("ghost signs from closed retailers")
        // that contradict a freshly rebuilt scene outright. Only the extras path
        // was covered before, so whether the contradiction shipped came down to
        // which pool the ghost-sign line happened to be sampled from.
        var ghostReconciled = false;
        foreach (var item in picks)
        {
            var line = ReconcileWithNewCondition(item, condition);
            if (!ReferenceEquals(line, item))
            {
                if (ghostReconciled) continue;   // one reconciling line is enough
                ghostReconciled = true;
            }
            sb.AppendLine($"- {line}");
        }

        // Chain tenant plan: a per-run decision resolved fresh for this era's
        // year/condition. Not decay content — a ghost sign is a period detail
        // (like the storefronts above), so it never goes through DecayPoolFor.
        foreach (var tenant in context.ResolveChainTenants(era.Year, sceneType, condition))
            sb.AppendLine($"- {tenant.Text}");

        if (isGasStation)
        {
            if (gasSign.Kind == GenerationContext.GasSignKind.DeadBoard)
            {
                // Dead station — no price, no lit brand. A stripped, rusted frame.
                sb.AppendLine("- main sign: a bare stripped sign frame — rusted metal posts, an empty price panel with no digits, no lit letters, no logo, all branding gone");
            }
            else
            {
                sb.AppendLine($"- price sign showing gas around {era.Transportation.Fuel.AveragePricePerGallon}");
                if (gasSign.Brand is not null)
                {
                    sb.AppendLine($"- main sign: \"{gasSign.Brand}\" branded gas station — {gasSign.Brand} pole sign with price display and {gasSign.Brand} colors on the canopy fascia");
                }
                else
                {
                    var brands = era.Business.GasBrands;
                    if (brands is { Count: > 0 })
                        sb.AppendLine($"- main sign: an independent station sign in the style of {brands[rng.Next(brands.Count)]}");
                }
            }
        }

        if (content is not null)
        {
            var signs = Sample(content.WindowSigns, 2, rng);
            if (signs.Count > 0)
                sb.AppendLine($"- window signs: {string.Join(", ", signs.Select(s => $"'{s}'"))}");

            // REQUIRED extras are always emitted and don't consume a sampling slot
            var required = content.Extras.Where(e => e.Contains("REQUIRED")).ToList();
            var optional = content.Extras.Except(required).ToList();
            foreach (var extra in required)
                sb.AppendLine($"- {ReconcileWithNewCondition(StripRequiredMarker(extra), condition)}");
            foreach (var extra in Sample(optional, 2, rng))
                sb.AppendLine($"- {ReconcileWithNewCondition(extra, condition)}");
        }

        sb.AppendLine($"Typography: {era.Business.Signage.TypographyStyle}.");
        return sb.ToString();
    }

    // Matches text inside straight quotes ('...' or "...") or typographic quotes
    // ('...' or "..." with curly marks) — every place the scene block quotes a
    // sign, brand or window-sign string.
    //
    // Excludes newlines from the captured span: prose elsewhere in the block
    // (an era narrative, an extras entry) can carry a single unmatched
    // apostrophe ("regular's car"), and without this a quote pair could span
    // from that stray apostrophe all the way to an unrelated closing quote
    // many lines later.
    //
    // The straight-quote alternatives also have to tell a real delimiter apart
    // from a word-internal apostrophe/possessive — a business name like
    // "COBBLER'S CORNER" sits on the same line as a second quoted string, and
    // a naive [^'\n]+ would treat the apostrophe in "COBBLER'S" as the closing
    // quote, truncating that name and misreading the gap before the next
    // quoted string as a signage entry of its own. (?<!\w)/(?!\w) require a
    // real delimiter to sit next to whitespace or a boundary, not another
    // letter; (?<=\w)'(?=\w) lets a letter-flanked apostrophe count as
    // ordinary content instead of ending the match.
    private static readonly System.Text.RegularExpressions.Regex QuotedTextPattern = new(
        "(?<!\\w)\"((?:[^\"\n]|(?<=\\w)\"(?=\\w))+)\"(?!\\w)" +
        "|(?<!\\w)'((?:[^'\n]|(?<=\\w)'(?=\\w))+)'(?!\\w)" +
        "|“([^”\n]+)”" +
        "|‘([^’\n]+)’");

    // Every readable string the scene block actually quotes, in first-seen
    // order, deduplicated. This becomes the whitelist: the model otherwise
    // lifts words out of the prompt and paints them onto surfaces that were
    // never given any text at all.
    private static List<string> CollectSignText(string sceneBlock)
    {
        var seen   = new HashSet<string>();
        var result = new List<string>();
        foreach (System.Text.RegularExpressions.Match m in QuotedTextPattern.Matches(sceneBlock))
        {
            string? text = null;
            for (var i = 1; i <= 4; i++)
                if (m.Groups[i].Success) { text = m.Groups[i].Value.Trim(); break; }

            if (text is null || text.Length < 2) continue;
            if (seen.Add(text)) result.Add(text);
        }
        return result;
    }

    // Replaces the old blanket "sign text is only what appears in quotes" line
    // with an explicit whitelist of exactly the strings this era's scene block
    // quoted. A bare whitelist would forbid a ghost sign's own weathered-but-
    // readable text, so the closing sentence explicitly carves out lettering
    // that is present but illegible, rather than contradicting that detail.
    private static string AppendSignageRestriction(string sceneBlock)
    {
        var signText = CollectSignText(sceneBlock);
        if (signText.Count == 0) return sceneBlock;

        var sb = new StringBuilder(sceneBlock);
        sb.AppendLine();
        sb.AppendLine();
        sb.AppendLine("SIGNAGE RESTRICTION");
        sb.AppendLine("The only readable text anywhere in the image is:");
        foreach (var s in signText)
            sb.AppendLine($"'{s}'");
        sb.Append("Do not render any other readable words, business names, slogans, advertisements, logos or brand names. Lettering that appears elsewhere must be too small, too distant or too weathered to read as words.");
        return sb.ToString();
    }

    private static string BuildPeopleBlock(EraProfile era, SceneContent? content, int peopleCount, bool isGasStation, bool hasSidewalks, Random rng, GenerationContext context, bool isPacked, bool isSquatted, bool isDecliningRetail)
    {
        var sb = new StringBuilder();
        sb.AppendLine("PEOPLE");
        if (!isPacked && peopleCount == 0)
        {
            sb.Append("NO people anywhere — the place is completely deserted.");
            return sb.ToString();
        }

        // The era's people_activities are all customers and staff of a working
        // business — a clerk restocking a cooler, someone refuelling. Emitting
        // them at a dead station contradicts the condition outright, and the
        // image model resolves that by reopening the place. Nothing from the era
        // pool is used here, and no clothing is specified.
        if (isSquatted)
        {
            sb.AppendLine($"EXACTLY {peopleCount} people TOTAL and nobody else:");
            sb.AppendLine("- two of them sitting close together on the kerb out of the wind, sharing a bottle between them, turned slightly away, keeping to themselves");

            // An ordinary passer-by still walks past a dead lot — they simply have
            // no business with it. Drawn from the era's people_mix so the figure
            // stays period-correct, but explicitly just passing through.
            if (era.PeopleMix is { Count: > 0 })
            {
                var mixPick = SampleUnused(era.PeopleMix, 1, rng, context);
                if (mixPick.Count > 0)
                    sb.AppendLine($"- {mixPick[0]} — passing by along the far edge, not stopping, nothing to do with this place");
            }

            sb.AppendLine("No staff, no customers, nobody working — nothing here is open.");
            sb.Append("The two have only come in to drink out of sight: no tents, no tarps, no bedding, no shopping carts, no belongings laid out — nobody lives here.");
            return sb.ToString();
        }

        if (isPacked)
        {
            sb.AppendLine("A DENSE CROWD of shoppers — far too many to count: a steady stream in and out of the entrance doors, groups moving between parked cars, clusters waiting near the doors, foreground figures sharp and background figures loose. Do not render an empty or sparse scene.");
        }
        else
        {
            var (near, opposite, distant) = SplitIntoZones(peopleCount);
            var zones = new List<string>();
            if (hasSidewalks)
            {
                if (near > 0)     zones.Add($"{near} near sidewalk (largest, foreground)");
                if (opposite > 0) zones.Add($"{opposite} opposite sidewalk mid-block");
                if (distant > 0)  zones.Add($"{distant} distant, far end of block");
            }
            else
            {
                if (near > 0)     zones.Add($"{near} in the foreground near the building entrance (largest)");
                if (opposite > 0) zones.Add($"{opposite} mid-ground out on the lot, beside parked vehicles");
                if (distant > 0)  zones.Add($"{distant} distant, at the far edge of the lot");
            }
            sb.AppendLine($"EXACTLY {peopleCount} people TOTAL: {string.Join(", ", zones)}. Grouped in pairs, threes, and singles.");
        }

        // The liquor store is the one thing still drawing anybody to a dying row,
        // so that is where the figures collect. Count and behaviour are both
        // sampled — sometimes one person waiting, sometimes a few already drinking.
        if (isDecliningRetail)
        {
            var n    = rng.Next(1, 4);
            var who  = n == 1 ? "one person" : $"{n} people";
            var doing = rng.Next(4) switch
            {
                0 => "waiting outside the liquor store for someone, not going in",
                1 => "just out of the liquor store, a bottle in a black plastic bag",
                2 => "already drinking, sitting along the wall a few doors down from the liquor store",
                _ => "standing around outside the liquor store with nothing to do"
            };
            sb.AppendLine($"- of these, {who} {doing} — keeping to themselves, not looking at the camera");
        }

        if (content is not null)
            foreach (var activity in SampleUnused(content.PeopleActivities, 2, rng, context))
                sb.AppendLine($"- {activity}");

        if (era.PeopleMix is { Count: > 0 })
        {
            var mixPick = SampleUnused(era.PeopleMix, 1, rng, context);
            if (mixPick.Count > 0)
                sb.AppendLine($"- {mixPick[0]}");
        }

        var fashion = era.Society.Fashion;
        var men     = Sample(fashion.Men, 2, rng);
        var women   = Sample(fashion.Women, 2, rng);
        sb.Append($"Clothing: men in {string.Join(", ", men)}; women in {string.Join(", ", women)}.");
        if (era.Photography.ColorMode != "black_and_white")
            sb.Append($" Fashion palette: {Join(fashion.Colors.Take(3).ToList())}.");
        sb.Append(" No posing or eye contact.");
        sb.Append(hasSidewalks
            ? " All people stay on sidewalks, at storefronts, or beside parked vehicles — never standing, sitting, or walking in the road or driving lanes."
            : " All people stay on the lot apron, at the building entrance, or beside parked vehicles — never standing, sitting, or walking in the road or driving lanes.");
        if (isGasStation)
            sb.Append(" Any customer activity at the pumps happens next to a parked vehicle — no one refuels without a car present.");
        return sb.ToString();
    }

    // ~40% near sidewalk (largest, foreground/mid-ground), ~35% opposite sidewalk
    // mid-block, remainder as small distant figures — always summing to the exact
    // total so the count contract holds regardless of scene density.
    private static (int Near, int Opposite, int Distant) SplitIntoZones(int peopleCount)
    {
        var near     = (int)Math.Round(peopleCount * 0.40, MidpointRounding.AwayFromZero);
        var opposite = (int)Math.Round(peopleCount * 0.35, MidpointRounding.AwayFromZero);
        var distant  = peopleCount - near - opposite;
        if (distant < 0)
        {
            var shrink       = -distant;
            var fromOpposite = Math.Min(opposite, shrink);
            opposite -= fromOpposite;
            shrink   -= fromOpposite;
            near      = Math.Max(0, near - shrink);
            distant   = peopleCount - near - opposite;
        }
        return (near, opposite, distant);
    }

    private static List<(string Model, string? Color)> PickVehicles(
        EraProfile era, GenerationContext context, int vehicleCount, ILogger logger)
    {
        var cars     = era.Transportation.Cars;
        var fullPool = cars.SpecificModels.Concat(era.Transportation.Trucks.SpecificModels).Distinct().ToList();
        var pool     = fullPool.Where(m => !context.IsCarModelUsed(m)).ToList();

        if (pool.Count < vehicleCount)
            logger.LogWarning(
                "Vehicle pool exhausted for year {Year}: {Unused} unused of {Total}, need {Count} — topping up from full list",
                era.Year, pool.Count, fullPool.Count, vehicleCount);

        var rng        = context.Random;
        var monochrome = era.Photography.ColorMode == "black_and_white";
        var usedColors = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var pickedBases = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var picks      = new List<string>();

        foreach (var model in Sample(pool, pool.Count, rng))
        {
            if (picks.Count >= vehicleCount) break;
            if (!context.TryUseCarModel(model)) continue;
            pickedBases.Add(GenerationContext.BaseModelName(model));
            picks.Add(model);
        }
        // Top up from the full list when the unused pool ran dry, still keeping
        // base model names unique within this prompt.
        foreach (var model in Sample(fullPool, fullPool.Count, rng))
        {
            if (picks.Count >= vehicleCount) break;
            if (!pickedBases.Add(GenerationContext.BaseModelName(model))) continue;
            picks.Add(model);
        }

        var result = new List<(string, string?)>();
        foreach (var model in picks)
        {
            string? color = null;
            if (!monochrome)
            {
                var available = cars.Colors.Where(c => !usedColors.Contains(c)).ToList();
                color = available.Count > 0
                    ? available[rng.Next(available.Count)]
                    : "period-correct color";
                usedColors.Add(color);
            }
            result.Add((model, color));
        }
        return result;
    }

    private static string BuildVehiclesBlock(IReadOnlyList<(string Model, string? Color)> vehicles, int year, string placement, bool isGasStation, bool onStreetParking, bool isPacked, int derelictCount)
    {
        var sb = new StringBuilder();
        sb.AppendLine("VEHICLES");

        // Anything still on a squatted lot was dumped, not parked: it must read as
        // a wreck a decade older than the era, never a current running car.
        if (derelictCount > 0)
        {
            var noun = derelictCount == 1 ? "vehicle" : "vehicles";
            sb.AppendLine($"EXACTLY {derelictCount} old, worn {noun} and nothing else — left standing here, not driven for a long time:");
            sb.AppendLine($"- an ordinary sedan or pickup of roughly {year - 15}-{year - 12}, plain and unremarkable, already well used");
            sb.AppendLine("- dulled paint, dents and rust spots, dirty, one tyre flat, sitting askew across the faded stall lines");
            sb.Append($"NO clean, current or well-kept vehicles anywhere, and nothing newer than about {year - 12}.");
            return sb.ToString();
        }

        if (!isPacked && vehicles.Count == 0)
        {
            sb.Append("NO vehicles anywhere — empty lot, no parked or moving cars.");
            return sb.ToString();
        }

        if (isPacked)
        {
            sb.AppendLine("A FULL parking lot — every stall occupied in unbroken rows out to the far edge, too many vehicles to count, no empty stalls in the foreground. Representative models visible near the camera:");
            foreach (var (model, color) in vehicles)
                sb.AppendLine(color is null ? $"- {model}" : $"- {model} — {color}");
            sb.Append($"Every other vehicle in the lot is period-correct for {year}; nothing newer. All nose-in or angled into stalls; none parallel-parked.");
            return sb.ToString();
        }

        sb.AppendLine($"EXACTLY {vehicles.Count} period vehicles, all different:");
        foreach (var (model, color) in vehicles)
            sb.AppendLine(color is null ? $"- {model}" : $"- {model} — {color}");
        sb.AppendLine($"Parked with gaps. Where a model is listed with a year range, render the {year} model year specifically — only styling, trim and features available in {year}, nothing introduced later. No vehicle newer than {year}.");
        sb.AppendLine(onStreetParking
            ? "Parked vehicles hug the curb — parallel, each facing its lane's direction; none sideways, diagonal, or against traffic. Keep at least one full driving lane clear each way for through traffic."
            : "Parked vehicles sit nose-in or angled into the lot, evenly spaced; none parallel-parked along the street, none blocking a driveway apron. Keep the driveway aprons and the drive lanes through the lot clear.");
        sb.Append($"PLACEMENT: {placement}. No vehicle in the same spot as any other era.");
        return sb.ToString();
    }

    private static string BuildEnvironmentBlock(
        SceneDna scene, EraProfile era, int year, string sceneType,
        string condition, Random rng)
    {
        var infra = era.Infrastructure;
        var sb = new StringBuilder();
        sb.AppendLine("ENVIRONMENT");
        // Crisp era markings only make sense on a scene that is still kept up.
        if (condition is "declining" or "abandoned" or "squatted")
            sb.AppendLine("- road markings: worn and faded, well past their last repainting");
        else
            sb.AppendLine($"- road markings: {Join(infra.Roads.Markings.Take(3).ToList())}");
        var isDowntown = sceneType == "downtown_street";
        var utilitiesPool = isDowntown && era.Infrastructure.Utilities.DowntownCharacteristics is { Count: > 0 } dc
            ? dc
            : infra.Utilities.Characteristics;
        sb.AppendLine($"- utilities: {Join(utilitiesPool.Take(2).ToList())}");
        sb.Append(BuildDecayBlock(condition, sceneType, rng));

        // In the source year the base image already shows the trees at their
        // current size, so every DescribeTreeSize comes back empty and the whole
        // section is omitted. Emitting it there would tell the model to match the
        // base and to deviate from it in the same breath.
        var treeLines = scene.Environment.Trees
            .Select(t => (Tree: t, Size: DescribeTreeSize(t.Size, year)))
            .Where(x => x.Size.Length > 0)
            .Select(x => $"- {x.Tree.Type} tree at {x.Tree.Position}: {x.Size}")
            .ToList();

        if (treeLines.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("TREES");
            foreach (var line in treeLines)
                sb.AppendLine(line);
            sb.Append("Tree sizes MUST follow this specification even where they differ from the base image.");
        }

        return sb.ToString().TrimEnd();
    }

    // The newest era is the anchor: a tree's canopy in any earlier era is
    // expressed as a proportion of what it looks like there, using a per-decade
    // retention rate for the size Vision recorded. Unlike an absolute rung
    // ladder, this never clamps and never repeats — every decade differs,
    // giving the model a comparator it can act on.
    //
    // The comparator is the BASE IMAGE, not the original photo: every era
    // request uploads base_clean.png (or base_synthetic.png), and the source
    // photo is never sent to those calls at all.
    private const int SourceYear = 2025; // newest era — the base image already shows trees at this size

    // Returns "" for the source year: the base already shows the trees at their
    // current size, so there is nothing to instruct. The caller drops the whole
    // TREES section in that case.
    private static string DescribeTreeSize(string size, int year)
    {
        var retention = size.ToLowerInvariant() switch
        {
            "large"  => 0.90,
            "medium" => 0.78,
            "small"  => 0.62,
            _        => 0.78
        };
        var decadesBack = (SourceYear - year) / 10;
        if (decadesBack == 0)
            return "";

        var fraction = Math.Pow(retention, decadesBack);
        var pct = (int)(Math.Round(fraction * 20.0, MidpointRounding.AwayFromZero) * 5);

        if (fraction >= 0.85)
            return $"slightly smaller than in the base image — about {pct}% of its canopy there";
        if (fraction >= 0.35)
            return $"clearly smaller than in the base image — about {pct}% of its canopy there, thinner trunk";
        return $"a young tree, only about {pct}% of its canopy in the base image, thin trunk";
    }

    private static string BuildStyleBlock(Photography photo, string condition)
    {
        var sb = new StringBuilder();
        sb.AppendLine("PHOTOGRAPHIC STYLE");
        var monochrome = photo.ColorMode == "black_and_white";
        if (monochrome)
        {
            sb.AppendLine("STRICTLY BLACK AND WHITE — true monochrome archival photograph, zero color anywhere.");
            sb.AppendLine($"Grain: {photo.Grain}. Style: {StripSaturationWording(photo.Style)}.");
            sb.AppendLine("Photorealistic, like a preserved newspaper-archive frame; slight period-lens softness, not digitally sharp.");
        }
        else
        {
            sb.AppendLine($"COLOR photograph — {photo.FilmStock} look.");
            sb.AppendLine($"Color: {Join(photo.ColorCharacteristics.Take(1).ToList())}. Grain: {photo.Grain}.");
            sb.AppendLine("Photorealistic — NOT black-and-white, no HDR.");
        }
        sb.Append(LightFor(condition, monochrome));
        return sb.ToString();
    }

    // Weather carries the arc. A living era shot under grey cloud reads as
    // depressing rather than remembered, and the loss at the end then has
    // nothing to land against — so the good years get open, pleasant daylight
    // and only the dead ones go flat and grey. Never storm or fog: the scene
    // must stay a clear daytime record, not a mood piece.
    private static string LightFor(string condition, bool monochrome)
    {
        if (condition is "abandoned" or "squatted")
            return "Light: flat, even light under a grey overcast — subdued and cheerless, but plainly daytime; no fog, no rain, no darkness.";

        if (condition == "declining")
            return monochrome
                ? "Light: plain daylight, lightly overcast, soft shadows — quiet, not bleak."
                : "Light: plain daylight, lightly overcast, soft shadows — colours a little muted, quiet but not bleak.";

        return monochrome
            ? "Light: bright open daylight, clear sky, soft natural shadows — a fine, ordinary day."
            : "Light: warm, bright afternoon daylight, clear sky with light cloud, soft natural shadows — a fine, ordinary day.";
    }

    // A monochrome prompt must never mention saturation — image models take it as a
    // color-grading hint and drift back into color.
    private static string StripSaturationWording(string style)
    {
        var parts = style.Split(',')
            .Select(p => p.Trim())
            .Where(p => p.Length > 0 && !p.Contains("saturat", StringComparison.OrdinalIgnoreCase));
        return string.Join(", ", parts);
    }

    private static List<string> Sample(IEnumerable<string> pool, int count, Random rng) =>
        pool.Distinct().OrderBy(_ => rng.Next()).Take(count).ToList();

    // Like Sample, but with cross-era memory via context.TryUsePeopleLine — the
    // same shape UsedCarModels gives vehicles. Shuffles the distinct pool once,
    // then walks it taking entries not yet used elsewhere in this run until it
    // has `count`. If the pool is exhausted before reaching `count` (every entry
    // already used by an earlier era), it tops up from those already-used
    // entries in the same shuffled order, so it always returns
    // min(count, pool size) — never fewer than plain Sample would — and never
    // throws on an empty pool.
    private static List<string> SampleUnused(
        IEnumerable<string> pool, int count, Random rng, GenerationContext context)
    {
        var shuffled = pool.Distinct().OrderBy(_ => rng.Next()).ToList();
        var picks    = new List<string>();
        var leftover = new List<string>();

        foreach (var item in shuffled)
        {
            if (picks.Count >= count) break;
            if (context.TryUsePeopleLine(item))
                picks.Add(item);
            else
                leftover.Add(item);
        }

        foreach (var item in leftover)
        {
            if (picks.Count >= count) break;
            picks.Add(item);
        }

        return picks;
    }

    private static string Join(IReadOnlyList<string> list) =>
        list.Count > 0 ? string.Join(", ", list) : "none";
}
