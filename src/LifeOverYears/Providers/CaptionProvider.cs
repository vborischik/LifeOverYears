using System.Text.Json;
using LifeOverYears.Services.Interfaces;
using Microsoft.Extensions.Logging;

namespace LifeOverYears.Providers;

public sealed class CaptionProvider : ICaptionProvider
{
    private const string Endpoint = "https://integrate.api.nvidia.com/v1/chat/completions";
    private const string Model = "openai/gpt-oss-120b";

    private readonly INvidiaProvider _nvidia;
    private readonly ILogger<CaptionProvider> _logger;

    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    public CaptionProvider(INvidiaProvider nvidia, ILogger<CaptionProvider> logger)
    {
        _nvidia = nvidia;
        _logger = logger;
    }

    public async Task<string> GenerateDescriptionAsync(string systemPrompt, string userContext)
    {
        var body = new
        {
            model = Model,
            messages = new[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user",   content = userContext }
            },
            temperature = 0.9,
            top_p = 0.95,
            // gpt-oss-120b is a reasoning model: its chain of thought goes to
            // reasoning_content and the answer to content, both drawn from this
            // one budget. At 700 the reasoning consumed all of it and content
            // came back empty on an otherwise successful 200.
            max_tokens = 2000
        };

        _logger.LogInformation("Caption: requesting description from {Model}", Model);
        var raw = await _nvidia.PostAsync(Endpoint, body);

        using var doc = JsonDocument.Parse(raw);
        var message = doc.RootElement
            .GetProperty("choices")[0]
            .GetProperty("message");
        var text = message.GetProperty("content").GetString();

        if (string.IsNullOrWhiteSpace(text))
        {
            // Distinguish "reasoned but never answered" (budget exhausted) from a
            // refusal or a malformed response — they look identical at the HTTP
            // layer, and only the first is fixed by raising max_tokens.
            var reasoning = message.TryGetProperty("reasoning_content", out var r)
                ? r.GetString()
                : null;
            _logger.LogWarning(
                "Caption: empty content from {Model}; reasoning_content {State} ({Length} chars)",
                Model,
                reasoning is null ? "absent" : "present",
                reasoning?.Length ?? 0);

            throw new InvalidOperationException(
                reasoning is { Length: > 0 }
                    ? $"Caption: model returned empty content after {reasoning.Length} chars of reasoning_content — the token budget was likely spent before the answer"
                    : "Caption: model returned empty content and no reasoning_content");
        }

        return text.Trim();
    }
}
