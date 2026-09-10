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
            // Every value below matches the vendor's Python sample verbatim,
            // except the temperature.
            //
            // 0.0, not the sample's 0.2. Reading a photograph into a fixed
            // schema is extraction, not writing: the same photo should give the
            // same answer twice, and it did not — two runs over the same folder
            // disagreed about sidewalks and trees, which made the verification
            // pass impossible to judge, because it was re-examining different
            // input each time. Note this makes `vision-variance --repeat`
            // measure nothing on its own: repeat variance is what the old
            // setting bought, and a stable reading is worth more than a measure
            // of instability.
            temperature      = 0.0,
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

        return ParseSceneDna(await RequestContentAsync(body, $"analyze {Path.GetFileName(photoPath)}"), _logger);
    }

    // How many times a call that comes back with nothing is repeated before the
    // photo is given up on.
    private const int EmptyResponseAttempts = 3;

    // A 200 that streams a single contentless chunk is the failure this project
    // actually sees: the gateway accepts the request, returns one chunk in about
    // two seconds, and ExtractStreamedContent hands back "". Nothing downstream
    // treated that as an error — ParseSceneDna caught the JSON exception and
    // returned its "unknown" stub, which then read as "Vision could not classify
    // the photo" when in fact Vision never answered at all.
    //
    // Retried rather than failed outright because it is intermittent: the same
    // image on the same model answers normally on the next call. Logged loudly
    // either way, because a silent empty answer is what made this cost money for
    // weeks without anyone seeing a single error line.
    private async Task<string> RequestContentAsync(object body, string what)
    {
        for (var attempt = 1; attempt <= EmptyResponseAttempts; attempt++)
        {
            var chunks = await _nvidia.PostStreamAsync(Url, body);
            var text   = ExtractStreamedContent(chunks);
            if (!string.IsNullOrWhiteSpace(text))
                return text;

            _logger.LogWarning(
                "Vision returned no content for {What} — {Chunks} chunk(s), nothing in delta.content (attempt {Attempt}/{Total})",
                what, chunks.Count, attempt, EmptyResponseAttempts);
        }

        throw new InvalidOperationException(
            $"Vision returned an empty response {EmptyResponseAttempts} times for {what}. " +
            "Nothing was generated. This is an API-side empty stream, not a photo the model could not read.");
    }

    // The fields worth a second call. Each one either picks which content pool a
    // whole run draws from, or asserts a physical feature the model will draw if
    // it is claimed — and every one of them is a plain thing to see in a photo,
    // so a model asked about it directly does better than one filling in a
    // 1100-word schema. Free, so it runs on every photo.
    public async Task<SceneDna> VerifyAsync(string photoPath, SceneDna current)
    {
        _logger.LogInformation("Verifying SceneDna {Id}", current.Id);

        var b64 = Convert.ToBase64String(await File.ReadAllBytesAsync(photoPath));
        var ext = Path.GetExtension(photoPath).TrimStart('.').ToLower();
        var mimeType = ext is "png" ? "image/png" : "image/jpeg";

        var trees = current.Environment.Trees.Count == 0
            ? "none"
            : string.Join("; ", current.Environment.Trees.Select(t => $"{t.Size} {t.Type} at {t.Position}"));

        // Deliberately short and answerable. Parking is asked as a question about
        // the photograph rather than as an enum to fill: "none" was the value
        // that got mishandled downstream and it is also the one a schema-filling
        // model is least likely to volunteer.
        var verifyPrompt = $"""
            Look at this photograph again and answer only about what is visible in it.

            A previous pass recorded:
              scene_type: {current.SceneType}
              parking:    {current.Geometry.Parking}
              sidewalks:  {(current.Geometry.Sidewalks ? "yes" : "no")}
              terrain:    {current.Environment.Terrain}
              trees:      {trees}

            Check each against the image and correct any that are wrong.

            parking must be "on-street" only if vehicles park along the roadway itself,
            "lot" only if a dedicated off-street paved parking area is visible, and
            "none" if there is no parking of either kind in the photograph — an
            unbroken frontage onto the pavement is "none". Do not guess a lot from
            the kind of business.

            sidewalks is true only if a raised pedestrian pavement is actually visible.

            """ + VerifyAnswerShape;

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
                        new { type = "image_url", image_url = new { url = $"data:{mimeType};base64,{b64}" } },
                        new { type = "text", text = verifyPrompt }
                    }
                }
            },
            temperature      = 0.0,
            top_p            = 0.95,
            max_tokens       = 65536,
            seed             = 12,
            reasoning_budget = 16384,
            chat_template_kwargs = new { enable_thinking = false },
            stream           = true
        };

        string text;
        try
        {
            text = await RequestContentAsync(body, $"verify {current.Id}");
        }
        catch (Exception ex)
        {
            // Best-effort by design: the first pass already produced a usable
            // SceneDna, and a failed second opinion must not cost the run.
            _logger.LogWarning(ex, "Verification failed for {Id} — keeping the first pass", current.Id);
            return current;
        }

        return ApplyVerification(current, text, _logger);
    }

    // Kept out of the interpolated prompt above: in a raw string literal a brace
    // opens an interpolation, so the JSON shape cannot live inside one without
    // fighting the escaping.
    private const string VerifyAnswerShape = """
        Return ONLY this JSON, snake_case, no other text:
        {"scene_type": "...", "parking": "...", "sidewalks": true, "terrain": "...",
         "trees": [{"position": "...", "size": "...", "type": "..."}]}
        """;

    // Only the five fields are taken, and only when the answer actually parses.
    // Anything else in the response is ignored: this pass is a second opinion on
    // named claims, not a second chance to rewrite the whole scene.
    private static SceneDna ApplyVerification(SceneDna current, string text, ILogger logger)
    {
        try
        {
            var clean = text.Trim();
            var thinkEnd = clean.LastIndexOf("</think>", StringComparison.OrdinalIgnoreCase);
            if (thinkEnd >= 0) clean = clean[(thinkEnd + "</think>".Length)..];
            var start = clean.IndexOf('{');
            var end   = clean.LastIndexOf('}');
            if (start < 0 || end <= start) return current;

            var dto = JsonSerializer.Deserialize<VerifyDto>(NormalizeKeys(clean[start..(end + 1)]), JsonOpts);
            if (dto is null) return current;

            var changes = new List<string>();
            var result  = current;

            if (!string.IsNullOrWhiteSpace(dto.SceneType) && dto.SceneType != current.SceneType)
            {
                changes.Add($"scene_type {current.SceneType} -> {dto.SceneType}");
                result = result with { SceneType = dto.SceneType };
            }
            if (!string.IsNullOrWhiteSpace(dto.Parking) && dto.Parking != current.Geometry.Parking)
            {
                changes.Add($"parking '{current.Geometry.Parking}' -> '{dto.Parking}'");
                result = result with { Geometry = result.Geometry with { Parking = dto.Parking } };
            }
            if (dto.Sidewalks is { } walks && walks != current.Geometry.Sidewalks)
            {
                changes.Add($"sidewalks {current.Geometry.Sidewalks} -> {walks}");
                result = result with { Geometry = result.Geometry with { Sidewalks = walks } };
            }
            if (!string.IsNullOrWhiteSpace(dto.Terrain) && dto.Terrain != current.Environment.Terrain)
            {
                changes.Add($"terrain {current.Environment.Terrain} -> {dto.Terrain}");
                result = result with { Environment = result.Environment with { Terrain = dto.Terrain } };
            }
            if (dto.Trees is { } trees && trees.Count != current.Environment.Trees.Count)
            {
                changes.Add($"trees {current.Environment.Trees.Count} -> {trees.Count}");
                result = result with
                {
                    Environment = result.Environment with
                    {
                        Trees = trees.Select(t => new Tree(
                            Position: t.Position ?? "unknown",
                            Size:     t.Size     ?? "medium",
                            Type:     t.Type     ?? "deciduous")).ToList()
                    }
                };
            }

            logger.LogInformation("Verification: {Result}",
                changes.Count == 0 ? "first pass confirmed, nothing changed" : string.Join("; ", changes));
            return result;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not read the verification answer — keeping the first pass");
            return current;
        }
    }

    private record VerifyDto(
        [property: JsonPropertyName("scene_type")] string? SceneType,
        [property: JsonPropertyName("parking")]    string? Parking,
        [property: JsonPropertyName("sidewalks")]  bool?   Sidewalks,
        [property: JsonPropertyName("terrain")]    string? Terrain,
        [property: JsonPropertyName("trees")]      List<TreeDto>? Trees);

    public async Task<SceneDna> EnrichAsync(string photoPath, SceneDna current, IReadOnlyList<string> missingFields)
    {
        _logger.LogInformation("Enriching SceneDna {Id}, missing: {Fields}", current.Id, string.Join(", ", missingFields));

        var b64 = Convert.ToBase64String(await File.ReadAllBytesAsync(photoPath));
        var ext = Path.GetExtension(photoPath).TrimStart('.').ToLower();
        var mimeType = ext is "png" ? "image/png" : "image/jpeg";

        // Serialized snake_case, matching the schema the vision prompt documents.
        // It used to go out in the C# record's own PascalCase, and the model did
        // the reasonable thing and mirrored that shape back — at which point the
        // DTO, which maps "scene_type", read every field as null and the retry
        // produced the same "unknown" it was called to fix.
        var currentJson = ToSnakeCaseJson(current);
        var fieldsList  = string.Join(", ", missingFields);
        var enrichPrompt = $"""
            The following fields are missing or have default values: {fieldsList}.
            Current SceneDna: {currentJson}
            Analyze the photo again and return ONLY the corrected JSON, using exactly
            the snake_case field names shown above, with all fields filled in.
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
            temperature      = 0.0,
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

        var text = await RequestContentAsync(body, $"enrich {current.Id}");

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

            // Read through a key normalizer rather than straight into the DTO:
            // the model answers in whatever casing the conversation put in front
            // of it, and "SceneType" does not match a [JsonPropertyName]
            // of "scene_type" however case-insensitive the options are — the
            // underscore is a different name, not a different case.
            var dto = JsonSerializer.Deserialize<SceneDnaDto>(NormalizeKeys(clean), JsonOpts);

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

    // PascalCase and camelCase keys rewritten to the snake_case the DTO declares,
    // recursively. Keys already in snake_case pass through untouched, so a
    // correctly-shaped answer is unaffected.
    private static string NormalizeKeys(string json)
    {
        var node = System.Text.Json.Nodes.JsonNode.Parse(json);
        return node is null ? json : Rewrite(node).ToJsonString();
    }

    private static System.Text.Json.Nodes.JsonNode? Rewrite(System.Text.Json.Nodes.JsonNode? node)
    {
        switch (node)
        {
            case System.Text.Json.Nodes.JsonObject obj:
            {
                var result = new System.Text.Json.Nodes.JsonObject();
                foreach (var (key, value) in obj.ToList())
                    result[ToSnakeCase(key)] = Rewrite(value?.DeepClone());
                return result;
            }
            case System.Text.Json.Nodes.JsonArray arr:
            {
                var result = new System.Text.Json.Nodes.JsonArray();
                foreach (var item in arr.ToList())
                    result.Add(Rewrite(item?.DeepClone()));
                return result;
            }
            default:
                return node?.DeepClone();
        }
    }

    private static string ToSnakeCase(string name)
    {
        if (name.Contains('_')) return name.ToLowerInvariant();   // already snake_case
        var sb = new StringBuilder(name.Length + 4);
        for (var i = 0; i < name.Length; i++)
        {
            if (char.IsUpper(name[i]) && i > 0) sb.Append('_');
            sb.Append(char.ToLowerInvariant(name[i]));
        }
        return sb.ToString();
    }

    // The SceneDna the enrich prompt shows the model, in the schema the prompt
    // documents rather than in the record's own casing.
    private static string ToSnakeCaseJson(SceneDna scene) =>
        NormalizeKeys(JsonSerializer.Serialize(scene, JsonOpts));

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
