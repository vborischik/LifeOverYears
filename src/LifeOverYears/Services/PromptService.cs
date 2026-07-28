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

        var isGasStation = sceneType == "gas_station";
        // Gas stations, downtown streets and strip malls all carry a condition
        // arc — decline is the story these runs are for. Only default/unknown
        // scenes stay thriving and use their base ranges untouched.
        var supportsCondition = sceneType is "gas_station" or "downtown_street" or "strip_mall";

        var condition = supportsCondition
            ? context.PickSceneCondition(eraProfile.AllowedSceneConditions, sceneType)
            : "thriving";
        _logger.LogInformation(
            "Scene condition for SceneDna {Id}, year {Year} (era {Index}): {Condition}",
            sceneDna.Id, year, eraIndex, condition);

        var peopleRange  = sceneContent?.People   ?? new CountRange(10, 15);
        var vehicleRange = sceneContent?.Vehicles ?? new CountRange(4, 6);
        var peopleCount  = rng.Next(peopleRange.Min, peopleRange.Max + 1);
        var vehicleCount = rng.Next(vehicleRange.Min, vehicleRange.Max + 1);

        // The opening era sets the "before" that everything after is measured
        // against; a sparse street there reads as empty rather than nostalgic.
        if (context.IsFirstEra)
            peopleCount = Math.Min(peopleCount * 2, FirstEraPeopleCap);

        if (supportsCondition && condition == "abandoned")
        {
            peopleCount  = 0;
            vehicleCount = 0;
        }
        else if (supportsCondition && condition == "squatted")
        {
            peopleCount  = rng.Next(2, 5);
            vehicleCount = rng.Next(0, 2);
        }
        else if (supportsCondition && condition == "declining")
        {
            peopleCount  = rng.Next(2, 5);
            vehicleCount = rng.Next(1, 3);
        }

        var vehicles  = PickVehicles(eraProfile, context, vehicleCount, _logger);
        // An abandoned era has no vehicles and no PLACEMENT line — don't consume a
        // placement pattern from the run's pool for it.
        var placement = vehicles.Count > 0 ? context.NextPlacement(vehicles.Count) : "";
        var gasSign   = isGasStation ? await ResolveGasSignAsync(context, year, condition) : default;

        var text = template
            .Replace("{PRESERVE_BLOCK}",    BuildPreserveBlock(sceneDna))
            .Replace("{SCENE_BLOCK}",       BuildSceneBlock(eraProfile, sceneContent, sceneType, condition, gasSign, rng))
            .Replace("{PEOPLE_BLOCK}",      BuildPeopleBlock(eraProfile, sceneContent, peopleCount, isGasStation, rng, context))
            .Replace("{VEHICLES_BLOCK}",    BuildVehiclesBlock(vehicles, year, placement, isGasStation))
            .Replace("{ENVIRONMENT_BLOCK}", BuildEnvironmentBlock(sceneDna, eraProfile, year, sceneType, condition, rng))
            .Replace("{STYLE_BLOCK}",       BuildStyleBlock(eraProfile.Photography));

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

    private static SceneContent? ResolveSceneContent(EraProfile era, string sceneType)
    {
        if (era.SceneContent is null)
            return null;
        if (era.SceneContent.TryGetValue(sceneType, out var content))
            return content;
        return era.SceneContent.TryGetValue("default", out var fallback) ? fallback : null;
    }

    private static string BuildPreserveBlock(SceneDna s)
    {
        var sb = new StringBuilder();
        sb.AppendLine("PRESERVE (must match source exactly)");
        // Some SceneDna directions already read "…-facing"; avoid "facing street-facing".
        var facingClause = s.Camera.Direction.Contains("facing", StringComparison.OrdinalIgnoreCase)
            ? s.Camera.Direction
            : $"facing {s.Camera.Direction}";
        sb.AppendLine($"- camera: {s.Camera.Height}, {facingClause}, fov {s.Camera.Fov}");
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
            sb.AppendLine($"- utilities: {string.Join(", ", s.Environment.Utilities)}");
        if (s.Environment.Landscape.Count > 0)
            sb.AppendLine($"- landscape: {string.Join(", ", s.Environment.Landscape)}");
        var immutable = CleanImmutableElements(s.ImmutableElements);
        if (immutable.Count > 0)
            sb.AppendLine($"- immutable elements: {string.Join(", ", immutable)}");
        sb.Append("Keep this location instantly recognizable.");
        return sb.ToString();
    }

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
        "squatted"  => "closed and derelict, makeshift shelters and tarps by the boarded entrance, shopping carts and scattered belongings, weeds through the pavement",
        "restored"  => "renovated appearance, modern updates on original structure",
        _           => "well-maintained, freshly painted, active business, clean lot"
    };

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

    // Pool selection lives here (not at the call site) so the smoke test can
    // assert against exactly the pool the builder would have used.
    internal static string[]? DecayPoolFor(string sceneType, string condition) => condition switch
    {
        "declining" => sceneType switch
        {
            "downtown_street" => DowntownDecayModerate,
            "strip_mall"      => StripMallDecayModerate,
            _                 => DecayModerate
        },
        "abandoned" or "squatted" => sceneType switch
        {
            "downtown_street" => DowntownDecayHeavy,
            "strip_mall"      => StripMallDecayHeavy,
            _                 => DecayHeavy
        },
        _ => null
    };

    // Two or three concrete details per era, sampled so consecutive eras of the
    // same run don't repeat the same wording.
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

    private static string BuildSceneBlock(EraProfile era, SceneContent? content, string sceneType, string condition, GenerationContext.GasSign gasSign, Random rng)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"TRANSFORM TO {era.Year}");
        var intro = TruncateToSentences(era.Description, 1);
        sb.AppendLine(content is null ? intro : $"{intro} {content.Narrative}");

        var isGasStation = sceneType == "gas_station";

        // Scene atmosphere — condition affects appearance/upkeep only, never the
        // physical geometry in the PRESERVE block.
        if (sceneType is "gas_station" or "downtown_street" or "strip_mall")
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
            sb.Append("No sign text anywhere except weathered remnants — do not turn words from this prompt into signage.");
            return sb.ToString();
        }

        var pool = new List<string>();
        if (content is not null)
            pool.AddRange(content.Storefronts);
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

        foreach (var item in picks)
            sb.AppendLine($"- {item}");
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
                sb.AppendLine($"- {StripRequiredMarker(extra)}");
            foreach (var extra in Sample(optional, 2, rng))
                sb.AppendLine($"- {extra}");
        }

        sb.AppendLine($"Typography: {era.Business.Signage.TypographyStyle}.");
        sb.Append("Sign text is only what appears in quotes — do not turn other words from this prompt into signage.");
        return sb.ToString();
    }

    private static string BuildPeopleBlock(EraProfile era, SceneContent? content, int peopleCount, bool isGasStation, Random rng, GenerationContext context)
    {
        var sb = new StringBuilder();
        sb.AppendLine("PEOPLE");
        if (peopleCount == 0)
        {
            sb.Append("NO people anywhere — the place is completely deserted.");
            return sb.ToString();
        }

        var (near, opposite, distant) = SplitIntoZones(peopleCount);
        var zones = new List<string>();
        if (near > 0)     zones.Add($"{near} near sidewalk (largest, foreground)");
        if (opposite > 0) zones.Add($"{opposite} opposite sidewalk mid-block");
        if (distant > 0)  zones.Add($"{distant} distant, far end of block");
        sb.AppendLine($"EXACTLY {peopleCount} people TOTAL: {string.Join(", ", zones)}. Grouped in pairs, threes, and singles.");

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
        sb.Append(" All people stay on sidewalks, at storefronts, or beside parked vehicles — never standing, sitting, or walking in the road or driving lanes.");
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

    private static string BuildVehiclesBlock(IReadOnlyList<(string Model, string? Color)> vehicles, int year, string placement, bool isGasStation)
    {
        var sb = new StringBuilder();
        sb.AppendLine("VEHICLES");
        if (vehicles.Count == 0)
        {
            sb.Append("NO vehicles anywhere — empty lot, no parked or moving cars.");
            return sb.ToString();
        }
        sb.AppendLine($"EXACTLY {vehicles.Count} period vehicles, all different:");
        foreach (var (model, color) in vehicles)
            sb.AppendLine(color is null ? $"- {model}" : $"- {model} — {color}");
        sb.AppendLine($"Parked with gaps; no vehicle newer than {year}.");
        sb.AppendLine("Parked vehicles hug the curb — parallel, each facing its lane's direction; none sideways, diagonal, or against traffic. Keep at least one full driving lane clear each way for through traffic.");
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
        sb.AppendLine();
        sb.AppendLine("TREES");
        foreach (var tree in scene.Environment.Trees)
            sb.AppendLine($"- {tree.Type} tree at {tree.Position}: {DescribeTreeSize(tree.Size, year)}");
        sb.Append("Tree sizes MUST follow this specification even where they differ from the source photo.");
        return sb.ToString().TrimEnd();
    }

    // The source photo is the newest era, so it is the anchor: a tree's canopy
    // in any earlier era is expressed as a proportion of what it looks like
    // there, using a per-decade retention rate for the size Vision recorded.
    // Unlike an absolute rung ladder, this never clamps and never repeats —
    // every decade differs, giving the model a comparator it can act on
    // regardless of which image it is actually editing.
    private const int SourceYear = 2025; // newest era — trees render "as in the source" here

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
            return "same size as in the source photo";

        var fraction = Math.Pow(retention, decadesBack);
        var pct = (int)(Math.Round(fraction * 20.0, MidpointRounding.AwayFromZero) * 5);

        if (fraction >= 0.85)
            return $"slightly smaller than in the source — about {pct}% of its canopy there";
        if (fraction >= 0.35)
            return $"clearly smaller than in the source — about {pct}% of its canopy there, thinner trunk";
        return $"a young tree, only about {pct}% of its canopy in the source photo, thin trunk";
    }

    private static string BuildStyleBlock(Photography photo)
    {
        var sb = new StringBuilder();
        sb.AppendLine("PHOTOGRAPHIC STYLE");
        if (photo.ColorMode == "black_and_white")
        {
            sb.AppendLine("STRICTLY BLACK AND WHITE — true monochrome archival photograph, zero color anywhere.");
            sb.AppendLine($"Grain: {photo.Grain}. Style: {StripSaturationWording(photo.Style)}.");
            sb.Append("Photorealistic, like a preserved newspaper-archive frame; slight period-lens softness, not digitally sharp.");
        }
        else
        {
            sb.AppendLine($"COLOR photograph — {photo.FilmStock} look.");
            sb.AppendLine($"Color: {Join(photo.ColorCharacteristics.Take(1).ToList())}. Grain: {photo.Grain}.");
            sb.Append("Photorealistic — NOT black-and-white, no HDR.");
        }
        return sb.ToString();
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
