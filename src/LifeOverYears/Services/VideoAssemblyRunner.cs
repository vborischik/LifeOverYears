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

    // The frame order every caller uses today: newest year first, then the rest
    // oldest to newest. The video opens on the place as it is now — the only
    // frame a viewer can recognise, and so the only one that earns the next
    // three seconds — then rewinds and walks back up to it.
    //
    // Paired with the loop tail, which repeats the opening frame at the end, the
    // run closes on the same image it started with and the platform's auto-loop
    // has no seam: 2025, 1975 … 2015, 2025.
    //
    // A helper rather than four copies of the expression, but still called
    // explicitly at each site: what order a video tells its story in is a
    // content decision, and hiding it inside RunAsync is what made it invisible
    // in the first place.
    public static IReadOnlyList<int> NewestFirst(IEnumerable<int> years)
    {
        var ascending = years.OrderBy(y => y).ToList();
        return ascending.Count > 1
            ? ascending.TakeLast(1).Concat(ascending.Take(ascending.Count - 1)).ToList()
            : ascending;
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

        // No sort. Which year opens the video is a content decision now that the
        // loop tail repeats the opening frame at the end: the first image is
        // also the last thing on screen, so a run can lead with the present and
        // rewind, or lead with the past and walk forward. That belongs to the
        // caller, which is the only place that knows which story it is telling.
        var images = years
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
