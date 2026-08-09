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
    // A year's image may arrive as either "{year}.png" or "{year}-clean.png" —
    // the second is a hand-corrected version dropped in alongside the generated
    // one. When both exist the cleaned file wins: it was made deliberately to
    // replace what the provider produced. Returns null when neither is present.
    public static string? FindEraImage(string imagesDir, int year)
    {
        var cleaned = Path.Combine(imagesDir, $"{year}-clean.png");
        if (File.Exists(cleaned)) return cleaned;
        var plain = Path.Combine(imagesDir, $"{year}.png");
        return File.Exists(plain) ? plain : null;
    }

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
            .Where(y => FindEraImage(imagesDir, y) is null)
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
            // Non-null: the missing check above already returned on any gap.
            var source = FindEraImage(imagesDir, year)!;
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
