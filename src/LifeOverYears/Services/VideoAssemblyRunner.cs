using LifeOverYears.Models;
using LifeOverYears.Services.Interfaces;
using Microsoft.Extensions.Logging;

namespace LifeOverYears.Services;

// Shared tail of the pipeline — verify exactly the requested years' images are
// on disk, stamp each with its year, then compose the stamped set into a video.
// Used by the 'collect' and 'assemble' CLI modes; both target images that are
// already present, so a missing year is an immediate error, not a wait.
public static class VideoAssemblyRunner
{
    public static async Task<(IReadOnlyList<int> Missing, Video? Video)> RunAsync(
        IYearOverlayService overlay,
        IVideoService video,
        string imagesDir,
        string stampedDir,
        string videoOutputPath,
        IReadOnlyList<int> years,
        ILogger logger)
    {
        var missing = years
            .Where(y => !File.Exists(Path.Combine(imagesDir, $"{y}.png")))
            .ToList();
        if (missing.Count > 0)
        {
            logger.LogError("Missing images for years {Years} in {Dir}",
                string.Join(", ", missing), imagesDir);
            return (missing, null);
        }

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
