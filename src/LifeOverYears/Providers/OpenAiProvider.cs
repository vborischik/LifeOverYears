using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using LifeOverYears.Services.Interfaces;
using Microsoft.Extensions.Logging;

namespace LifeOverYears.Providers;

public sealed class OpenAiProvider : IOpenAiProvider
{
    private const string EditsUrl = "https://api.openai.com/v1/images/edits";
    private const string Model = "gpt-image-2";
    private const int MaxRetries = 4;

    private static readonly JsonSerializerOptions JsonOpts =
        new() { PropertyNameCaseInsensitive = true };

    private readonly HttpClient _http;
    private readonly string _apiKey;
    private readonly ILogger<OpenAiProvider> _logger;

    public OpenAiProvider(HttpClient http, string apiKey, ILogger<OpenAiProvider> logger)
    {
        _http = http;
        _apiKey = apiKey;
        _logger = logger;
        // gpt-image-2 can take up to ~2 min on complex prompts.
        _http.Timeout = TimeSpan.FromMinutes(5);
    }

    public async Task<byte[]> EditImageAsync(
        byte[] referenceImage, string prompt, string size, string quality,
        CancellationToken ct = default)
    {
        for (var attempt = 1; ; attempt++)
        {
            using var form = new MultipartFormDataContent
            {
                { new StringContent(Model), "model" },
                { new StringContent(prompt), "prompt" },
                { new StringContent(size), "size" },
                { new StringContent(quality), "quality" },
            };
            var imageContent = new ByteArrayContent(referenceImage);
            imageContent.Headers.ContentType = new MediaTypeHeaderValue("image/png");
            form.Add(imageContent, "image[]", "base.png");

            using var req = new HttpRequestMessage(HttpMethod.Post, EditsUrl) { Content = form };
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);

            HttpResponseMessage resp;
            try
            {
                resp = await _http.SendAsync(req, ct);
            }
            catch (HttpRequestException ex) when (attempt < MaxRetries)
            {
                _logger.LogWarning(ex, "OpenAI request failed (attempt {Attempt}), retrying", attempt);
                await Task.Delay(BackoffMs(attempt), ct);
                continue;
            }

            var payload = await resp.Content.ReadAsStringAsync(ct);

            if (resp.IsSuccessStatusCode)
                return ExtractImageBytes(payload);

            // Retry transient failures only.
            if ((resp.StatusCode == HttpStatusCode.TooManyRequests
                 || (int)resp.StatusCode >= 500) && attempt < MaxRetries)
            {
                _logger.LogWarning("OpenAI {Status} (attempt {Attempt}), retrying", resp.StatusCode, attempt);
                await Task.Delay(BackoffMs(attempt), ct);
                continue;
            }

            // Non-retryable (e.g. moderation_blocked, bad request) — surface it.
            throw new InvalidOperationException(
                $"OpenAI image edit failed ({(int)resp.StatusCode} {resp.StatusCode}): {payload}");
        }
    }

    private static int BackoffMs(int attempt) => (int)(Math.Pow(2, attempt) * 1000);

    private static byte[] ExtractImageBytes(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (root.TryGetProperty("data", out var data) && data.GetArrayLength() > 0
            && data[0].TryGetProperty("b64_json", out var b64))
            return Convert.FromBase64String(b64.GetString()!);
        throw new InvalidOperationException($"No image data in OpenAI response: {json}");
    }
}
