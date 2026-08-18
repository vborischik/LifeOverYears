using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using LifeOverYears.Models;
using LifeOverYears.Services.Interfaces;
using Microsoft.Extensions.Logging;
using Environment = LifeOverYears.Models.Environment;

namespace LifeOverYears.Providers;

public sealed class VisionProvider : IVisionProvider
{
    private readonly INvidiaProvider _nvidia;
    private readonly ILogger<VisionProvider> _logger;

    private const string Url   = "https://integrate.api.nvidia.com/v1/chat/completions";
     private const string Model = "nvidia/nemotron-3-nano-omni-30b-a3b-reasoning";
    // private const string Model = "nvidia/nemotron-3-nano-omni-30b-a3b-reasoning";
    //private const string Model = "nvidia/nemotron-3-nano-omni-30b-a3b-reasoning";

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
                    // Image before text, matching the model card. The model is
                    // sensitive to this ordering: with the prompt first it reads
                    // as answering from the schema, with the image first as
                    // describing what it sees.
                    content = new object[]
                    {
                        new { type = "image_url", image_url = new { url = $"data:{mimeType};base64,{b64}" } },
                        new { type = "text", text = prompt }
                    }
                }
            },
            // Every value below matches the vendor's Python sample verbatim.
            temperature      = 0.2,
            top_p            = 0.95,
            max_tokens       = 65536,
            seed             = 12,
            // The sample pairs a 16384 reasoning budget with thinking switched
            // off, which looks contradictory but is what the vendor ships and
            // what is known to work — the budget is the allocation, the kwarg is
            // the switch. Do not "simplify" one away without retesting.
            reasoning_budget = 16384,
            chat_template_kwargs = new { enable_thinking = false },
            // Streamed: a non-streaming call makes the gateway hold the
            // connection for the whole generation, which is how a long reasoning
            // answer turns into a 502 that says nothing about the request.
            stream           = true
        };

        var chunks = await _nvidia.PostStreamAsync(Url, body);
        var text   = ExtractStreamedContent(chunks);
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
                    // Image first, same as AnalyzeImageAsync above.
                    content = new object[]
                    {
                        new { type = "image_url", image_url = new { url = $"data:{mimeType};base64,{b64}" } },
                        new { type = "text", text = enrichPrompt }
                    }
                }
            },
            // Every value below matches the vendor's Python sample verbatim.
            temperature      = 0.2,
            top_p            = 0.95,
            max_tokens       = 65536,
            seed             = 12,
            // The sample pairs a 16384 reasoning budget with thinking switched
            // off, which looks contradictory but is what the vendor ships and
            // what is known to work — the budget is the allocation, the kwarg is
            // the switch. Do not "simplify" one away without retesting.
            reasoning_budget = 16384,
            chat_template_kwargs = new { enable_thinking = false },
            // Streamed: a non-streaming call makes the gateway hold the
            // connection for the whole generation, which is how a long reasoning
            // answer turns into a 502 that says nothing about the request.
            stream           = true
        };

        var chunks = await _nvidia.PostStreamAsync(Url, body);
        var text   = ExtractStreamedContent(chunks);

        var enriched  = ParseSceneDna(text, _logger);
        var sceneType = missingFields.Contains("scene_type") ? enriched.SceneType : current.SceneType;
        return enriched with { Id = current.Id, CreatedAt = current.CreatedAt, SceneType = sceneType };
    }

    // A streamed completion arrives as deltas: every chunk carries the next
    // fragment of the answer in choices[0].delta.content, and the answer only
    // exists once they are concatenated in order. Reasoning tokens come back on
    // a separate delta field (reasoning_content) and are deliberately dropped —
    // ParseSceneDna wants the answer, and an inline <think> block it strips
    // itself. Chunks that carry only a role or a finish_reason have no content
    // and contribute nothing.
    private static string ExtractStreamedContent(IReadOnlyList<string> chunks)
    {
        var sb = new StringBuilder();

        foreach (var chunk in chunks)
        {
            using var doc = JsonDocument.Parse(chunk);
            if (!doc.RootElement.TryGetProperty("choices", out var choices)
                || choices.ValueKind != JsonValueKind.Array
                || choices.GetArrayLength() == 0)
                continue;

            if (!choices[0].TryGetProperty("delta", out var delta))
                continue;

            if (delta.TryGetProperty("content", out var content)
                && content.ValueKind == JsonValueKind.String)
                sb.Append(content.GetString());
        }

        return sb.ToString().Trim();
    }

    private static SceneDna ParseSceneDna(string text, ILogger logger)
    {
        try
        {
            var clean = text.Trim();

            // Reasoning models may emit a <think>...</think> block before the answer.
            var thinkEnd = clean.LastIndexOf("</think>", StringComparison.OrdinalIgnoreCase);
            if (thinkEnd >= 0)
                clean = clean[(thinkEnd + "</think>".Length)..];

            // Keep only the JSON object, dropping code fences or surrounding prose.
            var start = clean.IndexOf('{');
            var end   = clean.LastIndexOf('}');
            if (start >= 0 && end > start)
                clean = clean[start..(end + 1)];

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

            var composition = dto?.Composition is null
                ? null
                : new Composition(
                    SubjectDistance: dto.Composition.SubjectDistance ?? "mid",
                    FrameShare:      dto.Composition.FrameShare      ?? "moderate",
                    Horizon:         dto.Composition.Horizon         ?? "middle");

            return new SceneDna(
                Id:                Guid.NewGuid().ToString(),
                CreatedAt:         DateTimeOffset.UtcNow.ToString("o"),
                SceneType:         dto?.SceneType ?? "unknown",
                Camera:            camera,
                Geometry:          geometry,
                Environment:       environment,
                ImmutableElements: dto?.ImmutableElements ?? [],
                Composition:       composition,
                Distinctive:       dto?.Distinctive ?? []);
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
        [property: JsonPropertyName("immutable_elements")] List<string>?   ImmutableElements,
        [property: JsonPropertyName("composition")]        CompositionDto? Composition,
        [property: JsonPropertyName("distinctive")]        List<string>?   Distinctive);

    private record CameraDto(
        [property: JsonPropertyName("height")]    string? Height,
        [property: JsonPropertyName("direction")] string? Direction,
        [property: JsonPropertyName("fov")]       int?    Fov);

    private record CompositionDto(
        [property: JsonPropertyName("subject_distance")]    string? SubjectDistance,
        [property: JsonPropertyName("subject_frame_share")] string? FrameShare,
        [property: JsonPropertyName("horizon")]             string? Horizon);

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
