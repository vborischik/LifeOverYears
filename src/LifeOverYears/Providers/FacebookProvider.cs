using System.Text.Json;
using LifeOverYears.Models;
using LifeOverYears.Services;
using LifeOverYears.Services.Interfaces;
using Microsoft.Extensions.Logging;

namespace LifeOverYears.Providers;

// Facebook Page Reels through the Graph API. Chosen over the plain Page
// video endpoint because a 9:16 sixteen-second clip is a Reel, and the Reels
// surface is where Facebook shows one; posted as a Page video it lands in the
// feed as a portrait rectangle.
//
// Three phases, like Instagram's flow but with the upload split out: start
// (get a video id and an upload URL), upload (hand that URL the public link
// to fetch from — no bytes cross this connection), finish (attach the text
// and say whether to publish). Written from the Graph API reference rather
// than ported; nothing in the two source projects posts to Facebook.
public sealed class FacebookProvider : IPublishTarget
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan PollCeiling  = TimeSpan.FromMinutes(5);

    private readonly HttpClient _http;
    private readonly ILogger<FacebookProvider> _logger;
    private readonly string _pageToken;
    private readonly string _pageId;
    private readonly string _version;

    public string Platform => "facebook";

    // pageToken is a Page access token, not a user token — the user token
    // can read the Page but cannot publish as it.
    public FacebookProvider(
        HttpClient http, string pageToken, string pageId, ILogger<FacebookProvider> logger,
        string apiVersion = "v25.0")
    {
        _http      = http;
        _logger    = logger;
        _pageToken = pageToken;
        _pageId    = pageId;
        _version   = apiVersion;
    }

    public async Task<Publication> PublishAsync(PublishRequest request, CancellationToken ct = default)
    {
        var videoUrl = request.PublicVideoUrl
            ?? throw new InvalidOperationException(
                "Facebook Reels pull the video from a URL and this request has none — run the storage step first");

        _logger.LogInformation("Facebook: starting Reel upload on page {Page}", _pageId);
        var (videoId, uploadUrl) = await StartAsync(ct);

        _logger.LogInformation("Facebook: video {Id}, handing it {Url}", videoId, videoUrl);
        await UploadByUrlAsync(uploadUrl, videoUrl, ct);

        await FinishAsync(videoId, request, ct);
        await WaitUntilProcessedAsync(videoId, ct);

        var url = $"https://www.facebook.com/reel/{videoId}";
        _logger.LogInformation("Facebook Reel {Url}", url);

        return new Publication(
            Id:          Guid.NewGuid().ToString(),
            VideoId:     request.Video.Id,
            CaptionId:   request.Caption.Id,
            Platform:    Platform,
            Url:         url,
            PublishedAt: DateTimeOffset.UtcNow.ToString("o"));
    }

    private async Task<(string VideoId, string UploadUrl)> StartAsync(CancellationToken ct)
    {
        var json = await PostFormAsync($"{Graph}/{_pageId}/video_reels", new Dictionary<string, string>
        {
            ["upload_phase"] = "start",
        }, ct);
        return (
            json.GetProperty("video_id").GetString()   ?? throw new InvalidOperationException("Facebook start returned no video_id"),
            json.GetProperty("upload_url").GetString() ?? throw new InvalidOperationException("Facebook start returned no upload_url"));
    }

    // The upload host takes the source as a header, not a body: file_url
    // tells Facebook to fetch, and the request itself carries nothing.
    private async Task UploadByUrlAsync(string uploadUrl, string videoUrl, CancellationToken ct)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, uploadUrl);
        request.Headers.TryAddWithoutValidation("Authorization", $"OAuth {_pageToken}");
        request.Headers.TryAddWithoutValidation("file_url", videoUrl);

        var response = await _http.SendAsync(request, ct);
        var body     = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"Facebook upload {(int)response.StatusCode}: {body}");

        using var doc = JsonDocument.Parse(body);
        if (!doc.RootElement.TryGetProperty("success", out var ok) || !ok.GetBoolean())
            throw new InvalidOperationException($"Facebook upload did not report success: {body}");
    }

    // Privacy maps onto video_state: "public" publishes (or schedules when a
    // time is given), anything else stays a draft on the Page, visible to
    // admins only — the closest thing Facebook has to YouTube's private.
    private async Task FinishAsync(string videoId, PublishRequest request, CancellationToken ct)
    {
        var fields = new Dictionary<string, string>
        {
            ["upload_phase"] = "finish",
            ["video_id"]     = videoId,
            ["title"]        = request.Caption.Title,
            ["description"]  = PublishText.BodyWithTags(request.Caption),
        };

        var isPublic = string.Equals(request.Privacy, "public", StringComparison.OrdinalIgnoreCase);
        if (isPublic && request.PublishAt is { } at)
        {
            fields["video_state"]            = "SCHEDULED";
            fields["scheduled_publish_time"] = at.ToUnixTimeSeconds().ToString();
        }
        else
        {
            fields["video_state"] = isPublic ? "PUBLISHED" : "DRAFT";
        }

        await PostFormAsync($"{Graph}/{_pageId}/video_reels", fields, ct);
    }

    // finish returns before transcoding is done. The status field carries
    // the processing and publishing phases; "complete" on the outer status
    // is the one that means the Reel is actually watchable. Bounded like
    // Instagram's poll, and for the same reason.
    private async Task WaitUntilProcessedAsync(string videoId, CancellationToken ct)
    {
        var deadline = DateTimeOffset.UtcNow + PollCeiling;
        while (DateTimeOffset.UtcNow < deadline)
        {
            await Task.Delay(PollInterval, ct);

            var json = await GetAsync($"{Graph}/{videoId}?fields=status", ct);
            if (!json.TryGetProperty("status", out var status))
                return;

            var phase = status.TryGetProperty("video_status", out var v) ? v.GetString() : null;
            _logger.LogDebug("Facebook video {Id}: {Status}", videoId, phase);

            if (phase is "ready" or "complete")
                return;
            if (phase is "error")
                throw new InvalidOperationException($"Facebook video {videoId} failed processing: {status.GetRawText()}");
        }
        // Not fatal: the Reel exists and Facebook finishes on its own. A
        // publish that is up is not a failure because a poll ran out.
        _logger.LogWarning("Facebook video {Id} still processing after {Minutes} minutes; publication recorded anyway",
            videoId, PollCeiling.TotalMinutes);
    }

    // ── HTTP ─────────────────────────────────────────────────────────────────

    private string Graph => $"https://graph.facebook.com/{_version}";

    private async Task<JsonElement> PostFormAsync(string url, Dictionary<string, string> fields, CancellationToken ct)
    {
        fields["access_token"] = _pageToken;
        using var form = new FormUrlEncodedContent(fields);
        var response = await _http.PostAsync(url, form, ct);
        return await ReadAsync(response, ct);
    }

    private async Task<JsonElement> GetAsync(string url, CancellationToken ct)
    {
        var separator = url.Contains('?') ? "&" : "?";
        var response  = await _http.GetAsync($"{url}{separator}access_token={Uri.EscapeDataString(_pageToken)}", ct);
        return await ReadAsync(response, ct);
    }

    private static async Task<JsonElement> ReadAsync(HttpResponseMessage response, CancellationToken ct)
    {
        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"Facebook API {(int)response.StatusCode}: {body}");
        return JsonDocument.Parse(body).RootElement;
    }
}
