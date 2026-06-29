using System.Text.Json;
using LifeOverYears.Services.Interfaces;
using Microsoft.Extensions.Logging;

namespace LifeOverYears.Providers;

public sealed class PromptProvider : IPromptProvider
{
    private readonly INvidiaProvider _nvidia;
    private readonly ILogger<PromptProvider> _logger;

    private const string Url   = "https://ai.api.nvidia.com/v1/chat/completions";
    private const string Model = "nvidia/nemotron-3-ultra-550b-a55b";

    public PromptProvider(INvidiaProvider nvidia, ILogger<PromptProvider> logger)
    {
        _nvidia = nvidia;
        _logger = logger;
    }

    public async Task<string> GeneratePromptAsync(string context)
    {
        _logger.LogInformation("Generating image prompt via {Model}", Model);

        var body = new
        {
            model = Model,
            messages = new[]
            {
                new { role = "user", content = context }
            },
            temperature = 0.7,
            max_tokens  = 1024
        };

        var json = await _nvidia.PostAsync(Url, body);

        return JsonDocument.Parse(json)
            .RootElement
            .GetProperty("choices")[0]
            .GetProperty("message")
            .GetProperty("content")
            .GetString() ?? string.Empty;
    }
}
