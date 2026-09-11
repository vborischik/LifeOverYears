using Google.Apis.Auth.OAuth2;
using Google.Apis.Services;
using Google.Apis.Upload;
using Google.Apis.YouTube.v3;
using YtVideo = Google.Apis.YouTube.v3.Data.Video;
using Google.Apis.YouTube.v3.Data;
using LifeOverYears.Models;
using LifeOverYears.Services;
using LifeOverYears.Services.Interfaces;
using Microsoft.Extensions.Logging;

namespace LifeOverYears.Providers;

// YouTube Data API through Google's SDK. Ported from YoutubePublisher's
// YoutubeUploader, the version that shipped a month of daily uploads, with
// the metadata rules kept exactly: privacy is passed, never defaulted;
// synthetic-media disclosure is on; an over-long title is a caller error
// rather than a silent cut.
//
// The SDK is the one dependency this file adds to the project, and it earns
// it twice: Videos.Insert with a stream is a resumable upload the library
// negotiates and retries itself, and the OAuth broker caches consent in the
// user's FileDataStore — same client secret, same "user" key, and it picks
// up the refresh token already on this machine without opening a browser.
public sealed class YouTubeProvider : IPublishTarget
{
    // YouTube's real title limit. The original code truncated at 90, which
    // was its own invention.
    public const int MaxTitleLength = 100;

    // People & Blogs. The category set is fixed by YouTube and this is the
    // one that fits a place-over-time video.
    private const string CategoryId = "22";

    private static readonly IReadOnlySet<string> ValidPrivacy =
        new HashSet<string>(StringComparer.Ordinal) { "private", "unlisted", "public" };

    private readonly YouTubeService _youtube;
    private readonly ILogger<YouTubeProvider> _logger;

    public string Platform => "youtube";

    private YouTubeProvider(YouTubeService youtube, ILogger<YouTubeProvider> logger)
    {
        _youtube = youtube;
        _logger  = logger;
    }

    // Async because the first call may need a browser consent; every call
    // after that is a cache hit. The "user" key and the client secret are the
    // identity of that cache — change either and the consent has to be
    // granted again.
    public static async Task<YouTubeProvider> CreateAsync(string clientSecretPath, ILogger<YouTubeProvider> logger)
    {
        if (!File.Exists(clientSecretPath))
            throw new FileNotFoundException(
                $"YouTube client secret not found at {clientSecretPath} — set Publish:YouTube:ClientSecretPath",
                clientSecretPath);

        var secrets = (await GoogleClientSecrets.FromFileAsync(clientSecretPath)).Secrets;

        var credential = await GoogleWebAuthorizationBroker.AuthorizeAsync(
            secrets,
            new[] { YouTubeService.Scope.YoutubeUpload },
            "user",
            CancellationToken.None);

        var service = new YouTubeService(new BaseClientService.Initializer
        {
            HttpClientInitializer = credential,
            ApplicationName       = "LifeOverYears",
        });

        return new YouTubeProvider(service, logger);
    }

    public async Task<Publication> PublishAsync(PublishRequest request, CancellationToken ct = default)
    {
        var video = BuildVideo(
            request.Caption.Title,
            PublishText.BodyWithTags(request.Caption),
            PublishText.BareTags(request.Caption),
            request.Privacy,
            request.PublishAt);

        _logger.LogInformation("YouTube: uploading {Path} as {Privacy}", request.Video.FilePath, request.Privacy);
        var videoId = await UploadAsync(request.Video.FilePath, video, ct);

        if (request.ThumbnailPath is { } thumb && File.Exists(thumb))
            await TrySetThumbnailAsync(videoId, thumb, ct);

        var url = $"https://youtu.be/{videoId}";
        _logger.LogInformation("YouTube video {Url}", url);

        return new Publication(
            Id:          Guid.NewGuid().ToString(),
            VideoId:     request.Video.Id,
            CaptionId:   request.Caption.Id,
            Platform:    Platform,
            Url:         url,
            PublishedAt: DateTimeOffset.UtcNow.ToString("o"));
    }

    // Split out so the metadata rules can be checked without a token or a
    // network call — every constraint that matters is enforced here.
    //
    // privacyStatus has no default anywhere in this class. Our scope is
    // youtube.upload only, which cannot call videos.update afterwards: a
    // batch published public by accident could not be pulled back down
    // through the API. Making the caller say the word is the cheapest guard.
    public static YtVideo BuildVideo(
        string title, string description, IEnumerable<string> tags, string privacyStatus,
        DateTimeOffset? publishAt = null)
    {
        if (string.IsNullOrWhiteSpace(privacyStatus))
            throw new ArgumentException(
                "privacyStatus must be passed explicitly — the youtube.upload scope cannot undo a public publish",
                nameof(privacyStatus));
        if (!ValidPrivacy.Contains(privacyStatus))
            throw new ArgumentException(
                $"privacyStatus '{privacyStatus}' is not one of: {string.Join(", ", ValidPrivacy)}",
                nameof(privacyStatus));
        if (string.IsNullOrWhiteSpace(title))
            throw new ArgumentException("title is empty", nameof(title));
        if (title.Length > MaxTitleLength)
            throw new ArgumentException(
                $"title is {title.Length} chars, over YouTube's {MaxTitleLength} limit — draw another one rather than truncating",
                nameof(title));
        if (publishAt is { } at && privacyStatus != "private")
            throw new ArgumentException(
                $"a scheduled publishAt ({at:o}) is only accepted on a private video — YouTube flips it public itself",
                nameof(publishAt));

        return new YtVideo
        {
            Snippet = new VideoSnippet
            {
                Title       = title,
                Description = description,
                Tags        = tags.Where(t => t.Length > 0).ToList(),
                CategoryId  = CategoryId,
            },
            Status = new VideoStatus
            {
                PrivacyStatus = privacyStatus,
                // YouTube requires an explicit audience declaration; relying on
                // a server-side default across a batch is not something to do.
                SelfDeclaredMadeForKids = false,
                // Every frame is model-generated and meant to read as a real
                // photograph of a real place — exactly the "realistic altered
                // or synthetic content" a channel owner has to disclose. A
                // fact about this project, not a choice, so not a parameter.
                ContainsSyntheticMedia = true,
                // Scheduled release; the mechanism that turns one upload
                // session into a month of daily publishing.
                PublishAtDateTimeOffset = publishAt,
            },
        };
    }

    private async Task<string> UploadAsync(string videoPath, YtVideo video, CancellationToken ct)
    {
        await using var stream = new FileStream(videoPath, FileMode.Open, FileAccess.Read);

        var request = _youtube.Videos.Insert(video, "snippet,status", stream, "video/*");
        request.ProgressChanged += p =>
        {
            if (p.Status == UploadStatus.Failed)
                _logger.LogError(p.Exception, "YouTube upload failed for {Path}", videoPath);
        };

        var progress = await request.UploadAsync(ct);
        if (progress.Status != UploadStatus.Completed)
            throw progress.Exception ?? new InvalidOperationException($"YouTube upload ended as {progress.Status}");

        return request.ResponseBody?.Id
            ?? throw new InvalidOperationException("YouTube upload completed with no response body");
    }

    // Allowed under youtube.upload, so no second consent — but a separate
    // call after the video exists, billed to the general quota rather than
    // the upload bucket. Never fatal: a video up with YouTube's own cover is
    // a far better outcome than a video marked failed over its thumbnail.
    // Custom thumbnails also need a phone-verified channel, and that arrives
    // here as a 403.
    private async Task TrySetThumbnailAsync(string videoId, string imagePath, CancellationToken ct)
    {
        try
        {
            await using var stream = new FileStream(imagePath, FileMode.Open, FileAccess.Read);
            var mime = Path.GetExtension(imagePath).ToLowerInvariant() is ".png" ? "image/png" : "image/jpeg";

            var progress = await _youtube.Thumbnails.Set(videoId, stream, mime).UploadAsync(ct);
            if (progress.Status != UploadStatus.Completed)
                throw progress.Exception ?? new InvalidOperationException($"thumbnail upload ended as {progress.Status}");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "YouTube: video {Id} is up, thumbnail was not set", videoId);
        }
    }
}
