using Microsoft.Extensions.Logging;

namespace LifeOverYears.Services;

// Polls the run's images/ folder until every requested year has arrived.
// Generation order is not guaranteed (providers complete out of order), so
// completion is defined purely by files on disk — the folder contract.
public static class CompletionWatcher
{
    public static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(2);
    public static readonly TimeSpan Timeout      = TimeSpan.FromMinutes(15);

    // Returns the years still missing when the wait ended (empty = success).
    // generation: the in-flight producer task; a fault there ends the wait
    // immediately instead of running out the full timeout.
    public static async Task<IReadOnlyList<int>> WaitForImagesAsync(
        string imagesDir,
        IReadOnlyList<int> years,
        Task generation,
        ILogger logger)
    {
        var deadline = DateTimeOffset.UtcNow + Timeout;

        while (true)
        {
            var missing = years
                .Where(y => !File.Exists(Path.Combine(imagesDir, $"{y}.png")))
                .ToList();

            if (missing.Count == 0)
                return missing;

            if (generation.IsFaulted)
                await generation; // rethrows the provider's exception

            if (DateTimeOffset.UtcNow >= deadline)
            {
                logger.LogError("Completion watcher timed out after {Timeout} — missing years: {Years}",
                    Timeout, string.Join(", ", missing));
                return missing;
            }

            logger.LogInformation("Waiting for images: {Missing} missing ({Years})",
                missing.Count, string.Join(", ", missing));
            await Task.Delay(PollInterval);
        }
    }
}
