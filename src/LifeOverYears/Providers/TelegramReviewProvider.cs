using System.Net.Http.Headers;
using System.Text.Json;
using LifeOverYears.Models;
using LifeOverYears.Services;
using LifeOverYears.Services.Interfaces;
using Microsoft.Extensions.Logging;

namespace LifeOverYears.Providers;

// The review channel over the Telegram Bot API: the video goes to the
// reviewer's private chat under two inline buttons, and the answer comes
// back as a callback query — or as a typed "yes"/"no", for a reviewer who
// would rather type. A different provider from TelegramProvider, which
// posts to a channel: this one talks to one person and listens.
//
// Listening is getUpdates long-polling with a persisted offset, so a
// restart resumes where it left off instead of re-reading a day of
// updates. Anything from a chat other than the reviewer's is discarded
// unread — the bot is public by construction, and a stranger's "yes" must
// not publish a video.
public sealed class TelegramReviewProvider : IReviewChannel
{
    private const string ApproveData = "publish";
    private const string SkipData    = "skip";

    private static readonly HashSet<string> YesWords =
        new(StringComparer.OrdinalIgnoreCase) { "publish", "yes", "y", "да", "ок", "ok", "go" };
    private static readonly HashSet<string> NoWords =
        new(StringComparer.OrdinalIgnoreCase) { "no", "n", "skip", "нет", "не", "next" };

    private readonly HttpClient _http;
    private readonly ILogger<TelegramReviewProvider> _logger;
    private readonly string _botToken;
    private readonly string _reviewChatId;
    private readonly string _offsetPath;

    private long? _offset;

    // reviewChatId is the reviewer's own chat with the bot — a user id, so a
    // bare number. offsetPath is where the last-seen update id persists.
    public TelegramReviewProvider(
        HttpClient http, string botToken, string reviewChatId, string offsetPath,
        ILogger<TelegramReviewProvider> logger)
    {
        _http         = http;
        _logger       = logger;
        _botToken     = botToken;
        _reviewChatId = reviewChatId;
        _offsetPath   = offsetPath;
    }

    public async Task<long> SendForReviewAsync(ReviewItem item, PublishRequest request, CancellationToken ct = default)
    {
        // Title and body, no hashtags: the reviewer is judging the video and
        // the wording, and a wall of tags under it only hides the question.
        var caption = $"{request.Caption.Title}\n\n{request.Caption.Description}";
        if (caption.Length > TelegramProvider.MaxCaptionLength)
            caption = request.Caption.Title;

        using var form = new MultipartFormDataContent();
        await using var video = File.OpenRead(request.Video.FilePath);
        var videoPart = new StreamContent(video);
        videoPart.Headers.ContentType = new MediaTypeHeaderValue("video/mp4");
        form.Add(videoPart, "video", Path.GetFileName(request.Video.FilePath));
        form.Add(new StringContent(_reviewChatId), "chat_id");
        form.Add(new StringContent(caption), "caption");
        form.Add(new StringContent("true"), "supports_streaming");
        form.Add(new StringContent(ReplyMarkup(item.Id)), "reply_markup");

        var body = await PostAsync("sendVideo", form, ct);
        var messageId = body.GetProperty("result").GetProperty("message_id").GetInt64();
        _logger.LogInformation("Sent {Id} for review as message {Msg}", item.Id, messageId);
        return messageId;
    }

    // Two buttons. callback_data carries the item id so a button pressed on
    // an older message still names the right run.
    public static string ReplyMarkup(string itemId) =>
        JsonSerializer.Serialize(new
        {
            inline_keyboard = new[]
            {
                new[]
                {
                    new { text = "✅ Publish", callback_data = $"{ApproveData}:{itemId}" },
                    new { text = "⏭ Skip",    callback_data = $"{SkipData}:{itemId}" },
                },
            },
        });

    public async Task<IReadOnlyList<ReviewDecision>> PollDecisionsAsync(TimeSpan timeout, CancellationToken ct = default)
    {
        _offset ??= await ReadOffsetAsync();

        using var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["timeout"]         = ((int)timeout.TotalSeconds).ToString(),
            ["allowed_updates"] = "[\"message\",\"callback_query\"]",
            ["offset"]          = _offset is { } o ? (o + 1).ToString() : "",
        });

        // The long poll needs a client timeout longer than Telegram's own, or
        // the request is torn down just before the answer arrives.
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout + TimeSpan.FromSeconds(10));

        var body = await PostAsync("getUpdates", form, cts.Token);
        var decisions = new List<ReviewDecision>();

        foreach (var update in body.GetProperty("result").EnumerateArray())
        {
            var updateId = update.GetProperty("update_id").GetInt64();
            _offset = Math.Max(_offset ?? 0, updateId);

            var decision = await ParseUpdateAsync(update, ct);
            if (decision is not null)
                decisions.Add(decision);
        }

        if (decisions.Count > 0 || body.GetProperty("result").GetArrayLength() > 0)
            await WriteOffsetAsync();

        return decisions;
    }

    // A callback query is a button; a message is typed text. Both are
    // ignored unless they come from the reviewer's chat.
    public async Task<ReviewDecision?> ParseUpdateAsync(JsonElement update, CancellationToken ct)
    {
        if (update.TryGetProperty("callback_query", out var query))
        {
            var chatId = query.GetProperty("message").GetProperty("chat").GetProperty("id").GetInt64().ToString();
            if (chatId != _reviewChatId)
                return null;

            var data      = query.GetProperty("data").GetString() ?? "";
            var messageId = query.GetProperty("message").GetProperty("message_id").GetInt64();
            var queryId   = query.GetProperty("id").GetString() ?? "";

            var approved = data.StartsWith(ApproveData + ":", StringComparison.Ordinal);
            var skipped  = data.StartsWith(SkipData + ":", StringComparison.Ordinal);
            if (!approved && !skipped)
                return null;

            // Clears the spinner on the button; best-effort.
            try { await AnswerCallbackAsync(queryId, approved ? "Publishing…" : "Skipped", ct); }
            catch (Exception ex) { _logger.LogDebug(ex, "answerCallbackQuery failed"); }

            return new ReviewDecision(messageId, approved);
        }

        if (update.TryGetProperty("message", out var message))
        {
            var chatId = message.GetProperty("chat").GetProperty("id").GetInt64().ToString();
            if (chatId != _reviewChatId)
            {
                // Logged at info so the reviewer can find their own chat id on
                // first contact: send the bot anything, read the log.
                _logger.LogInformation("Ignoring message from chat {Chat} (reviewer chat is {Reviewer})", chatId, _reviewChatId);
                return null;
            }
            if (!message.TryGetProperty("text", out var textEl))
                return null;

            var text = (textEl.GetString() ?? "").Trim();
            long? replyTo = message.TryGetProperty("reply_to_message", out var r)
                ? r.GetProperty("message_id").GetInt64()
                : null;

            if (YesWords.Contains(text)) return new ReviewDecision(replyTo, true);
            if (NoWords.Contains(text))  return new ReviewDecision(replyTo, false);
        }

        return null;
    }

    public async Task ReportAsync(ReviewItem item, string text, CancellationToken ct = default)
    {
        var fields = new Dictionary<string, string>
        {
            ["chat_id"] = _reviewChatId,
            ["text"]    = text,
        };
        if (item.ReviewMessageId is { } replyTo)
            fields["reply_to_message_id"] = replyTo.ToString();
        using var form = new FormUrlEncodedContent(fields);
        await PostAsync("sendMessage", form, ct);
    }

    private async Task AnswerCallbackAsync(string queryId, string text, CancellationToken ct)
    {
        using var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["callback_query_id"] = queryId,
            ["text"]              = text,
        });
        await PostAsync("answerCallbackQuery", form, ct);
    }

    // First contact. With no ReviewChatId configured the loop cannot know
    // whose answers count, and the id is not something a person can look up
    // in the Telegram app. So: wait for any message to the bot and report
    // where it came from. Reads without advancing the persisted offset — the
    // real loop, once configured, sees the same update and ignores it.
    public static async Task<string?> DiscoverChatIdAsync(
        HttpClient http, string botToken, TimeSpan wait, ILogger logger, CancellationToken ct = default)
    {
        var deadline = DateTimeOffset.UtcNow + wait;
        long? offset = null;
        while (DateTimeOffset.UtcNow < deadline && !ct.IsCancellationRequested)
        {
            using var form = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["timeout"] = "20",
                ["offset"]  = offset is { } o ? (o + 1).ToString() : "",
            });
            var response = await http.PostAsync($"https://api.telegram.org/bot{botToken}/getUpdates", form, ct);
            var body     = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct)).RootElement;
            if (!body.TryGetProperty("ok", out var ok) || !ok.GetBoolean())
                throw new InvalidOperationException($"Telegram getUpdates failed: {body}");

            foreach (var update in body.GetProperty("result").EnumerateArray())
            {
                offset = update.GetProperty("update_id").GetInt64();
                if (!update.TryGetProperty("message", out var message)) continue;
                var chat = message.GetProperty("chat");
                var id   = chat.GetProperty("id").GetInt64().ToString();
                var who  = chat.TryGetProperty("username", out var u) ? u.GetString() : chat.TryGetProperty("first_name", out var n) ? n.GetString() : "?";
                logger.LogInformation("Message from chat {Id} ({Who}): \"{Text}\"", id, who,
                    message.TryGetProperty("text", out var t) ? t.GetString() : "(no text)");
                return id;
            }
        }
        return null;
    }

    // ── Offset persistence ───────────────────────────────────────────────────

    private async Task<long?> ReadOffsetAsync()
    {
        if (!File.Exists(_offsetPath)) return null;
        return long.TryParse((await File.ReadAllTextAsync(_offsetPath)).Trim(), out var v) ? v : null;
    }

    private async Task WriteOffsetAsync()
    {
        if (_offset is null) return;
        Directory.CreateDirectory(Path.GetDirectoryName(_offsetPath)!);
        await File.WriteAllTextAsync(_offsetPath, _offset.Value.ToString());
    }

    // ── HTTP ─────────────────────────────────────────────────────────────────

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
}
