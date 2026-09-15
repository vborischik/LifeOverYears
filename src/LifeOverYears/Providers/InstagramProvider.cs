using System.Text.Json;
using LifeOverYears.Models;
using LifeOverYears.Services;
using LifeOverYears.Services.Interfaces;
using Microsoft.Extensions.Logging;

namespace LifeOverYears.Providers;

// Instagram Graph API, Reels. Three calls, and none of them takes the file:
// create a media container pointing at a public URL, poll until Instagram
// has fetched and transcoded it, then publish the container. Ported from
// HouseTimelineApp's InstagramApiClient, which ran this flow live, with the
// token moved out of the query string — a URL with the token in it ends up
// in every log line and every exception message — and the polling given a
// ceiling that matches what Instagram actually takes.
public sealed class InstagramProvider : IPublishTarget
{
    // Graph API limits on a Reel caption. Enforced before the first call,
    // because the container is created before the caption is rejected and
    // an orphaned container counts against the daily publishing quota.
    public const int MaxCaptionLength = 2200;
    public const int MaxHashtags      = 30;

    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan PollCeiling  = TimeSpan.FromMinutes(5);

    private readonly HttpClient _http;
    private readonly ILogger<InstagramProvider> _logger;
    private readonly string _accessToken;
    private readonly string _userId;
    private readonly string _base;

    public string Platform => "instagram";

    // userId is the Instagram professional account id, not the username.
    public InstagramProvider(
        HttpClient http, string accessToken, string userId, ILogger<InstagramProvider> logger,
        string apiVersion = "v25.0")
    {
        _http        = http;
        _logger      = logger;
        _accessToken = accessToken;
        _userId      = userId;
        _base        = $"https://graph.facebook.com/{apiVersion}";
    }

    public async Task<Publication> PublishAsync(PublishRequest request, CancellationToken ct = default)
    {
        var videoUrl = request.PublicVideoUrl
            ?? throw new InvalidOperationException(
                "Instagram pulls the video from a URL and this request has none — run the storage step first");

        var caption = BuildCaption(request.Caption);

        _logger.LogInformation("Instagram: creating Reel container for {Url}", videoUrl);
        var containerId = await CreateContainerAsync(videoUrl, caption, ct);

        _logger.LogInformation("Instagram: container {Id}, waiting for processing", containerId);
        await WaitUntilReadyAsync(containerId, ct);

        var mediaId = await PublishContainerAsync(containerId, ct);
        var url     = await PermalinkAsync(mediaId, ct);
        _logger.LogInformation("Instagram Reel {Url}", url);

        return new Publication(
            Id:          Guid.NewGuid().ToString(),
            VideoId:     request.Video.Id,
            CaptionId:   request.Caption.Id,
            Platform:    Platform,
            Url:         url,
            PublishedAt: DateTimeOffset.UtcNow.ToString("o"));
    }

    // Instagram has no title field; the body carries the hook already. Both
    // limits are caller errors, not things to trim: the pools are sized to
    // fit (C88 caps a caption at 400 words) and a cut body ends mid-question.
    public static string BuildCaption(Caption caption)
    {
        if (caption.Hashtags.Count > MaxHashtags)
            throw new ArgumentException(
                $"{caption.Hashtags.Count} hashtags, over Instagram's {MaxHashtags} — trim the pool, not the post",
                nameof(caption));

        var text = PublishText.BodyWithTags(caption);
        if (text.Length > MaxCaptionLength)
            throw new ArgumentException(
                $"caption is {text.Length} chars, over Instagram's {MaxCaptionLength} — draw another body rather than truncating",
                nameof(caption));
        return text;
    }

    private async Task<string> CreateContainerAsync(string videoUrl, string caption, CancellationToken ct)
    {
        var json = await PostFormAsync($"{_base}/{_userId}/media", new Dictionary<string, string>
        {
            ["media_type"]      = "REELS",
            ["video_url"]       = videoUrl,
            ["caption"]         = caption,
            ["share_to_feed"]   = "true",
            // Meta's self-disclosure of AI use, shown as the "AI info" label.
            // Every frame here is model-generated and made to read as a real
            // photograph of a real place — exactly the photorealistic synthetic
            // video Meta requires a poster to disclose, and may penalise for
            // not disclosing. A fact about this project, not a setting, so it
            // is not configurable — the same call YouTubeProvider makes with
            // ContainsSyntheticMedia.
            ["is_ai_generated"] = "true",
        }, ct);
        return json.GetProperty("id").GetString()
            ?? throw new InvalidOperationException("Instagram returned a container with no id");
    }

    // status_code moves IN_PROGRESS → FINISHED, or → ERROR with a reason in
    // "status". Bounded, because a URL Instagram cannot fetch never finishes
    // and never errors either — it just sits.
    private async Task WaitUntilReadyAsync(string containerId, CancellationToken ct)
    {
        var deadline = DateTimeOffset.UtcNow + PollCeiling;
        while (DateTimeOffset.UtcNow < deadline)
        {
            await Task.Delay(PollInterval, ct);

            var json   = await GetAsync($"{_base}/{containerId}?fields=status_code,status", ct);
            var status = json.GetProperty("status_code").GetString();
            _logger.LogDebug("Instagram container {Id}: {Status}", containerId, status);

            switch (status)
            {
                case "FINISHED":
                    return;
                case "ERROR":
                case "EXPIRED":
                    var detail = json.TryGetProperty("status", out var s) ? s.GetString() : null;
                    throw new InvalidOperationException($"Instagram container {containerId} ended {status}: {detail}");
            }
        }
        throw new TimeoutException($"Instagram container {containerId} not ready after {PollCeiling.TotalMinutes} minutes");
    }

    private async Task<string> PublishContainerAsync(string containerId, CancellationToken ct)
    {
        var json = await PostFormAsync($"{_base}/{_userId}/media_publish", new Dictionary<string, string>
        {
            ["creation_id"] = containerId,
        }, ct);
        return json.GetProperty("id").GetString()
            ?? throw new InvalidOperationException("Instagram media_publish returned no id");
    }

    // The media id is not a URL — the shortcode in the permalink is a
    // different value — so it is asked for. Best-effort: the Reel is up
    // whether or not this call answers, and a Publication with the id in it
    // beats a failed publish over its link.
    private async Task<string> PermalinkAsync(string mediaId, CancellationToken ct)
    {
        try
        {
            var json = await GetAsync($"{_base}/{mediaId}?fields=permalink", ct);
            if (json.TryGetProperty("permalink", out var link) && link.GetString() is { Length: > 0 } url)
                return url;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Instagram: media {Id} is published but its permalink could not be read", mediaId);
        }
        return $"instagram://media/{mediaId}";
    }

    // ── HTTP ─────────────────────────────────────────────────────────────────

    private async Task<JsonElement> PostFormAsync(string url, Dictionary<string, string> fields, CancellationToken ct)
    {
        fields["access_token"] = _accessToken;
        using var form = new FormUrlEncodedContent(fields);
        var response = await _http.PostAsync(url, form, ct);
        return await ReadAsync(response, ct);
    }

    private async Task<JsonElement> GetAsync(string url, CancellationToken ct)
    {
        var separator = url.Contains('?') ? "&" : "?";
        var response  = await _http.GetAsync($"{url}{separator}access_token={Uri.EscapeDataString(_accessToken)}", ct);
        return await ReadAsync(response, ct);
    }

    private static async Task<JsonElement> ReadAsync(HttpResponseMessage response, CancellationToken ct)
    {
        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"Instagram API {(int)response.StatusCode}: {body}");
        return JsonDocument.Parse(body).RootElement;
    }
}
