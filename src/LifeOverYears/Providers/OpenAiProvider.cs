using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using LifeOverYears.Services.Interfaces;
using Microsoft.Extensions.Logging;

namespace LifeOverYears.Providers;

public sealed class OpenAiProvider : IOpenAiProvider
{
    private const string EditsUrl = "https://api.openai.com/v1/images/edits";
    private const string FilesUrl = "https://api.openai.com/v1/files";
    private const string BatchesUrl = "https://api.openai.com/v1/batches";
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
        var payload = await SendWithRetryAsync(() =>
        {
            var form = new MultipartFormDataContent
            {
                { new StringContent(Model), "model" },
                { new StringContent(prompt), "prompt" },
                { new StringContent(size), "size" },
                { new StringContent(quality), "quality" },
            };
            var imageContent = new ByteArrayContent(referenceImage);
            imageContent.Headers.ContentType = new MediaTypeHeaderValue("image/png");
            form.Add(imageContent, "image[]", "base.png");
            return new HttpRequestMessage(HttpMethod.Post, EditsUrl) { Content = form };
        }, ct);

        return ExtractImageBytes(payload);
    }

    public async Task<string> UploadFileAsync(byte[] content, string fileName, string purpose,
        CancellationToken ct = default)
    {
        var payload = await SendWithRetryAsync(() =>
        {
            var form = new MultipartFormDataContent
            {
                { new StringContent(purpose), "purpose" },
            };
            form.Add(new ByteArrayContent(content), "file", fileName);
            return new HttpRequestMessage(HttpMethod.Post, FilesUrl) { Content = form };
        }, ct);

        return ExtractId(payload);
    }

    public async Task<string> CreateBatchAsync(string inputFileId, string endpoint,
        CancellationToken ct = default)
    {
        var payload = await SendWithRetryAsync(() =>
        {
            var body = new
            {
                input_file_id = inputFileId,
                endpoint,
                completion_window = "24h"
            };
            var json = JsonSerializer.Serialize(body, JsonOpts);
            return new HttpRequestMessage(HttpMethod.Post, BatchesUrl)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
        }, ct);

        return ExtractId(payload);
    }

    public async Task<(string Status, string? OutputFileId, string? ErrorFileId)> GetBatchAsync(
        string batchId, CancellationToken ct = default)
    {
        var payload = await SendWithRetryAsync(
            () => new HttpRequestMessage(HttpMethod.Get, $"{BatchesUrl}/{batchId}"), ct);

        var batch = JsonSerializer.Deserialize<BatchResponse>(payload, JsonOpts)
            ?? throw new InvalidOperationException($"Could not parse OpenAI batch response: {payload}");
        return (batch.Status, batch.OutputFileId, batch.ErrorFileId);
    }

    public Task<string> DownloadFileContentAsync(string fileId, CancellationToken ct = default) =>
        SendWithRetryAsync(() => new HttpRequestMessage(HttpMethod.Get, $"{FilesUrl}/{fileId}/content"), ct);

    // Sends one request per attempt (a HttpRequestMessage/its content can only be
    // sent once, so requestFactory builds a fresh one each time), retrying
    // transient failures with the same backoff schedule every call site shares.
    // Returns the raw response body on success; throws with status + payload on
    // a non-retryable failure.
    private async Task<string> SendWithRetryAsync(
        Func<HttpRequestMessage> requestFactory, CancellationToken ct)
    {
        for (var attempt = 1; ; attempt++)
        {
            using var req = requestFactory();
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

            using (resp)
            {
                var payload = await resp.Content.ReadAsStringAsync(ct);

                if (resp.IsSuccessStatusCode)
                    return payload;

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
                    $"OpenAI request failed ({(int)resp.StatusCode} {resp.StatusCode}): {payload}");
            }
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

    private static string ExtractId(string json) =>
        JsonSerializer.Deserialize<IdResponse>(json, JsonOpts)?.Id
            ?? throw new InvalidOperationException($"No id in OpenAI response: {json}");

    private sealed record IdResponse([property: JsonPropertyName("id")] string Id);

    private sealed record BatchResponse(
        [property: JsonPropertyName("status")] string Status,
        [property: JsonPropertyName("output_file_id")] string? OutputFileId,
        [property: JsonPropertyName("error_file_id")] string? ErrorFileId);
}
