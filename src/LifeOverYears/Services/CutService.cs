using LifeOverYears.Models;
using LifeOverYears.Services.Interfaces;
using Microsoft.Extensions.Logging;

namespace LifeOverYears.Services;

// Re-cuts a run for a platform family at publish time. The master is the
// looping cut (2025, 1975 … 2015, tail 2025) and it is what YouTube gets;
// Reels on Instagram and Facebook were measured to draw fewer views with
// it, so the Meta family is re-assembled 1975 → 2025 with no tail — a video
// that ends on the present instead of wiping back to it.
//
// Built from stamped/{year}.png, the year-overlaid frames every run keeps,
// through the same VideoService the run used — so a re-cut differs from
// the master in order and tail only, never in look.
public sealed class CutService : ICutService
{
    public const string LoopCut          = "loop";
    public const string ChronologicalCut = "chronological";

    private readonly IVideoService _video;
    private readonly IReadOnlyDictionary<string, string> _cutByFamily;
    private readonly ILogger<CutService> _logger;

    // cutByFamily is Publish:Cut straight from config — family → cut name.
    // A family with no entry keeps the master.
    public CutService(IVideoService video, IReadOnlyDictionary<string, string>? cutByFamily, ILogger<CutService> logger)
    {
        _video       = video;
        _cutByFamily = new Dictionary<string, string>(cutByFamily ?? new Dictionary<string, string>(), StringComparer.OrdinalIgnoreCase);
        _logger      = logger;
    }

    public string CutFor(string family) =>
        _cutByFamily.TryGetValue(family, out var cut) && !string.IsNullOrWhiteSpace(cut)
            ? cut.ToLowerInvariant()
            : LoopCut;

    public async Task<PublishRequest> WithCutAsync(string family, PublishRequest request, CancellationToken ct = default)
    {
        var cut = CutFor(family);
        if (cut == LoopCut)
            return request;
        if (cut != ChronologicalCut)
            throw new InvalidOperationException($"Publish:Cut:{family} is '{cut}' — known cuts: {LoopCut}, {ChronologicalCut}");

        var master  = request.Video.FilePath;
        var videoDir = Path.GetDirectoryName(master)!;
        var runDir   = Path.GetDirectoryName(videoDir)!;
        var target   = Path.Combine(videoDir, $"{Path.GetFileNameWithoutExtension(master)}.{family}.silent.mp4");

        // Reused when it is newer than the master; a master re-assembled
        // after the cut was made invalidates it.
        if (File.Exists(target) && File.GetLastWriteTimeUtc(target) >= File.GetLastWriteTimeUtc(master))
            return request with { Video = request.Video with { FilePath = target } };

        var frames = StampedFrames(runDir);
        if (frames.Count < 2)
            throw new InvalidOperationException(
                $"Cannot re-cut {request.Video.Id} for {family}: {frames.Count} stamped frame(s) under {Path.Combine(runDir, "stamped")}");

        _logger.LogInformation("Re-cutting {Id} for {Family}: {Order}, no loop tail",
            request.Video.Id, family, string.Join(" → ", frames.Select(f => f.Year)));

        var video = await _video.ComposeAsync(frames, target, loopTail: false)
            ?? throw new InvalidOperationException("ffmpeg could not compose the re-cut");
        return request with { Video = request.Video with { FilePath = video.FilePath } };
    }

    // Oldest to newest — the chronological cut is the years in order.
    private static List<HistoricalImage> StampedFrames(string runDir)
    {
        var dir = Path.Combine(runDir, "stamped");
        if (!Directory.Exists(dir)) return new List<HistoricalImage>();
        return Directory.EnumerateFiles(dir, "*.png")
            .Select(p => (Path: p, Year: int.TryParse(Path.GetFileNameWithoutExtension(p), out var y) ? y : -1))
            .Where(x => x.Year > 0)
            .OrderBy(x => x.Year)
            .Select(x => new HistoricalImage(
                Id: $"stamped-{x.Year}", PromptId: "", Year: x.Year, FilePath: x.Path,
                Provider: "stamped", CreatedAt: File.GetLastWriteTimeUtc(x.Path).ToString("o")))
            .ToList();
    }
}
