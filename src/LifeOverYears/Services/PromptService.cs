using System.Text;
using LifeOverYears.Models;
using LifeOverYears.Services.Interfaces;
using Microsoft.Extensions.Logging;

namespace LifeOverYears.Services;

public sealed class PromptService : IPromptService
{
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

        var sceneType    = sceneDna.SceneType ?? "default";
        var sceneContent = ResolveSceneContent(eraProfile, sceneType);
        if (sceneContent is null)
            _logger.LogWarning("No scene_content in era {Year} for scene type '{SceneType}' — building generic scene block",
                year, sceneType);

        var isGasStation = sceneType == "gas_station";

        // Conditions are a gas-station-only concept for now: only gas stations
        // sample one and let it drive activity level (abandoned → deserted,
        // declining → sparse). Other scene types always stay "thriving" and use
        // their base scene_content ranges untouched.
        var condition = isGasStation
            ? context.PickSceneCondition(eraProfile.AllowedSceneConditions)
            : "thriving";
        _logger.LogInformation("Scene condition for SceneDna {Id}, year {Year}: {Condition}",
            sceneDna.Id, year, condition);

        var peopleRange  = sceneContent?.People   ?? new CountRange(10, 15);
        var vehicleRange = sceneContent?.Vehicles ?? new CountRange(4, 6);
        var peopleCount  = rng.Next(peopleRange.Min, peopleRange.Max + 1);
        var vehicleCount = rng.Next(vehicleRange.Min, vehicleRange.Max + 1);

        if (isGasStation && condition == "abandoned")
        {
            peopleCount  = 0;
            vehicleCount = 0;
        }
        else if (isGasStation && condition == "declining")
        {
            peopleCount  = rng.Next(2, 5);
            vehicleCount = rng.Next(1, 3);
        }

        var vehicles  = PickVehicles(eraProfile, context, vehicleCount, _logger);
        // An abandoned era has no vehicles and no PLACEMENT line — don't consume a
        // placement pattern from the run's pool for it.
        var placement = vehicles.Count > 0 ? context.NextPlacement(vehicles.Count) : "";
        var brand     = isGasStation ? await ResolveGasBrandAsync(year, rng) : null;

        var text = template
            .Replace("{PRESERVE_BLOCK}",    BuildPreserveBlock(sceneDna))
            .Replace("{SCENE_BLOCK}",       BuildSceneBlock(eraProfile, sceneContent, sceneType, condition, brand, rng))
            .Replace("{PEOPLE_BLOCK}",      BuildPeopleBlock(eraProfile, sceneContent, peopleCount, isGasStation, rng))
            .Replace("{VEHICLES_BLOCK}",    BuildVehiclesBlock(vehicles, year, placement, isGasStation))
            .Replace("{ENVIRONMENT_BLOCK}", BuildEnvironmentBlock(sceneDna, eraProfile, year))
            .Replace("{STYLE_BLOCK}",       BuildStyleBlock(eraProfile.Photography))
            // Scene content refers to the recurring diner by token so the same name
            // persists across every era of a run; resolve it last.
            .Replace("{DINER_NAME}",        context.DinerName);

        return new Prompt(
            Id:               Guid.NewGuid().ToString(),
            SceneDnaId:       sceneDna.Id,
            Year:             year,
            Text:             text,
            SelectedVehicles: vehicles.Select(v => v.Model).ToList(),
            CreatedAt:        DateTimeOffset.UtcNow.ToString("o"),
            SceneCondition:   condition);
    }

    // Era-appropriate brand from data/brands/gas-brands.txt; null falls back to the
    // era JSON gas_brands list inside BuildSceneBlock.
    private async Task<string?> ResolveGasBrandAsync(int year, Random rng)
    {
        try
        {
            var brands   = await _data.LoadGasBrandsAsync();
            var eligible = brands.Where(b => b.From <= year && year <= b.To).ToList();
            if (eligible.Count > 0)
                return eligible[rng.Next(eligible.Count)].Name;
            _logger.LogWarning("No gas brand matches year {Year} — falling back to era JSON gas_brands", year);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not load gas brands file — falling back to era JSON gas_brands");
        }
        return null;
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
        "restored"  => "renovated appearance, modern updates on original structure",
        _           => "well-maintained, freshly painted, active business, clean lot"
    };

    private static string BuildSceneBlock(EraProfile era, SceneContent? content, string sceneType, string condition, string? brand, Random rng)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"TRANSFORM TO {era.Year}");
        var intro = TruncateToSentences(era.Description, 1);
        sb.AppendLine(content is null ? intro : $"{intro} {content.Narrative}");

        var isGasStation = sceneType == "gas_station";

        // Scene atmosphere — condition affects appearance/upkeep only, never the
        // physical geometry in the PRESERVE block. Gas stations only for now.
        if (isGasStation)
        {
            sb.AppendLine();
            sb.AppendLine($"CONDITION: {condition} — {ConditionDescriptor(condition)}");
        }

        sb.AppendLine();
        sb.AppendLine("PERIOD DETAILS");

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
            sb.AppendLine($"- price sign showing gas around {era.Transportation.Fuel.AveragePricePerGallon}");
            if (brand is not null)
            {
                sb.AppendLine($"- main sign: \"{brand}\" branded gas station — {brand} pole sign with price display and {brand} colors on the canopy fascia");
            }
            else
            {
                var brands = era.Business.GasBrands;
                if (brands is { Count: > 0 })
                    sb.AppendLine($"- main sign: an independent station sign in the style of {brands[rng.Next(brands.Count)]}");
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

    private static string BuildPeopleBlock(EraProfile era, SceneContent? content, int peopleCount, bool isGasStation, Random rng)
    {
        var sb = new StringBuilder();
        sb.AppendLine("PEOPLE");
        if (peopleCount == 0)
        {
            sb.Append("NO people anywhere — the place is completely deserted.");
            return sb.ToString();
        }
        sb.AppendLine($"EXACTLY {peopleCount} people, spread out with empty areas.");

        if (content is not null)
            foreach (var activity in Sample(content.PeopleActivities, 2, rng))
                sb.AppendLine($"- {activity}");

        if (era.PeopleMix is { Count: > 0 })
            sb.AppendLine($"- {era.PeopleMix[rng.Next(era.PeopleMix.Count)]}");

        var fashion = era.Society.Fashion;
        var men     = Sample(fashion.Men, 2, rng);
        var women   = Sample(fashion.Women, 2, rng);
        sb.Append($"Clothing: men in {string.Join(", ", men)}; women in {string.Join(", ", women)}.");
        if (era.Photography.ColorMode != "black_and_white")
            sb.Append($" Fashion palette: {Join(fashion.Colors.Take(3).ToList())}.");
        sb.Append(" No posing or eye contact.");
        sb.Append(" All people on sidewalks, at storefronts, or beside parked vehicles — the driving lanes stay empty of pedestrians.");
        if (isGasStation)
            sb.Append(" Any customer activity at the pumps happens next to a parked vehicle — no one refuels without a car present.");
        return sb.ToString();
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
        sb.AppendLine("All vehicles obey normal US right-hand traffic flow — parked parallel to the curb, each facing the same direction as its adjacent lane. Nothing sideways, diagonal, or against traffic.");
        if (isGasStation)
            sb.AppendLine("One of these vehicles is parked at the pump island with its driver standing beside it refueling.");
        sb.Append($"PLACEMENT: {placement}. No vehicle in the same spot as any other era.");
        return sb.ToString();
    }

    private static string BuildEnvironmentBlock(SceneDna scene, EraProfile era, int year)
    {
        var infra = era.Infrastructure;
        var sb = new StringBuilder();
        sb.AppendLine("ENVIRONMENT");
        sb.AppendLine($"- road markings: {Join(infra.Roads.Markings.Take(3).ToList())}");
        sb.AppendLine($"- utilities: {Join(infra.Utilities.Characteristics.Take(2).ToList())}");
        sb.AppendLine();
        sb.AppendLine("TREES");
        foreach (var tree in scene.Environment.Trees)
            sb.AppendLine($"- {tree.Type} tree at {tree.Position}: {DescribeTreeSize(tree.Size, year)}");
        sb.Append("Tree sizes MUST follow this specification even where they differ from the source photo.");
        return sb.ToString().TrimEnd();
    }

    private static readonly string[] TreeLadder =
    {
        "very young sapling",
        "young tree, thin trunk",
        "established tree, modest canopy",
        "maturing tree",
        "mature tree, full canopy",
        "mature tree, large canopy"
    };

    // Sizes scale relative to the size recorded in SceneDna: a tree that is already
    // young in the 2025 source stays small in every earlier era.
    private static string DescribeTreeSize(string size, int year)
    {
        var sourceIndex = size.ToLowerInvariant() switch
        {
            "large"  => 5,
            "medium" => 3,
            "small"  => 1,
            _        => 3
        };
        var stepsBack = (2025 - year) / 10;
        return TreeLadder[Math.Max(0, sourceIndex - stepsBack)];
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

    private static string Join(IReadOnlyList<string> list) =>
        list.Count > 0 ? string.Join(", ", list) : "none";
}
