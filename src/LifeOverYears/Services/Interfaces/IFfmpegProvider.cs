using LifeOverYears.Models;

namespace LifeOverYears.Services.Interfaces;

public interface IFfmpegProvider
{
    // Returns null when ffmpeg is unavailable and video assembly is skipped.
    Task<Video?> ComposeAsync(IReadOnlyList<HistoricalImage> images, string outputPath);
}
