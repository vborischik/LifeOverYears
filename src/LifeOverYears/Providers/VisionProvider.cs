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

        return ParseSceneDna(text);
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

        var enriched = ParseSceneDna(text);
        return enriched with { Id = current.Id, CreatedAt = current.CreatedAt };
    }

    private static string ExtractContent(string json)
    {
        var msg = JsonDocument.Parse(json)
            .RootElement
            .GetProperty("choices")[0]
            .GetProperty("message");

        // content is null when model is in thinking mode — fall back to reasoning_content
        if (msg.TryGetProperty("content", out var content) && content.ValueKind != JsonValueKind.Null)
            return content.GetString() ?? "{}";

        if (msg.TryGetProperty("reasoning_content", out var rc) && rc.ValueKind != JsonValueKind.Null)
            return rc.GetString() ?? "{}";

        return "{}";
    }

    private static SceneDna ParseSceneDna(string text)
    {
        try
        {
            var dto = JsonSerializer.Deserialize<SceneDnaDto>(text, JsonOpts);

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

    // DTOs без изменений
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