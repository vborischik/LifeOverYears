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
        // No sort. This used to re-order by year, which silently undid whatever
        // order the caller chose — and with VideoAssemblyRunner sorting too, the
        // frame order was decided twice, in two places, neither of them the one
        // that knows what story the video tells. It is the caller's now.
        _logger.LogInformation("Step 4 — composing {Count} images into {Output} (order: {Years})",
            images.Count, outputPath, string.Join(", ", images.Select(i => i.Year)));
        return await _ffmpeg.ComposeAsync(images, outputPath);
    }
}
