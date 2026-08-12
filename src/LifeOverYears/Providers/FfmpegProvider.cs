using System.Diagnostics;
using System.Text;
using LifeOverYears.Models;
using LifeOverYears.Services.Interfaces;
using Microsoft.Extensions.Logging;

namespace LifeOverYears.Providers;

public sealed class FfmpegProvider : IFfmpegProvider
{
    public const int    TargetTotalSeconds = 16;
    // Wipe duration. Halved 2s -> 1s: at 6 frames a 2s wipe left middle frames
    // only ~0.3s of clean view between back-to-back wipes (machine-gun effect).
    public const double TransitionSeconds  = 1.0;
    // Frame 0 is shown fully clean for this long before its wipe begins — a
    // short, punchy intro. Frames 1..n-1 share a uniform (longer) hold.
    public const double FirstCleanSeconds  = 1.8;
    // xfade offers 58 transitions. These five suit a fixed camera on one
    // location: each changes the image without moving the frame, so the place
    // stays put while the years change. slide*/cover*/reveal* are deliberately
    // excluded — they push the scene sideways and break the "same spot, another
    // decade" read that the whole video depends on.
    public static readonly IReadOnlyList<string> TransitionTypes = new[]
    {
        "radial", "fade", "smoothleft", "smoothright", "circleopen"
    };

    // One transition type for the WHOLE video — every cut in a single run uses
    // the same kind, so the visual grammar stays consistent across the run.
    // A different type may still be picked from one run to the next.
    public static string PickTransitionType(Random rng) => TransitionTypes[rng.Next(TransitionTypes.Count)];

    // Middle frames carry a transition on BOTH sides, so their 'pure' viewing
    // time is hold - 2*TransitionSeconds. Keep it positive or wipes overlap and
    // the sequence reads as rushed.
    public const double MinPureSecondsPerMiddleFrame = 0.3;

    // Shared timeline math — VideoSmokeTest derives its duration expectations
    // from this same method so test and provider cannot drift apart. Frame 0
    // gets a dedicated shorter hold (FirstCleanSeconds of clean view before its
    // wipe); frames 1..n-1 share a uniform hold so the chain sums to target.
    public static (double FirstHoldSeconds, double HoldSeconds, double TotalSeconds, bool Adjusted) PlanTimeline(int n)
    {
        if (n <= 1)
            return (TargetTotalSeconds, TargetTotalSeconds, TargetTotalSeconds, false);

        // clip 0 length so its clean (pre-wipe) view lasts exactly FirstCleanSeconds
        var firstHold = FirstCleanSeconds + TransitionSeconds;

        // remaining n-1 frames share a uniform hold h:
        //   firstHold + (n-1)*h - (n-1)*transition = target
        var holdSeconds = (TargetTotalSeconds - firstHold + (n - 1) * TransitionSeconds) / (n - 1);

        // Middle frames exist only when n >= 3; keep their pure view >= floor by
        // extending the total instead of overlapping wipes.
        var minRequiredHold = 2 * TransitionSeconds + MinPureSecondsPerMiddleFrame;
        var adjusted = n >= 3 && holdSeconds < minRequiredHold;
        if (adjusted)
            holdSeconds = minRequiredHold;

        var totalSeconds = firstHold + (n - 1) * holdSeconds - (n - 1) * TransitionSeconds;
        return (firstHold, holdSeconds, totalSeconds, adjusted);
    }

    private readonly ILogger<FfmpegProvider> _logger;
    private readonly string _ffmpegPath;

    public FfmpegProvider(ILogger<FfmpegProvider> logger, string ffmpegPath = "ffmpeg")
    {
        _logger = logger;
        _ffmpegPath = ffmpegPath;
    }

    public async Task<Video?> ComposeAsync(IReadOnlyList<HistoricalImage> images, string outputPath)
    {
        var transitionKind = PickTransitionType(Random.Shared);
        _logger.LogInformation(
            "Composing video from {Count} images — transition: {Transition} (same kind used for every cut in this run)",
            images.Count, transitionKind);

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath))!);

        if (!await SupportsXfadeAsync())
        {
            _logger.LogError(
                "ffmpeg at '{Path}' does not report the 'xfade' filter (requires ffmpeg 4.3+) — " +
                "skipping video assembly rather than falling back to a hard-cut concat.", _ffmpegPath);
            return null;
        }

        var n = images.Count;
        var (firstHold, holdSeconds, expectedDuration, adjusted) = PlanTimeline(n);
        if (adjusted)
        {
            _logger.LogWarning(
                "Requested {Target}s total would overlap transitions for middle frames — " +
                "extending to {Adjusted:0.##}s instead (per-frame hold floored at {MinRequired:0.##}s).",
                TargetTotalSeconds, expectedDuration, 2 * TransitionSeconds + MinPureSecondsPerMiddleFrame);
        }

        var args = BuildArgs(images, outputPath, firstHold, holdSeconds, transitionKind);
        _logger.LogDebug("ffmpeg command: {Args}", args);

        _logger.LogInformation(
            "Expected output duration: {Duration:0.##}s for {Count} frames (first {First:0.##}s, rest {Hold:0.####}s each)",
            expectedDuration, n, firstHold, holdSeconds);

        try
        {
            await RunFfmpegAsync(args);
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            _logger.LogError(
                "ffmpeg binary '{Path}' not found ({Reason}) — skipping video assembly. " +
                "Install ffmpeg to enable Step 4.", _ffmpegPath, ex.Message);
            return null;
        }

        _logger.LogInformation("Video composed: {Path}", outputPath);

        return new Video(
            Id:       Guid.NewGuid().ToString(),
            ImageIds: images.Select(i => i.Id).ToList(),
            FilePath: outputPath,
            CreatedAt: DateTimeOffset.UtcNow.ToString("o"));
    }

    // Clip 0 is a firstHold still; clips 1..n-1 are holdSeconds stills. Each is
    // normalized to 1080x1920/yuv420p/30fps, then chained with one transitionKind
    // wipe, repeated for every cut. The first wipe begins at
    // (firstHold - transition); each subsequent wipe advances by (hold - transition).
    private static string BuildArgs(
        IReadOnlyList<HistoricalImage> images, string outputPath, double firstHold, double holdSeconds,
        string transitionKind)
    {
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        var n = images.Count;

        var sb = new StringBuilder("-y ");
        for (var i = 0; i < n; i++)
        {
            var t = (i == 0 ? firstHold : holdSeconds).ToString("0.####", inv);
            sb.Append($"-loop 1 -t {t} -i \"{Path.GetFullPath(images[i].FilePath)}\" ");
        }

        var filter = new StringBuilder();
        for (var i = 0; i < n; i++)
            filter.Append(
                $"[{i}:v]scale=1080:1920:force_original_aspect_ratio=decrease," +
                $"pad=1080:1920:(ow-iw)/2:(oh-ih)/2,setsar=1,fps=30,format=yuv420p[v{i}];");

        string outLabel;
        if (n == 1)
        {
            outLabel = "v0";
        }
        else
        {
            var transition = TransitionSeconds.ToString("0.####", inv);
            var prevLabel  = "v0";
            var offset     = firstHold - TransitionSeconds; // first wipe starts after frame 0's clean view
            for (var i = 1; i < n; i++)
            {
                var stageLabel = i == n - 1 ? "outv" : $"x{i}";
                filter.Append(
                    $"[{prevLabel}][v{i}]xfade=transition={transitionKind}:duration={transition}:" +
                    $"offset={offset.ToString("0.####", inv)}[{stageLabel}];");
                prevLabel = stageLabel;
                offset += holdSeconds - TransitionSeconds;  // subsequent wipes advance by the uniform hold
            }
            outLabel = "outv";
        }

        var filterComplex = filter.ToString().TrimEnd(';');

        sb.Append($"-filter_complex \"{filterComplex}\" -map \"[{outLabel}]\" ");
        sb.Append($"-c:v libx264 -pix_fmt yuv420p -r 30 \"{outputPath}\"");
        return sb.ToString();
    }

    private async Task<bool> SupportsXfadeAsync()
    {
        try
        {
            var (exitCode, _, stdout) = await RunProcessAsync(_ffmpegPath, "-hide_banner -filters");
            return exitCode == 0 && stdout.Contains("xfade", StringComparison.OrdinalIgnoreCase);
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }

    private async Task RunFfmpegAsync(string arguments)
    {
        var (exitCode, stderr, _) = await RunProcessAsync(_ffmpegPath, arguments);
        if (exitCode != 0)
            throw new InvalidOperationException($"ffmpeg exited with code {exitCode}: {stderr}");
    }

    private static async Task<(int ExitCode, string StdErr, string StdOut)> RunProcessAsync(string fileName, string arguments)
    {
        var psi = new ProcessStartInfo(fileName, arguments)
        {
            RedirectStandardError  = true,
            RedirectStandardOutput = true,
            UseShellExecute        = false,
            CreateNoWindow         = true
        };

        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException($"Failed to start {fileName}");

        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        return (process.ExitCode, await stderrTask, await stdoutTask);
    }
}
