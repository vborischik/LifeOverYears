using System.Net.Http.Headers;
using System.Text.Json;
using LifeOverYears.Models;
using LifeOverYears.Services.Interfaces;
using Microsoft.Extensions.Logging;

namespace LifeOverYears.Providers;

public sealed class TelegramProvider : ITelegramProvider
{
    private readonly HttpClient _http;
    private readonly ILogger<TelegramProvider> _logger;
    private readonly string _botToken;
    private readonly long _chatId;

    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    public TelegramProvider(
        HttpClient http,
        string botToken,
        long chatId,
        ILogger<TelegramProvider> logger)
    {
        _http = http;
        _logger = logger;
        _botToken = botToken;
        _chatId = chatId;
    }

    public async Task<Publication> SendVideoAsync(Video video, Caption caption)
    {
        _logger.LogInformation("Sending video {VideoId} to Telegram chat {ChatId}", video.Id, _chatId);

        var captionText = $"{caption.Title}\n\n{caption.Description}\n\n{string.Join(" ", caption.Hashtags)}";

        using var form = new MultipartFormDataContent();

        var fileBytes = await File.ReadAllBytesAsync(video.FilePath);
        var fileContent = new ByteArrayContent(fileBytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("video/mp4");
        form.Add(fileContent, "video", Path.GetFileName(video.FilePath));
        form.Add(new StringContent(_chatId.ToString()), "chat_id");
        form.Add(new StringContent(captionText), "caption");
        form.Add(new StringContent("HTML"), "parse_mode");

        var url = $"https://api.telegram.org/bot{_botToken}/sendVideo";
        var response = await _http.PostAsync(url, form);
        var body = await response.Content.ReadAsStringAsync();
        response.EnsureSuccessStatusCode();

        var messageId = JsonDocument.Parse(body)
            .RootElement
            .GetProperty("result")
            .GetProperty("message_id")
            .GetInt64();

        var publicUrl = $"https://t.me/c/{_chatId}/{messageId}";

        _logger.LogInformation("Video published: {Url}", publicUrl);

        return new Publication(
            Id:          Guid.NewGuid().ToString(),
            VideoId:     video.Id,
            CaptionId:   caption.Id,
            Platform:    "telegram",
            Url:         publicUrl,
            PublishedAt: DateTimeOffset.UtcNow.ToString("o"));
    }
}
