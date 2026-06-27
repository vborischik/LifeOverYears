using System.Text.Json;
using System.Text.Json.Serialization;
using LifeOverYears.Models;
using LifeOverYears.Services.Interfaces;
using Microsoft.Extensions.Logging;
using SceneEnvironment = LifeOverYears.Models.Environment;

namespace LifeOverYears.Providers;

public sealed class VisionProvider : IVisionProvider
{
    private readonly INvidiaProvider _nvidia;
    private readonly ILogger<VisionProvider> _logger;

    private const string Url   = "https://ai.api.nvidia.com/v1/chat/completions";
    private const string Model = "meta/llama-3.2-90b-vision-instruct";

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

        var systemPrompt = """
            You are a scene analysis AI. Analyze the street photo and return ONLY a JSON object matching this schema:
            {
              "camera": { "height": string, "direction": string, "fov": number },
              "geometry": {
                "roads": [string],
                "sidewalks": bool,
                "buildings": [{ "type": string, "position": string }]
              },
              "environment": { "terrain": string, "utilities": [string] },
              "immutable_elements": [string]
            }
            """;

        var body = new
        {
            model = Model,
            messages = new object[]
            {
                new { role = "system", content = systemPrompt },
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
            max_tokens = 1024,
            temperature = 0.1
        };

        var json = await _nvidia.PostAsync(Url, body);

        var text = JsonDocument.Parse(json)
            .RootElement
            .GetProperty("choices")[0]
            .GetProperty("message")
            .GetProperty("content")
            .GetString() ?? "{}";

        return ParseSceneDna(text);
    }

    private static SceneDna ParseSceneDna(string text)
    {
        var json = text.Trim();
        if (json.StartsWith("```")) json = json[(json.IndexOf('\n') + 1)..];
        if (json.EndsWith("```"))   json = json[..json.LastIndexOf("```")].TrimEnd();

        try
        {
            var dto = JsonSerializer.Deserialize<SceneDnaDto>(json, JsonOpts);

            var camera = new Camera(
                Height:    dto?.Camera?.Height    ?? "eye-level",
                Direction: dto?.Camera?.Direction ?? "street",
                Fov:       dto?.Camera?.Fov       ?? 90);

            var buildings = (dto?.Geometry?.Buildings ?? [])
                .Select(b => new Building(b.Type ?? "unknown", b.Position ?? "unknown"))
                .ToList();

            var geometry = new Geometry(
                Roads:     dto?.Geometry?.Roads     ?? [],
                Sidewalks: dto?.Geometry?.Sidewalks ?? false,
                Buildings: buildings);

            var environment = new SceneEnvironment(
                Terrain:   dto?.Environment?.Terrain   ?? "urban",
                Utilities: dto?.Environment?.Utilities ?? []);

            return new SceneDna(
                Id:                Guid.NewGuid().ToString(),
                CreatedAt:         DateTimeOffset.UtcNow.ToString("o"),
                Camera:            camera,
                Geometry:          geometry,
                Environment:       environment,
                ImmutableElements: dto?.ImmutableElements ?? []);
        }
        catch
        {
            return new SceneDna(
                Id:                Guid.NewGuid().ToString(),
                CreatedAt:         DateTimeOffset.UtcNow.ToString("o"),
                Camera:            new Camera("eye-level", "street", 90),
                Geometry:          new Geometry([], false, []),
                Environment:       new SceneEnvironment("urban", []),
                ImmutableElements: []);
        }
    }

    private record SceneDnaDto(
        [property: JsonPropertyName("camera")]             CameraDto?      Camera,
        [property: JsonPropertyName("geometry")]           GeometryDto?    Geometry,
        [property: JsonPropertyName("environment")]        EnvironmentDto? Environment,
        [property: JsonPropertyName("immutable_elements")] List<string>?   ImmutableElements);

    private record CameraDto(
        [property: JsonPropertyName("height")]    string? Height,
        [property: JsonPropertyName("direction")] string? Direction,
        [property: JsonPropertyName("fov")]       int?    Fov);

    private record GeometryDto(
        [property: JsonPropertyName("roads")]     List<string>?      Roads,
        [property: JsonPropertyName("sidewalks")] bool               Sidewalks,
        [property: JsonPropertyName("buildings")] List<BuildingDto>? Buildings);

    private record BuildingDto(
        [property: JsonPropertyName("type")]     string? Type,
        [property: JsonPropertyName("position")] string? Position);

    private record EnvironmentDto(
        [property: JsonPropertyName("terrain")]   string?       Terrain,
        [property: JsonPropertyName("utilities")] List<string>? Utilities);
}
