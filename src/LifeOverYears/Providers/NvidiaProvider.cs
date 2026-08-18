using System.Net;
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

        const int maxAttempts = 4;
        for (var attempt = 1; ; attempt++)
        {
            HttpResponseMessage response;
            try
            {
                response = await _http.PostAsync(url, new StringContent(json, Encoding.UTF8, "application/json"));
            }
            catch (TaskCanceledException)
            {
                // No CancellationToken is passed into this method, so this always
                // means the HttpClient timeout elapsed — never caller cancellation.
                if (attempt >= maxAttempts)
                    throw;

                _logger.LogWarning(
                    "POST {Url} timed out on attempt {Attempt}/{MaxAttempts} — retrying in {Delay}ms",
                    url, attempt, maxAttempts, BackoffMs(attempt));
                await Task.Delay(BackoffMs(attempt));
                continue;
            }

            using (response)
            {
                if (response.IsSuccessStatusCode)
                    return await response.Content.ReadAsStringAsync();

                // A 502/503 from the inference gateway is transient and says
                // nothing about the request — without this it killed the whole
                // run at step 1 and retired the source photo to failed/. Same
                // policy as OpenAiProvider: rate limits and 5xx are retried,
                // everything else surfaces immediately.
                var transient = response.StatusCode == HttpStatusCode.TooManyRequests
                                || (int)response.StatusCode >= 500;
                if (transient && attempt < maxAttempts)
                {
                    _logger.LogWarning(
                        "POST {Url} returned {Status} on attempt {Attempt}/{MaxAttempts} — retrying in {Delay}ms",
                        url, response.StatusCode, attempt, maxAttempts, BackoffMs(attempt));
                    await Task.Delay(BackoffMs(attempt));
                    continue;
                }

                // Keep the payload in the message: NVIDIA returns the actual
                // complaint (bad model name, oversized image) in the body, and
                // EnsureSuccessStatusCode would throw it away.
                var payload = await response.Content.ReadAsStringAsync();
                throw new HttpRequestException(
                    $"NVIDIA request failed ({(int)response.StatusCode} {response.StatusCode}): {payload}");
            }
        }
    }

    // Streaming exists to survive long generations: the gateway sends response
    // headers as soon as the model starts producing, instead of holding the
    // connection open for the whole answer and returning 502 when its own
    // timeout elapses. ResponseHeadersRead is what makes that true — without it
    // HttpClient buffers the entire body first and the streaming is pointless.
    public async Task<IReadOnlyList<string>> PostStreamAsync(string url, object body)
    {
        _logger.LogDebug("POST (stream) {Url}", url);
        var json = JsonSerializer.Serialize(body, JsonOpts);

        const int maxAttempts = 4;
        for (var attempt = 1; ; attempt++)
        {
            HttpResponseMessage response;
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, url)
                {
                    Content = new StringContent(json, Encoding.UTF8, "application/json")
                };
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
                response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
            }
            catch (TaskCanceledException)
            {
                if (attempt >= maxAttempts)
                    throw;

                _logger.LogWarning(
                    "POST (stream) {Url} timed out on attempt {Attempt}/{MaxAttempts} — retrying in {Delay}ms",
                    url, attempt, maxAttempts, BackoffMs(attempt));
                await Task.Delay(BackoffMs(attempt));
                continue;
            }

            using (response)
            {
                if (!response.IsSuccessStatusCode)
                {
                    var transient = response.StatusCode == HttpStatusCode.TooManyRequests
                                    || (int)response.StatusCode >= 500;
                    if (transient && attempt < maxAttempts)
                    {
                        _logger.LogWarning(
                            "POST (stream) {Url} returned {Status} on attempt {Attempt}/{MaxAttempts} — retrying in {Delay}ms",
                            url, response.StatusCode, attempt, maxAttempts, BackoffMs(attempt));
                        await Task.Delay(BackoffMs(attempt));
                        continue;
                    }

                    var errorPayload = await response.Content.ReadAsStringAsync();
                    throw new HttpRequestException(
                        $"NVIDIA request failed ({(int)response.StatusCode} {response.StatusCode}): {errorPayload}");
                }

                // Only the status is retried. Once chunks have started arriving a
                // failure mid-stream is not safely repeatable — the caller would
                // have to reconcile a partial answer with a fresh one.
                var chunks = new List<string>();
                await using var stream = await response.Content.ReadAsStreamAsync();
                using var reader = new StreamReader(stream);

                while (await reader.ReadLineAsync() is { } line)
                {
                    if (!line.StartsWith("data:", StringComparison.Ordinal))
                        continue;

                    var payload = line["data:".Length..].Trim();
                    if (payload.Length == 0)
                        continue;
                    if (payload == "[DONE]")
                        break;

                    chunks.Add(payload);
                }

                _logger.LogDebug("Stream complete: {Count} chunks", chunks.Count);
                return chunks;
            }
        }
    }

    private static int BackoffMs(int attempt) => (int)(Math.Pow(2, attempt) * 1000);

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
