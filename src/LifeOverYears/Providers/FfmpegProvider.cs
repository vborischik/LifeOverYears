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
    // short, punchy intro. Frames 1..n-1 get ramped holds (see HoldWeights).
    public const double FirstCleanSeconds  = 1.8;

    // The extra clip rendered after the last era, repeating the first image so
    // the closing wipe arrives back where the video started. Platforms auto-loop
    // and the old hard cut from the last year to the first read as a glitch,
    // which is exactly where a second pass is won or lost.
    //
    // It carries the wipe and nothing else: about 0.15s of pure view after the
    // transition completes, so the loop restarts on the same image the wipe just
    // landed on and the seam is invisible. A real hold here would instead show
    // the opening year twice, back to back, at double length.
    public const double LoopTailSeconds = TransitionSeconds + 0.15;

    // Total zoom over a clip's life. 4% is deliberately small — the point is
    // that the frame is alive, not that the camera is doing something. Past
    // ~6% the crop starts eating the year overlay and the storefront sign.
    public const double ZoomEndScale = 1.04;

    // zoompan works in frames, not seconds, and it samples its input once per
    // output frame — so the input must already be at the output frame rate
    // before it runs, or the ramp advances in visible steps.
    private const int ZoomUpscaleFactor = 4;

    // Was three separate literals; the push-in needs it as arithmetic (a clip's
    // length in frames), so it is a named number now.
    private const int OutputFps = 30;
    private const int OutputWidth  = 1080;
    private const int OutputHeight = 1920;

    // Frames later in the run get progressively longer holds. Early cuts must
    // land fast — a viewer who sees nothing change between 3s and 5s leaves —
    // while a viewer still watching at 12s has already committed and can be
    // given time to read the frame.
    private static readonly double[] HoldWeights = { 1.0, 1.15, 1.3, 1.45, 1.6 };

    // xfade offers 58 transitions. These four suit a fixed camera on one
    // location: each changes the image without moving the frame, so the place
    // stays put while the years change. slide*/cover*/reveal* are deliberately
    // excluded — they push the scene sideways and break the "same spot, another
    // decade" read that the whole video depends on. "fade" is excluded for the
    // opposite reason: a crossfade between two frames of the same place reads as
    // the lens losing focus, not as a decade passing.
    public static readonly IReadOnlyList<string> TransitionTypes = new[]
    {
        "radial", "smoothleft", "smoothright", "circleopen"
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
    // from this same method so test and provider cannot drift apart.
    //
    // n is the number of era images; the plan renders n+1 clips and n
    // transitions, the extra clip being the loop tail. Clip 0 gets a dedicated
    // short hold (FirstCleanSeconds of clean view before its wipe), clips
    // 1..n-1 are ramped by HoldWeights and scaled so the chain still sums to
    // TargetTotalSeconds, and clip n is the tail.
    public static (double[] ClipSeconds, double TotalSeconds, bool Adjusted) PlanTimeline(int n)
    {
        // A single image has nothing to wipe to and no seam to hide — a tail
        // would just transition it into itself.
        if (n <= 1)
            return (new[] { (double)TargetTotalSeconds }, TargetTotalSeconds, false);

        var clips = new double[n + 1];

        // clip 0 length so its clean (pre-wipe) view lasts exactly FirstCleanSeconds
        clips[0]  = FirstCleanSeconds + TransitionSeconds;
        clips[^1] = LoopTailSeconds;

        // Every transition overlaps two clips, so the rendered seconds exceed
        // the finished duration by exactly one transition per cut. Add them back
        // before dividing, or the ramp is scaled against the wrong budget.
        var weights   = WeightsFor(n - 1);
        var sumHolds  = TargetTotalSeconds - clips[0] - LoopTailSeconds + n * TransitionSeconds;
        var unit      = sumHolds / weights.Sum();

        // Clips 1..n-1 each carry a wipe on both sides — the last one included,
        // now that the tail takes a wipe off it — so all of them need the pure
        // view floor, not just the interior ones.
        var minRequiredHold = 2 * TransitionSeconds + MinPureSecondsPerMiddleFrame;
        var adjusted = false;
        for (var i = 1; i < n; i++)
        {
            clips[i] = unit * weights[i - 1];
            if (clips[i] >= minRequiredHold)
                continue;
            // Extend the video rather than overlap wipes: a rushed sequence is
            // worse than a long one.
            clips[i] = minRequiredHold;
            adjusted = true;
        }

        // Read back from the clips actually planned. Once anything is clamped
        // the target no longer describes the result, and reporting the target
        // would make the smoke test's duration assertion a tautology.
        var totalSeconds = clips.Sum() - n * TransitionSeconds;
        return (clips, totalSeconds, adjusted);
    }

    // The ramp, stretched or trimmed to the number of holds a run actually has.
    // Runs longer than the curve keep widening at the final weight rather than
    // wrapping back to a short hold, which would read as the video speeding up
    // again just as it ends.
    private static double[] WeightsFor(int count)
    {
        var weights = new double[count];
        for (var i = 0; i < count; i++)
            weights[i] = HoldWeights[Math.Min(i, HoldWeights.Length - 1)];
        return weights;
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
        var (clipSeconds, expectedDuration, adjusted) = PlanTimeline(n);
        if (adjusted)
        {
            _logger.LogWarning(
                "Requested {Target}s total would overlap transitions for middle frames — " +
                "extending to {Adjusted:0.##}s instead (per-frame hold floored at {MinRequired:0.##}s).",
                TargetTotalSeconds, expectedDuration, 2 * TransitionSeconds + MinPureSecondsPerMiddleFrame);
        }

        var args = BuildArgs(images, outputPath, clipSeconds, transitionKind);
        _logger.LogDebug("ffmpeg command: {Args}", args);

        var inv = System.Globalization.CultureInfo.InvariantCulture;
        _logger.LogInformation(
            "Expected output duration: {Duration:0.##}s for {Count} frames — clips {Clips}s" +
            "{Tail}",
            expectedDuration, n,
            string.Join(", ", clipSeconds.Select(c => c.ToString("0.##", inv))),
            clipSeconds.Length > n ? $" (last repeats {Path.GetFileName(images[0].FilePath)} to close the loop)" : "");

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

    // One "-loop 1 -t" input per planned clip, each normalized to
    // 1080x1920/yuv420p/30fps, then chained with one transitionKind wipe per
    // cut. The first wipe begins at (clip[0] - transition); each subsequent one
    // advances by (clip[i] - transition), so a ramped plan spaces the cuts out
    // as the run goes on.
    //
    // clipSeconds is one longer than images when a loop tail is planned, and
    // that final input is images[0] again. The image ORDER is the caller's —
    // never reordered here — so the tail is a repeat, not a reshuffle.
    private static string BuildArgs(
        IReadOnlyList<HistoricalImage> images, string outputPath, double[] clipSeconds,
        string transitionKind)
    {
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        var n     = images.Count;
        var clips = clipSeconds.Length;

        var sb = new StringBuilder("-y ");
        for (var i = 0; i < clips; i++)
        {
            var source = images[i < n ? i : 0].FilePath;
            var t = clipSeconds[i].ToString("0.####", inv);
            sb.Append($"-loop 1 -t {t} -i \"{Path.GetFullPath(source)}\" ");
        }

        // Every clip is a still, so without this the video reads as a slideshow of
        // screenshots even between wipes. A slow push-in gives each frame motion
        // without moving the composition.
        //
        // Order is load-bearing, in three ways:
        //   * The upscale comes first. zoompan crops from its input at output
        //     resolution, so with no headroom the result is visibly soft and the
        //     pan jitters along integer pixel boundaries.
        //   * fps before zoompan. zoompan advances 'on' once per frame it sees;
        //     a still at the source rate would step through the ramp instead of
        //     gliding.
        //   * d=1, NOT the clip's frame count. 'd' is how many frames zoompan
        //     emits per frame it receives, and -loop 1 -t plus fps has already
        //     expanded this input to the clip's full length — measured, d=84 on
        //     a 2.8s clip renders 84x84 frames and 235 seconds of video. The
        //     clip's length belongs in the ramp's denominator instead, which is
        //     where it makes 'on' reach ZoomEndScale exactly on the final frame.
        //     (D-1, not D: landing short leaves a small jump at the loop seam.)
        var zoomStep = (ZoomEndScale - 1).ToString("0.####", inv);
        var upscaleW = OutputWidth  * ZoomUpscaleFactor;
        var upscaleH = OutputHeight * ZoomUpscaleFactor;

        var filter = new StringBuilder();
        for (var i = 0; i < clips; i++)
        {
            var frames = (int)Math.Round(clipSeconds[i] * OutputFps, MidpointRounding.AwayFromZero);
            var ramp   = Math.Max(1, frames - 1);
            filter.Append(
                $"[{i}:v]scale={upscaleW}:{upscaleH}:force_original_aspect_ratio=decrease," +
                $"pad={upscaleW}:{upscaleH}:(ow-iw)/2:(oh-ih)/2,setsar=1,fps={OutputFps}," +
                $"zoompan=z='1+{zoomStep}*on/{ramp}':x='iw/2-(iw/zoom/2)':y='ih/2-(ih/zoom/2)':" +
                $"d=1:s={OutputWidth}x{OutputHeight}:fps={OutputFps}," +
                $"format=yuv420p[v{i}];");
        }

        string outLabel;
        if (clips == 1)
        {
            outLabel = "v0";
        }
        else
        {
            var transition = TransitionSeconds.ToString("0.####", inv);
            var prevLabel  = "v0";
            var offset     = clipSeconds[0] - TransitionSeconds; // first wipe starts after frame 0's clean view
            for (var i = 1; i < clips; i++)
            {
                var stageLabel = i == clips - 1 ? "outv" : $"x{i}";
                filter.Append(
                    $"[{prevLabel}][v{i}]xfade=transition={transitionKind}:duration={transition}:" +
                    $"offset={offset.ToString("0.####", inv)}[{stageLabel}];");
                prevLabel = stageLabel;
                offset += clipSeconds[i] - TransitionSeconds;
            }
            outLabel = "outv";
        }

        var filterComplex = filter.ToString().TrimEnd(';');

        sb.Append($"-filter_complex \"{filterComplex}\" -map \"[{outLabel}]\" ");
        sb.Append($"-c:v libx264 -pix_fmt yuv420p -r {OutputFps} \"{outputPath}\"");
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
