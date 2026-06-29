using System.Text;
using LifeOverYears.Models;
using LifeOverYears.Services.Interfaces;
using Microsoft.Extensions.Logging;

namespace LifeOverYears.Services;

public sealed class PromptService : IPromptService
{
    private readonly IPromptProvider _promptProvider;
    private readonly IDataService _data;
    private readonly ILogger<PromptService> _logger;

    public PromptService(IPromptProvider promptProvider, IDataService data, ILogger<PromptService> logger)
    {
        _promptProvider = promptProvider;
        _data = data;
        _logger = logger;
    }

    public async Task<Prompt> BuildAsync(SceneDna sceneDna, EraProfile eraProfile)
    {
        _logger.LogInformation("Building prompt for SceneDna {Id}, year {Year}", sceneDna.Id, eraProfile.Year);

        var template = await _data.LoadPromptAsync("prompt-builder");
        var context  = BuildContext(sceneDna, eraProfile);
        var text     = await _promptProvider.GeneratePromptAsync(template + "\n\n" + context);

        return new Prompt(
            Id:         Guid.NewGuid().ToString(),
            SceneDnaId: sceneDna.Id,
            Year:       eraProfile.Year,
            Text:       text,
            CreatedAt:  DateTimeOffset.UtcNow.ToString("o"));
    }

    private static string BuildContext(SceneDna s, EraProfile e)
    {
        var sb = new StringBuilder();

        sb.AppendLine("=== SCENE (SceneDna) ===");
        sb.AppendLine($"Camera: height={s.Camera.Height}, direction={s.Camera.Direction}, fov={s.Camera.Fov}");
        sb.AppendLine($"Roads: {Join(s.Geometry.Roads)}");
        sb.AppendLine($"Sidewalks: {s.Geometry.Sidewalks}");
        sb.AppendLine($"Buildings: {string.Join(", ", s.Geometry.Buildings.Select(b => $"{b.Type} ({b.Position})"))}");
        sb.AppendLine($"Terrain: {s.Environment.Terrain}");
        sb.AppendLine($"Utilities: {Join(s.Environment.Utilities)}");
        sb.AppendLine($"Immutable elements: {Join(s.ImmutableElements)}");

        sb.AppendLine();
        sb.AppendLine($"=== ERA: {e.Year} — {e.Label} ===");
        sb.AppendLine(e.Description);

        sb.AppendLine();
        sb.AppendLine("--- CARS ---");
        sb.AppendLine($"Models present: {Join(e.Transportation.Cars.SpecificModels)}");
        sb.AppendLine($"Absent: {Join(e.Transportation.Cars.Absent)}");
        sb.AppendLine($"Body styles: {Join(e.Transportation.Cars.BodyStyles)}");
        sb.AppendLine($"Colors: {Join(e.Transportation.Cars.Colors)}");

        sb.AppendLine();
        sb.AppendLine("--- TRUCKS ---");
        sb.AppendLine($"Models present: {Join(e.Transportation.Trucks.SpecificModels)}");

        sb.AppendLine();
        sb.AppendLine("--- ARCHITECTURE (Commercial) ---");
        sb.AppendLine($"Styles: {Join(e.Architecture.Commercial.Styles)}");
        sb.AppendLine($"Materials: {Join(e.Architecture.Commercial.Materials)}");
        sb.AppendLine($"Characteristics: {Join(e.Architecture.Commercial.Characteristics)}");

        sb.AppendLine();
        sb.AppendLine("--- GAS STATIONS ---");
        sb.AppendLine(Join(e.Architecture.GasStations.Characteristics));

        sb.AppendLine();
        sb.AppendLine("--- BUSINESS ---");
        sb.AppendLine($"Active brands: {Join(e.Business.ActiveBrands)}");
        sb.AppendLine($"Absent brands: {Join(e.Business.AbsentBrands)}");
        sb.AppendLine($"Signage: {Join(e.Business.Signage.Characteristics)}");
        sb.AppendLine($"Typography: {e.Business.Signage.TypographyStyle}");

        sb.AppendLine();
        sb.AppendLine("--- INFRASTRUCTURE ---");
        sb.AppendLine($"Road markings: {Join(e.Infrastructure.Roads.Markings)}");
        sb.AppendLine($"Road materials: {Join(e.Infrastructure.Roads.Materials)}");
        sb.AppendLine($"Road characteristics: {Join(e.Infrastructure.Roads.Characteristics)}");
        sb.AppendLine($"Traffic signs: {e.Infrastructure.TrafficSigns.Style} — {Join(e.Infrastructure.TrafficSigns.Characteristics)}");
        sb.AppendLine($"Street furniture: {Join(e.Infrastructure.StreetFurniture.Items)}");
        sb.AppendLine($"Utilities: {Join(e.Infrastructure.Utilities.Characteristics)}");

        sb.AppendLine();
        sb.AppendLine("--- SOCIETY ---");
        sb.AppendLine($"Men fashion: {Join(e.Society.Fashion.Men)}");
        sb.AppendLine($"Women fashion: {Join(e.Society.Fashion.Women)}");
        sb.AppendLine($"Fashion colors: {Join(e.Society.Fashion.Colors)}");
        sb.AppendLine($"Advertising: {e.Society.Advertising.Style}");
        sb.AppendLine($"Ad media: {Join(e.Society.Advertising.Media)}");

        sb.AppendLine();
        sb.AppendLine("--- ENVIRONMENT ---");
        sb.AppendLine($"Street lights: {e.Environment.Lighting.StreetLights}");
        sb.AppendLine($"Commercial lighting: {e.Environment.Lighting.Commercial}");
        sb.AppendLine($"Signs lighting: {e.Environment.Lighting.Signs}");
        sb.AppendLine($"Vegetation: {Join(e.Environment.Vegetation.Characteristics)}");

        sb.AppendLine();
        sb.AppendLine("--- PHOTOGRAPHY ---");
        sb.AppendLine($"Film stock: {e.Photography.FilmStock}");
        sb.AppendLine($"Color: {Join(e.Photography.ColorCharacteristics)}");
        sb.AppendLine($"Grain: {e.Photography.Grain}");
        sb.AppendLine($"Style: {e.Photography.Style}");

        return sb.ToString();
    }

    private static string Join(IReadOnlyList<string> list) =>
        list.Count > 0 ? string.Join(", ", list) : "none";
}
