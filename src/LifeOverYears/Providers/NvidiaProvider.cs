using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using LifeOverYears.Models;
using LifeOverYears.Services.Interfaces;
using SceneEnvironment = LifeOverYears.Models.Environment;
using Microsoft.Extensions.Logging;

namespace LifeOverYears.Providers;

public sealed class NvidiaProvider : INvidiaProvider
{
    private readonly HttpClient _http;
    private readonly ILogger<NvidiaProvider> _logger;

    private const string VisionUrl  = "https://ai.api.nvidia.com/v1/chat/completions";
    private const string FluxUrl    = "https://ai.api.nvidia.com/v1/genai/black-forest-labs/flux-dev";
    private const string VisionModel = "meta/llama-3.2-90b-vision-instruct";

    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    public NvidiaProvider(HttpClient http, string apiKey, ILogger<NvidiaProvider> logger)
    {
        _http = http;
        _logger = logger;
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
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

        var body = JsonSerializer.Serialize(new
        {
            model = VisionModel,
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
        });

        var response = await _http.PostAsync(VisionUrl,
            new StringContent(body, Encoding.UTF8, "application/json"));
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync();
        var text = JsonDocument.Parse(json)
            .RootElement
            .GetProperty("choices")[0]
            .GetProperty("message")
            .GetProperty("content")
            .GetString() ?? "{}";

        return ParseSceneDna(text);
    }

    public async Task<HistoricalImage> GenerateImageAsync(Prompt prompt)
    {
        _logger.LogInformation("Generating image for year {Year}, prompt {Id}", prompt.Year, prompt.Id);

        var body = JsonSerializer.Serialize(new
        {
            prompt = prompt.Text,
            width = 1024,
            height = 1024,
            steps = 20,
            seed = 0
        });

        var response = await _http.PostAsync(FluxUrl,
            new StringContent(body, Encoding.UTF8, "application/json"));

        if (response.StatusCode == System.Net.HttpStatusCode.Accepted)
        {
            var pollUrl = response.Headers.Location?.ToString()
                ?? throw new InvalidOperationException("No polling URL in response");
            response = await PollAsync(pollUrl);
        }

        response.EnsureSuccessStatusCode();

        var imageBytes = ExtractImageBytes(await response.Content.ReadAsStringAsync());

        var dir = Path.Combine("output", "images", prompt.Year.ToString());
        Directory.CreateDirectory(dir);
        var filePath = Path.Combine(dir, $"{prompt.Id}.png");
        await File.WriteAllBytesAsync(filePath, imageBytes);

        return new HistoricalImage(
            Id:        Guid.NewGuid().ToString(),
            PromptId:  prompt.Id,
            Year:      prompt.Year,
            FilePath:  filePath,
            Provider:  "nvidia-flux",
            CreatedAt: DateTimeOffset.UtcNow.ToString("o"));
    }

    private async Task<HttpResponseMessage> PollAsync(string url)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(120);
        while (DateTimeOffset.UtcNow < deadline)
        {
            await Task.Delay(3000);
            var r = await _http.GetAsync(url);
            if (r.StatusCode != System.Net.HttpStatusCode.Accepted)
                return r;
        }
        throw new TimeoutException("NVIDIA generation timed out after 120s");
    }

    private static byte[] ExtractImageBytes(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        if (root.TryGetProperty("artifacts", out var artifacts) && artifacts.GetArrayLength() > 0)
            return Convert.FromBase64String(artifacts[0].GetProperty("base64").GetString()!);

        if (root.TryGetProperty("data", out var data) && data.GetArrayLength() > 0)
            return Convert.FromBase64String(data[0].GetProperty("b64_json").GetString()!);

        throw new InvalidOperationException("No image data in NVIDIA response");
    }

    private static SceneDna ParseSceneDna(string text)
    {
        // strip markdown code fences if present
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

    // DTOs for JSON deserialization
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
        [property: JsonPropertyName("roads")]     List<string>?    Roads,
        [property: JsonPropertyName("sidewalks")] bool             Sidewalks,
        [property: JsonPropertyName("buildings")] List<BuildingDto>? Buildings);

    private record BuildingDto(
        [property: JsonPropertyName("type")]     string? Type,
        [property: JsonPropertyName("position")] string? Position);

    private record EnvironmentDto(
        [property: JsonPropertyName("terrain")]   string?       Terrain,
        [property: JsonPropertyName("utilities")] List<string>? Utilities);
}
// 