using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using LifeOverYears.Services.Interfaces;
using Microsoft.Extensions.Logging;

namespace LifeOverYears.Providers;

public sealed class NvidiaProvider : INvidiaProvider
{
    private readonly HttpClient _http;
    private readonly ILogger<NvidiaProvider> _logger;

    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    public NvidiaProvider(HttpClient http, string apiKey, ILogger<NvidiaProvider> logger)
    {
        _http = http;
        _logger = logger;
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
    }

    public async Task<string> PostAsync(string url, object body)
    {
        _logger.LogDebug("POST {Url}", url);
        var json = JsonSerializer.Serialize(body, JsonOpts);

        const int maxAttempts = 3;
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                var response = await _http.PostAsync(url, new StringContent(json, Encoding.UTF8, "application/json"));
                response.EnsureSuccessStatusCode();
                return await response.Content.ReadAsStringAsync();
            }
            catch (TaskCanceledException)
            {
                // No CancellationToken is passed into this method, so this always
                // means the HttpClient timeout elapsed — never caller cancellation.
                if (attempt >= maxAttempts)
                    throw;

                _logger.LogWarning(
                    "POST {Url} timed out on attempt {Attempt}/{MaxAttempts} — retrying in 2s",
                    url, attempt, maxAttempts);
                await Task.Delay(2000);
            }
        }
    }

    public async Task<string> PollAsync(string url, int timeoutSeconds = 120)
    {
        _logger.LogDebug("Polling {Url} (timeout {Timeout}s)", url, timeoutSeconds);
        var deadline = DateTimeOffset.UtcNow.AddSeconds(timeoutSeconds);
        while (DateTimeOffset.UtcNow < deadline)
        {
            await Task.Delay(3000);
            var response = await _http.GetAsync(url);
            if (response.StatusCode != System.Net.HttpStatusCode.Accepted)
            {
                response.EnsureSuccessStatusCode();
                return await response.Content.ReadAsStringAsync();
            }
        }
        throw new TimeoutException($"NVIDIA polling timed out after {timeoutSeconds}s");
    }
}
