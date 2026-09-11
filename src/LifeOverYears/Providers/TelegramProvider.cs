using System.Net.Http.Headers;
using System.Text.Json;
using LifeOverYears.Models;
using LifeOverYears.Services;
using LifeOverYears.Services.Interfaces;
using Microsoft.Extensions.Logging;

namespace LifeOverYears.Providers;

// Bot API sendVideo. The only target that takes the bytes directly, so it
// needs no storage step and is the cheapest end-to-end test of a publish.
//
// Two things the June 2026 sketch got wrong, both fixed here: it sent the
// caption with parse_mode=HTML and never escaped it, so the first caption
// with an '&' or a '<' in it would have been rejected; and it built the
// t.me/c/ link from the raw chat id, which for a channel carries a -100
// prefix that the link form does not.
public sealed class TelegramProvider : IPublishTarget
{
    // Bot API limit on a media caption. Above it the whole request is
    // rejected, not trimmed.
    public const int MaxCaptionLength = 1024;

    private readonly HttpClient _http;
    private readonly ILogger<TelegramProvider> _logger;
    private readonly string _botToken;
    private readonly string _chatId;

    public string Platform => "telegram";

    // chatId is a string on purpose: a channel is "@name" or "-100…", a
    // private chat is a bare number, and the Bot API takes all three as-is.
    public TelegramProvider(HttpClient http, string botToken, string chatId, ILogger<TelegramProvider> logger)
    {
        _http     = http;
        _logger   = logger;
        _botToken = botToken;
        _chatId   = chatId;
    }

    public async Task<Publication> PublishAsync(PublishRequest request, CancellationToken ct = default)
    {
        var full = PublishText.TitledBodyWithTags(request.Caption);

        // The full caption fits: one message. It does not: the video goes up
        // under title plus hashtags and the body follows as a reply. Nothing
        // is cut — every body in the pool is built to end on a question, and
        // a truncated one asks nothing.
        var (caption, overflow) = full.Length <= MaxCaptionLength
            ? (full, null)
            : (TitleAndTags(request.Caption), request.Caption.Description);

        _logger.LogInformation("Sending {Video} to Telegram chat {Chat}", request.Video.FilePath, _chatId);

        var messageId = await SendVideoAsync(request, caption, ct);
        if (overflow is not null)
            await SendMessageAsync(overflow, replyTo: messageId, ct);

        var url = MessageUrl(_chatId, messageId);
        _logger.LogInformation("Telegram post {Url}", url);

        return new Publication(
            Id:          Guid.NewGuid().ToString(),
            VideoId:     request.Video.Id,
            CaptionId:   request.Caption.Id,
            Platform:    Platform,
            Url:         url,
            PublishedAt: DateTimeOffset.UtcNow.ToString("o"));
    }

    private static string TitleAndTags(Caption caption) =>
        caption.Hashtags.Count == 0
            ? caption.Title
            : $"{caption.Title}\n\n{string.Join(" ", caption.Hashtags)}";

    private async Task<long> SendVideoAsync(PublishRequest request, string caption, CancellationToken ct)
    {
        using var form = new MultipartFormDataContent();

        await using var video = File.OpenRead(request.Video.FilePath);
        var videoPart = new StreamContent(video);
        videoPart.Headers.ContentType = new MediaTypeHeaderValue("video/mp4");
        form.Add(videoPart, "video", Path.GetFileName(request.Video.FilePath));

        // Telegram shows its own first-frame cover otherwise, which for this
        // project is already the frame we want — so the thumbnail is optional
        // and a missing file is not worth failing the post over.
        FileStream? thumb = null;
        if (request.ThumbnailPath is { } thumbPath && File.Exists(thumbPath))
        {
            thumb = File.OpenRead(thumbPath);
            var thumbPart = new StreamContent(thumb);
            thumbPart.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
            form.Add(thumbPart, "thumbnail", Path.GetFileName(thumbPath));
        }

        try
        {
            form.Add(new StringContent(_chatId), "chat_id");
            // Plain text, no parse_mode: the caption is prose from the pools
            // and nothing in it is markup.
            form.Add(new StringContent(caption), "caption");
            form.Add(new StringContent("true"), "supports_streaming");

            var body = await PostAsync("sendVideo", form, ct);
            return body.GetProperty("result").GetProperty("message_id").GetInt64();
        }
        finally
        {
            if (thumb is not null) await thumb.DisposeAsync();
        }
    }

    private async Task SendMessageAsync(string text, long replyTo, CancellationToken ct)
    {
        using var form = new MultipartFormDataContent
        {
            { new StringContent(_chatId),           "chat_id" },
            { new StringContent(text),              "text" },
            { new StringContent(replyTo.ToString()), "reply_to_message_id" },
        };
        await PostAsync("sendMessage", form, ct);
    }

    private async Task<JsonElement> PostAsync(string method, HttpContent content, CancellationToken ct)
    {
        var response = await _http.PostAsync($"https://api.telegram.org/bot{_botToken}/{method}", content, ct);
        var body     = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"Telegram {method} failed {(int)response.StatusCode}: {body}");

        var root = JsonDocument.Parse(body).RootElement;
        if (!root.TryGetProperty("ok", out var ok) || !ok.GetBoolean())
            throw new InvalidOperationException($"Telegram {method} returned ok=false: {body}");
        return root;
    }

    // A channel's public link form. "@name" → t.me/name/N; "-100123" →
    // t.me/c/123/N (the -100 is a Bot API namespace prefix, not part of the
    // link). A bare private-chat id has no link at all, and the best that
    // can be recorded is the id and message number.
    public static string MessageUrl(string chatId, long messageId)
    {
        if (chatId.StartsWith('@'))
            return $"https://t.me/{chatId[1..]}/{messageId}";
        if (chatId.StartsWith("-100", StringComparison.Ordinal))
            return $"https://t.me/c/{chatId[4..]}/{messageId}";
        return $"tg://chat/{chatId}/{messageId}";
    }
}
