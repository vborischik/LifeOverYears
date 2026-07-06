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

        var vehicles = PickVehicles(eraProfile, context, vehicleCount, _logger);

        var text = template
            .Replace("{PRESERVE_BLOCK}",    BuildPreserveBlock(sceneDna))
            .Replace("{SCENE_BLOCK}",       BuildSceneBlock(eraProfile, sceneContent, sceneType, rng))
            .Replace("{PEOPLE_BLOCK}",      BuildPeopleBlock(eraProfile, sceneContent, peopleCount, rng))
            .Replace("{VEHICLES_BLOCK}",    BuildVehiclesBlock(vehicles, year))
            .Replace("{ENVIRONMENT_BLOCK}", BuildEnvironmentBlock(sceneDna, eraProfile, year, rng))
            .Replace("{STYLE_BLOCK}",       BuildStyleBlock(eraProfile.Photography))
            .Replace("{REJECT_BLOCK}",      BuildRejectBlock(eraProfile, year, peopleCount, vehicleCount))
            .Replace("{YEAR}",              year.ToString());

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
        sb.AppendLine($"- camera: {s.Camera.Height}, facing {s.Camera.Direction}, fov {s.Camera.Fov}");
        foreach (var r in s.Geometry.Roads)
            sb.AppendLine($"- {r.Type} road, {r.Lanes}-lane, {r.Surface}");
        sb.AppendLine($"- sidewalks {(s.Geometry.Sidewalks ? "present" : "absent")}, curbs {(s.Geometry.Curbs ? "present" : "absent")}");
        sb.AppendLine($"- driveways: {Join(s.Geometry.Driveways)}; parking: {s.Geometry.Parking}");
        foreach (var b in s.Geometry.Buildings)
            sb.AppendLine($"- {b.Type} building at {b.Position}, {b.Stories} stories, {Join(b.Materials)}, {b.Roof} roof, {b.Setback} setback");
        sb.AppendLine($"- utilities: {Join(s.Environment.Utilities)}");
        sb.AppendLine($"- landscape: {Join(s.Environment.Landscape)}");
        sb.AppendLine($"- immutable elements: {Join(s.ImmutableElements)}");
        sb.Append("The location must remain instantly recognizable as the same place. Do not redesign, relocate, or reinterpret any structure.");
        return sb.ToString();
    }

    private static string BuildSceneBlock(EraProfile era, SceneContent? content, string sceneType, Random rng)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"TRANSFORM TO {era.Year}");
        sb.AppendLine(content is null ? era.Description : $"{era.Description} {content.Narrative}");
        sb.AppendLine();
        sb.AppendLine("STOREFRONTS & PERIOD SIGNAGE");

        if (content is null)
        {
            foreach (var item in era.Architecture.Commercial.Characteristics)
                sb.AppendLine($"- {item}");
            foreach (var item in era.Business.Signage.Characteristics)
                sb.AppendLine($"- {item}");
        }
        else
        {
            foreach (var item in Sample(content.Storefronts, rng.Next(3, 6), rng))
                sb.AppendLine($"- {item}");
            foreach (var item in Sample(content.WindowSigns, rng.Next(2, 4), rng))
                sb.AppendLine($"- window sign: \"{item}\"");
            foreach (var item in content.Extras)
                sb.AppendLine($"- {item}");
        }

        sb.AppendLine($"Signage typography: {era.Business.Signage.TypographyStyle}.");
        sb.Append("Only include as many businesses as naturally fit the actual building frontage in the source. All signage modest and local in scale, not cluttered.");

        if (sceneType == "gas_station")
        {
            sb.AppendLine();
            sb.AppendLine();
            sb.AppendLine("GAS STATION DETAILS");
            foreach (var item in era.Architecture.GasStations.Characteristics)
                sb.AppendLine($"- {item}");
            sb.Append($"- price sign showing gas around {era.Transportation.Fuel.AveragePricePerGallon}");
        }

        return sb.ToString();
    }

    private static string BuildPeopleBlock(EraProfile era, SceneContent? content, int peopleCount, Random rng)
    {
        var sb = new StringBuilder();
        sb.AppendLine("PEOPLE");
        sb.AppendLine($"EXACTLY {peopleCount} people TOTAL in the entire frame, no more. Distributed naturally with clear open space between groups; parts of the scene remain OPEN and empty — emptiness is natural and required.");

        if (content is not null)
            foreach (var activity in Sample(content.PeopleActivities, rng.Next(4, 7), rng))
                sb.AppendLine($"- {activity}");

        var fashion = era.Society.Fashion;
        var men     = Sample(fashion.Men, 3, rng);
        var women   = Sample(fashion.Women, 3, rng);
        sb.AppendLine($"Clothing: men in {string.Join(", ", men)}; women in {string.Join(", ", women)}. Fashion palette: {Join(fashion.Colors)}.");
        sb.Append("No posing, no eye contact with camera.");
        return sb.ToString();
    }

    private static List<(string Model, string Color)> PickVehicles(
        EraProfile era, GenerationContext context, int vehicleCount, ILogger logger)
    {
        var cars     = era.Transportation.Cars;
        var fullPool = cars.SpecificModels.Concat(era.Transportation.Trucks.SpecificModels).Distinct().ToList();
        var pool     = fullPool.Where(m => !context.UsedCarModels.Contains(m)).ToList();

        if (pool.Count < vehicleCount)
        {
            logger.LogWarning(
                "Vehicle pool exhausted for year {Year}: {Unused} unused of {Total}, need {Count} — topping up from full list",
                era.Year, pool.Count, fullPool.Count, vehicleCount);
            pool = pool.Concat(fullPool.Except(pool)).ToList();
        }

        var rng    = context.Random;
        var picks  = Sample(pool, vehicleCount, rng);
        var result = new List<(string, string)>();
        foreach (var model in picks)
        {
            context.UsedCarModels.Add(model);
            var color = cars.Colors.Count > 0
                ? cars.Colors[rng.Next(cars.Colors.Count)]
                : "period-correct color";
            result.Add((model, color));
        }
        return result;
    }

    private static string BuildVehiclesBlock(IReadOnlyList<(string Model, string Color)> vehicles, int year)
    {
        var sb = new StringBuilder();
        sb.AppendLine("VEHICLES");
        sb.AppendLine($"EXACTLY {vehicles.Count} period vehicles total, each a DIFFERENT make/model:");
        foreach (var (model, color) in vehicles)
            sb.AppendLine($"- {model} — {color}");
        sb.Append($"Parked along the curb with EMPTY spaces between some of them — not wall-to-wall cars; optionally 1 vehicle driving; no vehicle newer than {year}; these exact models only.");
        return sb.ToString();
    }

    private static string BuildEnvironmentBlock(SceneDna scene, EraProfile era, int year, Random rng)
    {
        var infra = era.Infrastructure;
        var sb = new StringBuilder();
        sb.AppendLine("ENVIRONMENT & STREET DETAIL");
        sb.AppendLine($"- road markings: {Join(infra.Roads.Markings)}");
        sb.AppendLine($"- road materials: {Join(infra.Roads.Materials)}");
        foreach (var item in Sample(infra.StreetFurniture.Items, rng.Next(3, 5), rng))
            sb.AppendLine($"- {item}");
        sb.AppendLine($"- utilities: {Join(infra.Utilities.Characteristics)}");
        sb.AppendLine($"- lighting: street — {era.Environment.Lighting.StreetLights}; commercial — {era.Environment.Lighting.Commercial}; signs — {era.Environment.Lighting.Signs}");
        sb.AppendLine($"- vegetation: {Join(era.Environment.Vegetation.Characteristics)}");
        sb.AppendLine();
        sb.AppendLine("TREES");
        foreach (var tree in scene.Environment.Trees)
            sb.AppendLine($"- {tree.Type} tree at {tree.Position}: {DescribeTreeSize(year)}");
        sb.Append("Tree positions and species NEVER change across eras.");
        return sb.ToString();
    }

    private static string DescribeTreeSize(int year) => (2025 - year) switch
    {
        >= 40 => "very young sapling, recently planted",
        >= 30 => "young and small",
        >= 20 => "noticeably smaller and slimmer than in the source photo",
        >= 10 => "slightly smaller than in the source photo",
        _     => "same size as in the source photo"
    };

    private static string BuildStyleBlock(Photography photo)
    {
        var sb = new StringBuilder();
        sb.AppendLine("PHOTOGRAPHIC STYLE");
        if (photo.ColorMode == "black_and_white")
        {
            sb.AppendLine("STRICTLY BLACK AND WHITE — a true monochrome archival photograph, zero color anywhere in the image.");
            sb.AppendLine($"Grain: {photo.Grain}. Style: {photo.Style}.");
            sb.Append("Photorealistic, like a preserved frame from a local newspaper's photo archive; slight softness typical of period lenses — not digitally sharp.");
        }
        else
        {
            sb.AppendLine($"COLOR photograph — styled as if shot on {photo.FilmStock}.");
            sb.AppendLine($"Color: {Join(photo.ColorCharacteristics)}. Grain: {photo.Grain}. Style: {photo.Style}.");
            sb.Append("Photorealistic — NOT black-and-white, NOT digitally clean, no HDR.");
        }
        return sb.ToString();
    }

    private static string BuildRejectBlock(EraProfile era, int year, int peopleCount, int vehicleCount)
    {
        var colorRule = era.Photography.ColorMode == "black_and_white"
            ? "any color in the image;"
            : "black-and-white rendering;";

        var absent = era.Transportation.Cars.Absent.Take(3)
            .Concat(era.Business.AbsentBrands.Take(3))
            .Select(item => $"no {item}");

        return $"REJECT IF: the output is a collage, grid, triptych, photo strip, split panels, " +
               $"or contains ANY visible frame divisions; the year \"{year}\" appears more than once; " +
               $"more than {peopleCount} people; more than {vehicleCount} vehicles; " +
               $"any vehicle newer than {year} or not from the listed models; " +
               $"altered building footprint, roofline, or street alignment; {colorRule} " +
               $"modern signage, vehicles, or clothing; {string.Join("; ", absent)}.";
    }

    private static List<string> Sample(IEnumerable<string> pool, int count, Random rng) =>
        pool.Distinct().OrderBy(_ => rng.Next()).Take(count).ToList();

    private static string Join(IReadOnlyList<string> list) =>
        list.Count > 0 ? string.Join(", ", list) : "none";
}
