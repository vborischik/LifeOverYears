using System.Text.Json;
using System.Text.Json.Serialization;
using LifeOverYears.Models;
using LifeOverYears.Services.Interfaces;
using Microsoft.Extensions.Logging;

namespace LifeOverYears.Providers;

public sealed class VisionProvider : IVisionProvider
{
    private readonly INvidiaProvider _nvidia;
    private readonly ILogger<VisionProvider> _logger;

    private const string Url   = "https://integrate.api.nvidia.com/v1/chat/completions";
    private const string Model = "nvidia/nemotron-3-nano-omni-30b-a3b-reasoning";

    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    public VisionProvider(INvidiaProvider nvidia, ILogger<VisionProvider> logger)
    {
        _nvidia = nvidia;
        _logger = logger;
    }

    public async Task<SceneDna> AnalyzeImageAsync(string photoPath, string prompt)
    {
        _logger.LogInformation("Analyzing image: {Path}", photoPath);

        var b64 = Convert.ToBase64String(await File.ReadAllBytesAsync(photoPath));
        var ext = Path.GetExtension(photoPath).TrimStart('.').ToLower();
        var mimeType = ext is "png" ? "image/png" : "image/jpeg";

        var body = new
        {
            model = Model,
            messages = new object[]
            {
                new
                {
                    role = "user",
                    content = new object[]
                    {
                        new { type = "text", text = prompt },
                        new { type = "image_url", image_url = new { url = $"data:{mimeType};base64,{b64}" } }
                    }
                }
            },
            temperature          = 0.6,
            top_p                = 0.95,
            max_tokens           = 65536,
            chat_template_kwargs = new { enable_thinking = false },
            reasoning_budget     = 0
        };

        var json = await _nvidia.PostAsync(Url, body);
        var text = ExtractContent(json);
        return ParseSceneDna(text, _logger);
    }

    public async Task<SceneDna> EnrichAsync(string photoPath, SceneDna current, IReadOnlyList<string> missingFields)
    {
        _logger.LogInformation("Enriching SceneDna {Id}, missing: {Fields}", current.Id, string.Join(", ", missingFields));

        var b64 = Convert.ToBase64String(await File.ReadAllBytesAsync(photoPath));
        var ext = Path.GetExtension(photoPath).TrimStart('.').ToLower();
        var mimeType = ext is "png" ? "image/png" : "image/jpeg";

        var currentJson = JsonSerializer.Serialize(current, JsonOpts);
        var fieldsList  = string.Join(", ", missingFields);
        var enrichPrompt = $"""
            The following fields are missing or have default values: {fieldsList}.
            Current SceneDna: {currentJson}
            Analyze the photo again and return ONLY the corrected JSON with all fields filled in.
            """;

        var body = new
        {
            model = Model,
            messages = new object[]
            {
                new
                {
                    role = "user",
                    content = new object[]
                    {
                        new { type = "text", text = enrichPrompt },
                        new { type = "image_url", image_url = new { url = $"data:{mimeType};base64,{b64}" } }
                    }
                }
            },
            temperature          = 0.6,
            top_p                = 0.95,
            max_tokens           = 65536,
            chat_template_kwargs = new { enable_thinking = false },
            reasoning_budget     = 0
        };

        var json = await _nvidia.PostAsync(Url, body);
        var text = ExtractContent(json);

        var enriched  = ParseSceneDna(text, _logger);
        var sceneType = missingFields.Contains("scene_type") ? enriched.SceneType : current.SceneType;
        return enriched with { Id = current.Id, CreatedAt = current.CreatedAt, SceneType = sceneType };
    }

    private static string ExtractContent(string json)
    {
        var msg = JsonDocument.Parse(json)
            .RootElement
            .GetProperty("choices")[0]
            .GetProperty("message");

        if (msg.TryGetProperty("content", out var content) && content.ValueKind != JsonValueKind.Null)
            return content.GetString()?.Trim() ?? "{}";

        return "{}";
    }

    private static SceneDna ParseSceneDna(string text, ILogger logger)
    {
        try
        {
            var clean = text.Trim();
            if (clean.StartsWith("```"))
            {
                var start = clean.IndexOf('{');
                var end   = clean.LastIndexOf('}');
                if (start >= 0 && end > start)
                    clean = clean[start..(end + 1)];
            }

            var dto = JsonSerializer.Deserialize<SceneDnaDto>(clean, JsonOpts);

            var camera = new Camera(
                Height:    dto?.Camera?.Height    ?? "eye-level",
                Direction: dto?.Camera?.Direction ?? "street",
                Fov:       dto?.Camera?.Fov       ?? 90);

            var roads = (dto?.Geometry?.Roads ?? [])
                .Select(r => new Road(
                    Type:     r.Type     ?? "unknown",
                    Lanes:    r.Lanes    ?? 1,
                    Markings: r.Markings ?? [],
                    Surface:  r.Surface  ?? "asphalt"))
                .ToList();

            var buildings = (dto?.Geometry?.Buildings ?? [])
                .Select(b => new Building(
                    Type:      b.Type      ?? "unknown",
                    Position:  b.Position  ?? "unknown",
                    Stories:   b.Stories   ?? 1,
                    Materials: b.Materials ?? [],
                    Roof:      b.Roof      ?? "unknown",
                    Setback:   b.Setback   ?? "unknown"))
                .ToList();

            var geometry = new Geometry(
                Roads:     roads,
                Sidewalks: dto?.Geometry?.Sidewalks ?? false,
                Curbs:     dto?.Geometry?.Curbs     ?? false,
                Buildings: buildings,
                Driveways: dto?.Geometry?.Driveways ?? [],
                Parking:   dto?.Geometry?.Parking   ?? "none");

            var trees = (dto?.Environment?.Trees ?? [])
                .Select(t => new Tree(
                    Position: t.Position ?? "unknown",
                    Size:     t.Size     ?? "unknown",
                    Type:     t.Type     ?? "unknown"))
                .ToList();

            var environment = new Environment(
                Terrain:   dto?.Environment?.Terrain   ?? "urban",
                Utilities: dto?.Environment?.Utilities ?? [],
                Trees:     trees,
                Landscape: dto?.Environment?.Landscape ?? []);

            return new SceneDna(
                Id:                Guid.NewGuid().ToString(),
                CreatedAt:         DateTimeOffset.UtcNow.ToString("o"),
                SceneType:         dto?.SceneType ?? "unknown",
                Camera:            camera,
                Geometry:          geometry,
                Environment:       environment,
                ImmutableElements: dto?.ImmutableElements ?? []);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "ParseSceneDna failed. Raw text: {Text}", text);
            return new SceneDna(
                Id:                Guid.NewGuid().ToString(),
                CreatedAt:         DateTimeOffset.UtcNow.ToString("o"),
                SceneType:         "unknown",
                Camera:            new Camera("eye-level", "street", 90),
                Geometry:          new Geometry([], false, false, [], [], "none"),
                Environment:       new Environment("urban", [], [], []),
                ImmutableElements: []);
        }
    }

    private record SceneDnaDto(
        [property: JsonPropertyName("scene_type")]         string?         SceneType,
        [property: JsonPropertyName("camera")]             CameraDto?      Camera,
        [property: JsonPropertyName("geometry")]           GeometryDto?    Geometry,
        [property: JsonPropertyName("environment")]        EnvironmentDto? Environment,
        [property: JsonPropertyName("immutable_elements")] List<string>?   ImmutableElements);

    private record CameraDto(
        [property: JsonPropertyName("height")]    string? Height,
        [property: JsonPropertyName("direction")] string? Direction,
        [property: JsonPropertyName("fov")]       int?    Fov);

    private record RoadDto(
        [property: JsonPropertyName("type")]     string?       Type,
        [property: JsonPropertyName("lanes")]    int?          Lanes,
        [property: JsonPropertyName("markings")] List<string>? Markings,
        [property: JsonPropertyName("surface")]  string?       Surface);

    private record GeometryDto(
        [property: JsonPropertyName("roads")]     List<RoadDto>?     Roads,
        [property: JsonPropertyName("sidewalks")] bool               Sidewalks,
        [property: JsonPropertyName("curbs")]     bool               Curbs,
        [property: JsonPropertyName("buildings")] List<BuildingDto>? Buildings,
        [property: JsonPropertyName("driveways")] List<string>?      Driveways,
        [property: JsonPropertyName("parking")]   string?            Parking);

    private record BuildingDto(
        [property: JsonPropertyName("type")]      string?       Type,
        [property: JsonPropertyName("position")]  string?       Position,
        [property: JsonPropertyName("stories")]   int?          Stories,
        [property: JsonPropertyName("materials")] List<string>? Materials,
        [property: JsonPropertyName("roof")]      string?       Roof,
        [property: JsonPropertyName("setback")]   string?       Setback);

    private record TreeDto(
        [property: JsonPropertyName("position")] string? Position,
        [property: JsonPropertyName("size")]     string? Size,
        [property: JsonPropertyName("type")]     string? Type);

    private record EnvironmentDto(
        [property: JsonPropertyName("terrain")]   string?        Terrain,
        [property: JsonPropertyName("utilities")] List<string>?  Utilities,
        [property: JsonPropertyName("trees")]     List<TreeDto>? Trees,
        [property: JsonPropertyName("landscape")] List<string>?  Landscape);
}
