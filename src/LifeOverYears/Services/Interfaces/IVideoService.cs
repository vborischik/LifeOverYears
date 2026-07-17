using LifeOverYears.Models;

namespace LifeOverYears.Services.Interfaces;

public interface IVideoService
{
    // Returns null when video assembly was skipped (e.g. ffmpeg missing).
    Task<Video?> ComposeAsync(IReadOnlyList<HistoricalImage> images, string outputPath);
}
