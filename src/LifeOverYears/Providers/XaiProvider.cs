using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using LifeOverYears.Services.Interfaces;
using Microsoft.Extensions.Logging;

namespace LifeOverYears.Providers;

public sealed class XaiProvider : IXaiProvider
{
    private readonly HttpClient _http;
    private readonly ILogger<XaiProvider> _logger;

    private const string CompletionsUrl = "https://api.x.ai/v1/chat/completions";
    private const string Model = "grok-3";

    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    public XaiProvider(HttpClient http, string apiKey, ILogger<XaiProvider> logger)
    {
        _http = http;
        _logger = logger;
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
    }

    public async Task<string> CompleteAsync(string prompt)
    {
        _logger.LogInformation("Sending completion request to xAI");

        var body = JsonSerializer.Serialize(new
        {
            model = Model,
            messages = new[]
            {
                new { role = "user", content = prompt }
            },
            temperature = 0.7
        });

        var response = await _http.PostAsync(CompletionsUrl,
            new StringContent(body, Encoding.UTF8, "application/json"));
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync();
        return JsonDocument.Parse(json)
            .RootElement
            .GetProperty("choices")[0]
            .GetProperty("message")
            .GetProperty("content")
            .GetString() ?? string.Empty;
    }
}
