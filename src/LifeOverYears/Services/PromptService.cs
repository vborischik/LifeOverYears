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

        var peopleRange  = sceneContent?.People   ?? new CountRange(10, 15);
        var vehicleRange = sceneContent?.Vehicles ?? new CountRange(4, 6);
        var peopleCount  = rng.Next(peopleRange.Min, peopleRange.Max + 1);
        var vehicleCount = rng.Next(vehicleRange.Min, vehicleRange.Max + 1);

        var vehicles  = PickVehicles(eraProfile, context, vehicleCount, _logger);
        var placement = context.NextPlacement(vehicles.Count);

        var text = template
            .Replace("{PRESERVE_BLOCK}",    BuildPreserveBlock(sceneDna))
            .Replace("{SCENE_BLOCK}",       BuildSceneBlock(eraProfile, sceneContent, sceneType, rng))
            .Replace("{PEOPLE_BLOCK}",      BuildPeopleBlock(eraProfile, sceneContent, peopleCount, rng))
            .Replace("{VEHICLES_BLOCK}",    BuildVehiclesBlock(vehicles, year, placement))
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
            CreatedAt:        DateTimeOffset.UtcNow.ToString("o"));
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

    private static string BuildSceneBlock(EraProfile era, SceneContent? content, string sceneType, Random rng)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"TRANSFORM TO {era.Year}");
        var intro = TruncateToSentences(era.Description, 1);
        sb.AppendLine(content is null ? intro : $"{intro} {content.Narrative}");
        sb.AppendLine();
        sb.AppendLine("PERIOD DETAILS");

        var isGasStation = sceneType == "gas_station";
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
            var brands = era.Business.GasBrands;
            if (brands is { Count: > 0 })
                sb.AppendLine($"- main sign: an independent station sign in the style of {brands[rng.Next(brands.Count)]}");
        }

        sb.AppendLine($"Typography: {era.Business.Signage.TypographyStyle}.");
        sb.Append("Sign text is only what appears in quotes — do not turn other words from this prompt into signage.");
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

        sb.Append($"Typography: {era.Business.Signage.TypographyStyle}.");
        return sb.ToString();
    }

    private static string BuildPeopleBlock(EraProfile era, SceneContent? content, int peopleCount, Random rng)
    {
        var sb = new StringBuilder();
        sb.AppendLine("PEOPLE");
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

    private static string BuildVehiclesBlock(IReadOnlyList<(string Model, string? Color)> vehicles, int year, string placement)
    {
        var sb = new StringBuilder();
        sb.AppendLine("VEHICLES");
        sb.AppendLine($"EXACTLY {vehicles.Count} period vehicles, all different:");
        foreach (var (model, color) in vehicles)
            sb.AppendLine(color is null ? $"- {model}" : $"- {model} — {color}");
        sb.AppendLine($"Parked with gaps; no vehicle newer than {year}.");
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
