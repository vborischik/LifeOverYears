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
            max_tokens = 700
        };

        _logger.LogInformation("Caption: requesting description from {Model}", Model);
        var raw = await _nvidia.PostAsync(Endpoint, body);

        using var doc = JsonDocument.Parse(raw);
        var text = doc.RootElement
            .GetProperty("choices")[0]
            .GetProperty("message")
            .GetProperty("content")
            .GetString();

        if (string.IsNullOrWhiteSpace(text))
            throw new InvalidOperationException("Caption: model returned empty content");

        return text.Trim();
    }
}
