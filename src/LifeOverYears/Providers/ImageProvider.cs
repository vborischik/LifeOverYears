using System.Text.Json;
using LifeOverYears.Models;
using LifeOverYears.Services.Interfaces;
using Microsoft.Extensions.Logging;

namespace LifeOverYears.Providers;

public sealed class ImageProvider : IImageProvider
{
    private readonly INvidiaProvider _nvidia;
    private readonly ILogger<ImageProvider> _logger;

    private const string FluxUrl = "https://ai.api.nvidia.com/v1/genai/black-forest-labs/flux-dev";

    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    public ImageProvider(INvidiaProvider nvidia, ILogger<ImageProvider> logger)
    {
        _nvidia = nvidia;
        _logger = logger;
    }

    public async Task<HistoricalImage> GenerateImageAsync(Prompt prompt)
    {
        _logger.LogInformation("Generating image for year {Year}, prompt {Id}", prompt.Year, prompt.Id);

        var body = new
        {
            prompt = prompt.Text,
            width  = 1024,
            height = 1024,
            steps  = 20,
            seed   = 0
        };

        var json = await _nvidia.PostAsync(FluxUrl, body);

        if (IsAccepted(json, out var pollUrl))
            json = await _nvidia.PollAsync(pollUrl!);

        var imageBytes = ExtractImageBytes(json);

        var dir = Path.Combine("output", "images", prompt.Year.ToString());
        Directory.CreateDirectory(dir);
        var filePath = Path.Combine(dir, $"{prompt.Id}.png");
        await File.WriteAllBytesAsync(filePath, imageBytes);

        return new HistoricalImage(
            Id:        Guid.NewGuid().ToString(),
            PromptId:  prompt.Id,
            Year:      prompt.Year,
            FilePath:  filePath,
            Provider:  "nvidia-flux",
            CreatedAt: DateTimeOffset.UtcNow.ToString("o"));
    }

    private static bool IsAccepted(string json, out string? pollUrl)
    {
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.TryGetProperty("pollingUrl", out var p))
        {
            pollUrl = p.GetString();
            return pollUrl is not null;
        }
        pollUrl = null;
        return false;
    }

    private static byte[] ExtractImageBytes(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        if (root.TryGetProperty("artifacts", out var artifacts) && artifacts.GetArrayLength() > 0)
            return Convert.FromBase64String(artifacts[0].GetProperty("base64").GetString()!);

        if (root.TryGetProperty("data", out var data) && data.GetArrayLength() > 0)
            return Convert.FromBase64String(data[0].GetProperty("b64_json").GetString()!);

        throw new InvalidOperationException("No image data in NVIDIA Flux response");
    }
}
