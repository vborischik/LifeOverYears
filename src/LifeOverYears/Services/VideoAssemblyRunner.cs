using LifeOverYears.Models;
using LifeOverYears.Services.Interfaces;
using Microsoft.Extensions.Logging;

namespace LifeOverYears.Services;

// Shared tail of the pipeline — wait for exactly the requested years' images,
// stamp each with its year, then compose the stamped set into a video.
// Used by both Pipeline.cs (after real generation) and the 'assemble' CLI
// mode (against a folder with images already on disk).
public static class VideoAssemblyRunner
{
    public static async Task<(IReadOnlyList<int> Missing, Video? Video)> RunAsync(
        IYearOverlayService overlay,
        IVideoService video,
        string imagesDir,
        string stampedDir,
        string videoOutputPath,
        IReadOnlyList<int> years,
        Task generation,
        ILogger logger)
    {
        var missing = await CompletionWatcher.WaitForImagesAsync(imagesDir, years, generation, logger);
        if (missing.Count > 0)
            return (missing, null);

        Directory.CreateDirectory(stampedDir);
        foreach (var year in years)
        {
            var source = Path.Combine(imagesDir, $"{year}.png");
            var stamped = Path.Combine(stampedDir, $"{year}.png");
            await overlay.StampAsync(source, year, stamped);
        }
        logger.LogInformation("Overlay complete — {Count} years stamped into {Dir}", years.Count, stampedDir);

        var images = years
            .OrderBy(y => y)
            .Select(y => new HistoricalImage(
                Id:        Guid.NewGuid().ToString(),
                PromptId:  "manual",
                Year:      y,
                FilePath:  Path.Combine(stampedDir, $"{y}.png"),
                Provider:  "stamped",
                CreatedAt: DateTimeOffset.UtcNow.ToString("o")))
            .ToList();

        var result = await video.ComposeAsync(images, videoOutputPath);
        return (Array.Empty<int>(), result);
    }
}
