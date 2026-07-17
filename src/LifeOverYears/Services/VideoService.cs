using LifeOverYears.Models;
using LifeOverYears.Services.Interfaces;
using Microsoft.Extensions.Logging;

namespace LifeOverYears.Services;

public sealed class VideoService : IVideoService
{
    private readonly IFfmpegProvider _ffmpeg;
    private readonly ILogger<VideoService> _logger;

    public VideoService(IFfmpegProvider ffmpeg, ILogger<VideoService> logger)
    {
        _ffmpeg = ffmpeg;
        _logger = logger;
    }

    public async Task<Video?> ComposeAsync(IReadOnlyList<HistoricalImage> images, string outputPath)
    {
        var ordered = images.OrderBy(i => i.Year).ToList();
        _logger.LogInformation("Step 4 — composing {Count} images into {Output}",
            ordered.Count, outputPath);
        return await _ffmpeg.ComposeAsync(ordered, outputPath);
    }
}
