// TODO: remove smoke test
using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using LifeOverYears.Models;
using LifeOverYears.Services.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace LifeOverYears.Services;

// TODO: remove smoke test
// Isolated ffmpeg smoke test: validates video assembly only. Generates its
// own test images via ffmpeg's lavfi color source (no vision, no prompts,
// no API keys), then exercises the real overlay + IVideoService/FfmpegProvider
// path via the same VideoAssemblyRunner the 'collect' and 'assemble' CLI use.
public static class VideoSmokeTest
{
    private static readonly int[] Years = { 1975, 1985, 1995, 2005, 2015, 2025 };
    private static readonly int[] PartialYears = { 1985, 2015 };

    private static readonly Dictionary<int, string> Colors = new()
    {
        { 1975, "firebrick" },
        { 1985, "darkorange" },
        { 1995, "gold" },
        { 2005, "seagreen" },
        { 2015, "steelblue" },
        { 2025, "indigo" }
    };

    private const int ImageWidth  = 1024;
    private const int ImageHeight = 1536;
    private const int ExpectedVideoWidth  = 1080;
    private const int ExpectedVideoHeight = 1920;

    // Duration expectations are derived from FfmpegProvider.PlanTimeline —
    // the exact method the provider itself uses (including the middle-frame
    // overlap guard) — so test and provider cannot drift apart.
    private static double ExpectedDurationSeconds(int frameCount) =>
        Providers.FfmpegProvider.PlanTimeline(frameCount).TotalSeconds;

    public static async Task<int> RunAsync(
        IVideoService videoService, IYearOverlayService overlay, ILogger logger, CapturingLoggerProvider logCapture)
    {
        logger.LogInformation("[SmokeVideo] VideoSmokeTest starting");

        var findings = new List<(string Id, string Desc, bool Pass, string Detail)>();
        var root        = Path.Combine("output", "smoke-video");
        var imagesDir   = Path.Combine(root, "images");
        var stampedDir  = Path.Combine(root, "stamped");
        var videoDir    = Path.Combine(root, "video");
        var outputPath  = Path.Combine(videoDir, "timeline.mp4");
        Directory.CreateDirectory(imagesDir);
        Directory.CreateDirectory(videoDir);

        // V7 — pure C# logic, no ffmpeg binary involved, so it runs unconditionally
        // and is never in the ffmpeg-missing skip list. V6 below only observes the
        // one transition kind Random.Shared happens to draw for this one process's
        // single video; this sweeps many seeds and frame counts directly against
        // the real BuildArgs (via reflection, since it's private) so the "same
        // kind for every cut in a run" invariant is checked far more thoroughly.
        findings.Add(CheckTransitionConsistency());
        findings.AddRange(CheckTimelinePlan());

        // V5 first — every other check depends on these binaries existing.
        var ffmpegOk  = await BinaryAvailable("ffmpeg", logger);
        var ffprobeOk = await BinaryAvailable("ffprobe", logger);
        if (!ffmpegOk || !ffprobeOk)
        {
            var missingBinaries = string.Join(", ", new[] { !ffmpegOk ? "ffmpeg" : null, !ffprobeOk ? "ffprobe" : null }
                .Where(m => m is not null));
            findings.Add(("V5", "ffmpeg and ffprobe binaries found in PATH",
                false, $"ffmpeg not found in PATH — missing: {missingBinaries}"));
            foreach (var (id, desc) in SkippedChecks())
                findings.Add((id, desc, false, "skipped — ffmpeg not found in PATH"));

            await WriteReport(findings, logger);
            PrintSummary(findings);
            return 1;
        }
        findings.Add(("V5", "ffmpeg and ffprobe binaries found in PATH",
            true, "Both binaries responded to -version"));

        // Generate 6 visually distinct test frames (un-stamped source).
        foreach (var year in Years)
            await GenerateTestImageAsync(year, Path.Combine(imagesDir, $"{year}.png"), logger);

        // Exercise the real production tail: verify -> overlay -> compose,
        // via the same VideoAssemblyRunner 'collect' and 'assemble' use.
        // Composed in the same order the product uses — newest first, then the
        // rewind — so the video this test leaves on disk is a fair sample of
        // what a real run produces. It is the artefact anyone eyeballs when they
        // want to see what the pipeline makes without paying for a run, and a
        // fixture that renders a different cut from production teaches the wrong
        // thing. Order preservation itself is asserted separately by O6, against
        // a deliberately descending list, so nothing is lost by matching here.
        var messagesBeforeFull = logCapture.Messages.Count;
        var (mainMissing, video) = await VideoAssemblyRunner.RunAsync(
            overlay, videoService, imagesDir, stampedDir, outputPath,
            VideoAssemblyRunner.NewestFirst(Years), logger);
        var messagesAfterFull = logCapture.Messages.Count;

        // O1 — stamped/{year}.png exists for every year, same dimensions as source.
        var o1Errors = new List<string>();
        foreach (var year in Years)
        {
            var stampedPath = Path.Combine(stampedDir, $"{year}.png");
            if (!File.Exists(stampedPath))
            {
                o1Errors.Add($"{year}: stamped file missing");
                continue;
            }
            var sourceDim  = await FfprobeDimensionsAsync(Path.Combine(imagesDir, $"{year}.png"));
            var stampedDim = await FfprobeDimensionsAsync(stampedPath);
            if (sourceDim != stampedDim)
                o1Errors.Add($"{year}: dimensions changed {sourceDim} -> {stampedDim}");
        }
        findings.Add(("O1", "stamped/{year}.png exists for every test year, same dimensions as source",
            o1Errors.Count == 0, o1Errors.Count == 0 ? "All stamped images present with matching dimensions" : string.Join("; ", o1Errors)));

        // O2 — stamping actually changed the file (bar + text drawn onto it).
        var o2Errors = new List<string>();
        foreach (var year in Years)
        {
            var sourcePath  = Path.Combine(imagesDir, $"{year}.png");
            var stampedPath = Path.Combine(stampedDir, $"{year}.png");
            if (!File.Exists(stampedPath)) continue; // already reported by O1
            var sourceLen  = new FileInfo(sourcePath).Length;
            var stampedLen = new FileInfo(stampedPath).Length;
            if (sourceLen == stampedLen)
                o2Errors.Add($"{year}: stamped size ({stampedLen}b) == source size ({sourceLen}b)");
        }
        findings.Add(("O2", "stamped output file size differs from the un-stamped source",
            o2Errors.Count == 0, o2Errors.Count == 0 ? "All stamped files differ in size from their source" : string.Join("; ", o2Errors)));


        // O6 — the runner plays the years in the order it is handed, so a caller
        // can open on the present and rewind. It used to sort them, which made
        // that impossible and made the loop tail always repeat the oldest year.
        // Driven through a recording IVideoService: no ffmpeg render, and it
        // asserts the list ComposeAsync actually receives rather than inferring
        // it from a command line.
        var o6Errors = new List<string>();
        var descending = Years.OrderByDescending(y => y).ToList();
        // Recorded at the ffmpeg boundary, not at IVideoService: the real
        // VideoService sits between them and used to re-sort by year, which is
        // exactly the bug this check exists to catch. Substituting the whole
        // service would step over the layer under test — an earlier version of
        // this check did, and passed while the shipped video came out ascending.
        var recorder = new RecordingFfmpegProvider();
        var realVideoService = new VideoService(recorder, NullLogger<VideoService>.Instance);
        await VideoAssemblyRunner.RunAsync(
            overlay, realVideoService, imagesDir, Path.Combine(root, "order-stamped"),
            Path.Combine(root, "order-video", "timeline.mp4"), descending, logger);

        if (recorder.Received is null)
            o6Errors.Add("the ffmpeg provider was never reached");
        else
        {
            var got = recorder.Received.Select(i => i.Year).ToList();
            if (!got.SequenceEqual(descending))
                o6Errors.Add($"passed [{string.Join(", ", descending)}], composed [{string.Join(", ", got)}]");
        }

        findings.Add(("O6",
            "The whole assembly chain — runner, VideoService, provider — hands ffmpeg the years in the order the caller passed them",
            o6Errors.Count == 0,
            o6Errors.Count == 0
                ? $"[{string.Join(", ", descending)}] survived intact to ComposeAsync"
                : string.Join("; ", o6Errors)));

        var fileInfo = File.Exists(outputPath) ? new FileInfo(outputPath) : null;
        var v1 = mainMissing.Count == 0 && video is not null && fileInfo is { Length: > 0 };
        findings.Add(("V1", "Video file exists and has non-zero size",
            v1, v1 ? $"{fileInfo!.Length} bytes at {outputPath}" : "file missing or empty (composition failed)"));

        if (!v1)
        {
            // O1/O2/O6 and V1 are already settled by this point: the first three
            // read the stamped frames, which exist whether or not ffmpeg composed.
            foreach (var (id, desc) in SkippedChecks().Where(c => c.Id is not ("O1" or "O2" or "O6" or "V1")))
                findings.Add((id, desc, false, "skipped — video file not produced"));
            await WriteReport(findings, logger);
            PrintSummary(findings);
            return 1;
        }

        var probe = await FfprobeAsync(outputPath);

        var v2 = probe.Width == ExpectedVideoWidth && probe.Height == ExpectedVideoHeight;
        findings.Add(("V2", $"Video resolution == {ExpectedVideoWidth}x{ExpectedVideoHeight}",
            v2, $"actual: {probe.Width}x{probe.Height}"));

        var expectedDuration = ExpectedDurationSeconds(Years.Length);
        var v3 = Math.Abs(probe.Duration - expectedDuration) <= 0.5;
        findings.Add(("V3",
            $"Duration is {expectedDuration}s ± 0.5s (fixed target; per-frame hold computed dynamically for {Years.Length} frames)",
            v3, $"actual: {probe.Duration:F2}s"));

        var v4 = probe.CodecName == "h264" && probe.PixFmt == "yuv420p";
        findings.Add(("V4", "codec_name == h264, pix_fmt == yuv420p",
            v4, $"actual: codec_name={probe.CodecName}, pix_fmt={probe.PixFmt}"));

        // V6: confirm the xfade path was actually taken, not a hard-cut concat —
        // detecting a wipe vs. a cut from pixels alone is out of scope for a
        // smoke test, so this asserts on the ffmpeg command FfmpegProvider
        // logged at Debug level. One transition kind is now picked per run and
        // reused for every cut, so this also checks that kind is from the pool
        // and that every cut in this one video uses the exact same kind.
        var commandLine = logCapture.Messages.FirstOrDefault(m =>
            m.Contains("ffmpeg command:", StringComparison.OrdinalIgnoreCase));
        var v6Errors = new List<string>();
        if (commandLine is null)
            v6Errors.Add("no 'ffmpeg command:' log line captured");
        else
        {
            if (!commandLine.Contains("xfade", StringComparison.OrdinalIgnoreCase))
                v6Errors.Add("command does not use xfade (hard-cut concat?)");

            var used = System.Text.RegularExpressions.Regex
                .Matches(commandLine, @"xfade=transition=([a-z]+)")
                .Select(m => m.Groups[1].Value)
                .ToList();

            // n frames now yield n cuts, not n-1: the loop tail takes one more.
            if (used.Count != Years.Length)
                v6Errors.Add($"{used.Count} transitions for {Years.Length} frames (expected {Years.Length})");
            foreach (var t in used.Where(t => !Providers.FfmpegProvider.TransitionTypes.Contains(t)))
                v6Errors.Add($"transition '{t}' is not in the pool");
            if (used.Distinct().Count() > 1)
                v6Errors.Add($"transitions differ within one video, expected the same kind throughout: {string.Join(", ", used)}");
        }

        findings.Add(("V6",
            $"ffmpeg used filter_complex xfade (not concat) with the same pooled transition kind reused for every cut across {Years.Length} frames",
            v6Errors.Count == 0,
            v6Errors.Count == 0 ? commandLine! : string.Join("; ", v6Errors)));

        // O3 — a PARTIAL year list must only wait for, stamp, and assemble
        // exactly those years, leaving the rest of the images/ folder alone.
        var o3Errors = new List<string>();
        var partialStampedDir = Path.Combine(root, "partial-stamped");
        var partialVideoPath  = Path.Combine(root, "partial-video", "timeline.mp4");
        if (Directory.Exists(partialStampedDir))
            Directory.Delete(partialStampedDir, recursive: true);
        Directory.CreateDirectory(Path.GetDirectoryName(partialVideoPath)!);

        var messagesBeforePartial = logCapture.Messages.Count;
        // Same cut as the full run: O3 compares the stamped set, not its order.
        var (partialMissing, partialVideo) = await VideoAssemblyRunner.RunAsync(
            overlay, videoService, imagesDir, partialStampedDir, partialVideoPath,
            VideoAssemblyRunner.NewestFirst(PartialYears), logger);

        if (partialMissing.Count > 0)
            o3Errors.Add($"watcher reported missing years for a partial request: {string.Join(", ", partialMissing)}");
        if (partialVideo is null)
            o3Errors.Add("partial video composition failed");

        var stampedNames = Directory.Exists(partialStampedDir)
            ? Directory.GetFiles(partialStampedDir, "*.png")
                .Select(f => Path.GetFileNameWithoutExtension(f)!)
                .ToHashSet()
            : new HashSet<string>();
        var expectedNames = PartialYears.Select(y => y.ToString()).ToHashSet();
        if (!stampedNames.SetEquals(expectedNames))
            o3Errors.Add($"partial-stamped/ contains [{string.Join(", ", stampedNames)}], expected exactly [{string.Join(", ", expectedNames)}]");

        var partialDuration = 0.0;
        if (partialVideo is not null && File.Exists(partialVideoPath))
        {
            var partialProbe = await FfprobeAsync(partialVideoPath);
            partialDuration = partialProbe.Duration;
            var expectedPartialDuration = ExpectedDurationSeconds(PartialYears.Length);
            if (Math.Abs(partialDuration - expectedPartialDuration) > 0.5)
                o3Errors.Add($"partial video duration {partialDuration:F2}s not within 0.5s of expected {expectedPartialDuration}s");
        }

        findings.Add(("O3",
            $"Partial year list [{string.Join(", ", PartialYears)}] only waits for/stamps those years and produces a {PartialYears.Length}-frame video",
            o3Errors.Count == 0,
            o3Errors.Count == 0
                ? $"stamped exactly [{string.Join(", ", stampedNames)}]; duration {partialDuration:F2}s"
                : string.Join("; ", o3Errors)));

        // O4 — the middle-frame overlap guard fires exactly when PlanTimeline
        // says it should. n<=2 has no middle frames, so it must never warn;
        // the full run must warn iff its plan was adjusted. (With the current
        // 16s target and 6 frames, hold 4.33s >= required 4.3s, so neither
        // run warns — the assertion is against the shared plan, not a
        // hardcoded expectation, so it stays correct if the constants change.)
        const string overlapWarningMarker = "would overlap transitions";
        var allMessages = logCapture.Messages;
        var fullWarned = allMessages
            .Skip(messagesBeforeFull).Take(messagesAfterFull - messagesBeforeFull)
            .Any(m => m.Contains(overlapWarningMarker, StringComparison.OrdinalIgnoreCase));
        var partialWarned = allMessages
            .Skip(messagesBeforePartial)
            .Any(m => m.Contains(overlapWarningMarker, StringComparison.OrdinalIgnoreCase));

        var expectFullWarn = Providers.FfmpegProvider.PlanTimeline(Years.Length).Adjusted;
        var o4Errors = new List<string>();
        if (partialWarned)
            o4Errors.Add($"overlap warning logged for n={PartialYears.Length} (no middle frames — guard must not fire)");
        if (fullWarned != expectFullWarn)
            o4Errors.Add($"n={Years.Length}: warning logged={fullWarned}, but plan Adjusted={expectFullWarn}");

        findings.Add(("O4",
            $"Overlap guard fires exactly per PlanTimeline: never for n={PartialYears.Length}, and for n={Years.Length} iff the plan was adjusted",
            o4Errors.Count == 0,
            o4Errors.Count == 0
                ? $"n={Years.Length} warned={fullWarned} (plan adjusted={expectFullWarn}); n={PartialYears.Length} warned={partialWarned}"
                : string.Join("; ", o4Errors)));

        // O5 — a hand-corrected "{year}-clean.png" must satisfy the year in place
        // of "{year}.png", and win when both are present. Assembly reads through
        // FindEraImage, so this is what decides which pixels reach the video.
        var o5Errors = new List<string>();
        var o5Dir = Path.Combine(root, "clean-variant");
        Directory.CreateDirectory(o5Dir);
        try
        {
            const int probeYear = 1975;
            var cleanOnly = Path.Combine(o5Dir, $"{probeYear}-clean.png");
            await GenerateTestImageAsync(probeYear, cleanOnly, logger);

            var found = VideoAssemblyRunner.FindEraImage(o5Dir, probeYear);
            if (found != cleanOnly)
                errsAdd($"'-clean' alone not accepted: resolved to {found ?? "(null)"}");

            // Both present: the cleaned file is the deliberate replacement.
            var plain = Path.Combine(o5Dir, $"{probeYear}.png");
            await GenerateTestImageAsync(probeYear, plain, logger);
            found = VideoAssemblyRunner.FindEraImage(o5Dir, probeYear);
            if (found != cleanOnly)
                errsAdd($"'-clean' did not win over the plain file: resolved to {found ?? "(null)"}");

            if (VideoAssemblyRunner.FindEraImage(o5Dir, 1801) is not null)
                errsAdd("a year with no file at all resolved to something");

            void errsAdd(string m) => o5Errors.Add(m);
        }
        finally
        {
            Directory.Delete(o5Dir, recursive: true);
        }

        findings.Add(("O5",
            "A hand-corrected {year}-clean.png satisfies the year and takes precedence over {year}.png",
            o5Errors.Count == 0,
            o5Errors.Count == 0 ? "clean-variant resolution correct" : string.Join("; ", o5Errors)));

        await WriteReport(findings, logger);
        PrintSummary(findings);

        return findings.All(f => f.Pass) ? 0 : 1;
    }

    // Sweeps many seeds and frame counts directly against the real BuildArgs
    // (private, reached via reflection so this exercises the actual
    // implementation rather than a reimplementation that could drift from it).
    // No ffmpeg process involved — pure string assembly — so this always runs.
    private static (string, string, bool, string) CheckTransitionConsistency()
    {
        var errs = new List<string>();
        var ffmpegType = typeof(Providers.FfmpegProvider);
        var buildArgs = ffmpegType.GetMethod("BuildArgs",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)
            ?? throw new InvalidOperationException("FfmpegProvider.BuildArgs not found via reflection");

        var frameCounts = new[] { 2, 3, 6, 10 };
        for (var seed = 1; seed <= 30; seed++)
        {
            foreach (var frameCount in frameCounts)
            {
                var rng  = new Random(seed);
                var kind = Providers.FfmpegProvider.PickTransitionType(rng);

                var images = Enumerable.Range(0, frameCount)
                    .Select(i => new HistoricalImage(
                        Id: $"img{i}", PromptId: $"prompt{i}", Year: 1975 + i * 10,
                        FilePath: $"fake{i}.png", Provider: "test", CreatedAt: "2025-01-01T00:00:00Z"))
                    .ToList();

                var (clipSeconds, _, _) = Providers.FfmpegProvider.PlanTimeline(frameCount);
                var args = (string)buildArgs.Invoke(null,
                    new object[] { images, "out.mp4", clipSeconds, kind })!;

                var used = System.Text.RegularExpressions.Regex
                    .Matches(args, @"xfade=transition=([a-z]+)")
                    .Select(m => m.Groups[1].Value)
                    .ToList();

                // One transition per cut, and the loop tail adds a cut of its own.
                var expectedCuts = clipSeconds.Length - 1;
                if (used.Count != expectedCuts)
                    errs.Add($"seed={seed} frames={frameCount}: {used.Count} transitions (expected {expectedCuts})");
                else if (used.Distinct().Count() > 1)
                    errs.Add($"seed={seed} frames={frameCount}: transitions differ within one run: {string.Join(", ", used)}");
                else if (used.Count > 0 && used[0] != kind)
                    errs.Add($"seed={seed} frames={frameCount}: used '{used[0]}' but PickTransitionType picked '{kind}'");
            }
        }

        return ("V7",
            "BuildArgs uses the exact same transition kind for every cut within one run, across many seeds and frame counts",
            errs.Count == 0,
            errs.Count == 0
                ? $"Transition kind stayed constant across 30 seeds x {frameCounts.Length} frame counts"
                : string.Join("; ", errs));
    }

    // V8-V13 — the retention shape of the timeline. Pure arithmetic over
    // PlanTimeline and the real BuildArgs, so no ffmpeg binary is involved and
    // these run even on a box without it.
    private static List<(string Id, string Desc, bool Pass, string Detail)> CheckTimelinePlan()
    {
        var results = new List<(string, string, bool, string)>();
        var f = typeof(Providers.FfmpegProvider);
        var buildArgs = f.GetMethod("BuildArgs",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)
            ?? throw new InvalidOperationException("FfmpegProvider.BuildArgs not found via reflection");

        const double transition = Providers.FfmpegProvider.TransitionSeconds;
        var minHold  = 2 * transition + Providers.FfmpegProvider.MinPureSecondsPerMiddleFrame;
        var counts   = new[] { 2, 3, 6, 10 };
        var n6       = Years.Length;
        var (plan6, total6, adjusted6) = Providers.FfmpegProvider.PlanTimeline(n6);

        string Fmt(IEnumerable<double> xs) =>
            string.Join(", ", xs.Select(x => x.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)));

        // V8 — the ramp. Strictly increasing while the weight curve still has
        // distinct values left; merely non-decreasing past that, because a run
        // longer than the curve repeats its final weight rather than dropping
        // back to a short hold. A flat or falling ramp is the bug: it means the
        // early cuts stopped being the fast ones.
        var v8 = new List<string>();
        foreach (var n in counts)
        {
            var (clips, _, _) = Providers.FfmpegProvider.PlanTimeline(n);
            var holds = clips.Skip(1).Take(n - 1).ToList();
            for (var i = 1; i < holds.Count; i++)
                if (holds[i] < holds[i - 1] - 1e-9)
                    v8.Add($"n={n}: hold {i + 1} ({holds[i]:0.##}s) is shorter than hold {i} ({holds[i - 1]:0.##}s)");
        }
        var holds6 = plan6.Skip(1).Take(n6 - 1).ToList();
        for (var i = 1; i < holds6.Count; i++)
            if (holds6[i] <= holds6[i - 1] + 1e-9)
                v8.Add($"n={n6}: holds {i} and {i + 1} are equal ({holds6[i]:0.##}s) — the ramp is flat where the curve is distinct");
        results.Add(("V8", $"Holds ramp upward across clips 1..n-1 — strictly at n={n6}, never falling at any n",
            v8.Count == 0, v8.Count == 0 ? $"n={n6}: {Fmt(holds6)}" : string.Join("; ", v8)));

        // V9 — the loop tail exists and is the first image again, so the closing
        // wipe lands where the video started and the platform's auto-loop has no
        // seam to show.
        var v9 = new List<string>();
        foreach (var n in counts)
        {
            var (clips, _, _) = Providers.FfmpegProvider.PlanTimeline(n);
            if (clips.Length != n + 1)
            {
                v9.Add($"n={n}: {clips.Length} clips planned, expected {n + 1}");
                continue;
            }
            if (Math.Abs(clips[^1] - Providers.FfmpegProvider.LoopTailSeconds) > 1e-9)
                v9.Add($"n={n}: tail is {clips[^1]:0.##}s, expected {Providers.FfmpegProvider.LoopTailSeconds:0.##}s");

            var images = FakeImages(n);
            var args   = (string)buildArgs.Invoke(null, new object[] { images, "out.mp4", clips, "radial" })!;
            var inputs = System.Text.RegularExpressions.Regex
                .Matches(args, "-i \"([^\"]+)\"").Select(m => m.Groups[1].Value).ToList();

            if (inputs.Count != n + 1)
                v9.Add($"n={n}: {inputs.Count} inputs emitted, expected {n + 1}");
            else if (inputs[^1] != inputs[0])
                v9.Add($"n={n}: tail input is {Path.GetFileName(inputs[^1])}, expected the first image {Path.GetFileName(inputs[0])}");
        }
        results.Add(("V9", "Every plan renders n+1 clips and the last input repeats the first image, so the auto-loop restart is seamless",
            v9.Count == 0, v9.Count == 0 ? $"n+1 clips and a repeated opening frame at n={string.Join("/", counts)}" : string.Join("; ", v9)));

        // V10 — the ramp redistributes the budget, it does not spend more of it.
        // Only meaningful when nothing was clamped: once the floor fires the
        // plan is deliberately longer than the target.
        var v10 = new List<string>();
        foreach (var n in counts)
        {
            var (clips, total, adjusted) = Providers.FfmpegProvider.PlanTimeline(n);
            if (adjusted) continue;
            var rendered = clips.Sum() - n * transition;
            if (Math.Abs(rendered - Providers.FfmpegProvider.TargetTotalSeconds) > 0.01)
                v10.Add($"n={n}: clips sum to {rendered:0.###}s, expected {Providers.FfmpegProvider.TargetTotalSeconds}s");
            if (Math.Abs(rendered - total) > 0.01)
                v10.Add($"n={n}: reported total {total:0.###}s disagrees with the clip array ({rendered:0.###}s)");
        }
        results.Add(("V10", $"An unadjusted plan's clips minus its n transitions equal {Providers.FfmpegProvider.TargetTotalSeconds}s (±0.01)",
            v10.Count == 0, v10.Count == 0 ? $"n={n6}: {total6:0.###}s from clips [{Fmt(plan6)}]" : string.Join("; ", v10)));

        // V11 — every clip between the first and the tail now carries a wipe on
        // both sides, the last era included, so all of them owe the pure-view
        // floor. The tail is exempt by design: it exists only to carry a wipe.
        var v11 = new List<string>();
        foreach (var n in counts)
        {
            var (clips, _, _) = Providers.FfmpegProvider.PlanTimeline(n);
            for (var i = 1; i < n; i++)
                if (clips[i] < minHold - 1e-9)
                    v11.Add($"n={n}: clip {i} is {clips[i]:0.##}s, below the {minHold:0.##}s floor");
        }
        results.Add(("V11", $"Every clip 1..n-1 holds at least {minHold:0.##}s, so back-to-back wipes never eat the whole frame",
            v11.Count == 0, v11.Count == 0 ? $"floor respected at n={string.Join("/", counts)}" : string.Join("; ", v11)));

        // V12 — the reason the ramp exists. A viewer who sees nothing change for
        // four seconds leaves, and the old uniform hold left a 2.6s dead patch
        // at exactly the moment they were deciding. Asserted on the production
        // frame count: a two-frame run is a degenerate test shape whose single
        // long hold is arithmetically correct and says nothing about retention.
        const double maxGapSeconds = 4.0;
        var offsets = WipeOffsets(plan6);
        var v12 = new List<string>();
        for (var i = 1; i < offsets.Count; i++)
        {
            var gap = offsets[i] - offsets[i - 1];
            if (gap > maxGapSeconds + 1e-9)
                v12.Add($"{gap:0.##}s of no movement between the wipes at {offsets[i - 1]:0.##}s and {offsets[i]:0.##}s");
        }
        results.Add(("V12", $"At n={n6} no two consecutive wipes are more than {maxGapSeconds:0.##}s apart",
            v12.Count == 0,
            v12.Count == 0
                ? $"wipes at {Fmt(offsets)} (largest gap {(offsets.Count > 1 ? offsets.Zip(offsets.Skip(1), (a, b) => b - a).Max() : 0):0.##}s)"
                : string.Join("; ", v12)));

        // V13 — a crossfade between two frames of the same location reads as the
        // lens losing focus, not as a decade passing.
        var hasFade = Providers.FfmpegProvider.TransitionTypes
            .Any(t => t.Equals("fade", StringComparison.OrdinalIgnoreCase));
        results.Add(("V13", "'fade' is not in the transition pool — a crossfade of one place reads as a focus wobble, not a change of decade",
            !hasFade,
            hasFade ? "'fade' is still in TransitionTypes"
                    : $"pool: {string.Join(", ", Providers.FfmpegProvider.TransitionTypes)}"));

        // V14 — the push-in's filter chain. Every term in it is load-bearing and
        // silently degrades rather than failing if it moves: a missing upscale
        // gives a soft, jittering crop, fps after zoompan gives a stepped ramp,
        // and a wrong denominator leaves the zoom short of its end scale at the
        // loop seam. None of that shows up as an error, only as a worse video.
        var v14 = new List<string>();
        var upscale = $"{1080 * 4}:{1920 * 4}";
        foreach (var n in counts)
        {
            var (clips, _, _) = Providers.FfmpegProvider.PlanTimeline(n);
            var args = (string)buildArgs.Invoke(null,
                new object[] { FakeImages(n), "out.mp4", clips, "radial" })!;

            var chains = args.Split(';').Where(c => c.Contains("zoompan", StringComparison.Ordinal)).ToList();
            if (chains.Count != clips.Length)
            {
                v14.Add($"n={n}: {chains.Count} chains carry zoompan, expected {clips.Length}");
                continue;
            }

            for (var i = 0; i < clips.Length; i++)
            {
                var chain  = chains[i];
                var frames = (int)Math.Round(clips[i] * 30, MidpointRounding.AwayFromZero);

                // The clip's length in frames drives the ramp's denominator, not
                // zoompan's d=. d is frames emitted per frame received, and the
                // input is already the clip's full length, so d=1 is the only
                // value that preserves the timeline — d=frames renders the clip
                // frames times too long.
                if (!chain.Contains($"*on/{frames - 1}'", StringComparison.Ordinal))
                    v14.Add($"n={n} clip {i}: ramp is not over {frames - 1} frames ({clips[i]:0.##}s at 30fps)");
                if (!chain.Contains(":d=1:", StringComparison.Ordinal))
                    v14.Add($"n={n} clip {i}: d= is not 1 — the clip will render its own length squared");

                var fpsAt  = chain.IndexOf("fps=30", StringComparison.Ordinal);
                var zoomAt = chain.IndexOf("zoompan", StringComparison.Ordinal);
                if (fpsAt < 0 || fpsAt > zoomAt)
                    v14.Add($"n={n} clip {i}: no fps=30 before zoompan — the ramp will advance in steps");

                if (!chain.Contains($"scale={upscale}:", StringComparison.Ordinal)
                    || !chain.Contains($"pad={upscale}:", StringComparison.Ordinal))
                    v14.Add($"n={n} clip {i}: not upscaled to {upscale} before zoompan — the crop will be soft");
                if (!chain.Contains(":s=1080x1920:", StringComparison.Ordinal))
                    v14.Add($"n={n} clip {i}: zoompan does not output exactly 1080x1920");
            }
        }
        // V15 — the no-tail plan. n clips, n-1 transitions, the same total,
        // and the last clip a real hold rather than the tail's wipe-only stub;
        // the loop plan is unchanged by the overload existing.
        var v15 = new List<string>();
        foreach (var n in new[] { 2, 3, 6, 8 })
        {
            var (clips, total, _) = Providers.FfmpegProvider.PlanTimeline(n, loopTail: false);
            var (loop, loopTotal, _) = Providers.FfmpegProvider.PlanTimeline(n, loopTail: true);
            if (clips.Length != n) v15.Add($"n={n}: {clips.Length} clips planned without a tail, expected {n}");
            if (Math.Abs(total - Providers.FfmpegProvider.TargetTotalSeconds) > 0.5) v15.Add($"n={n}: no-tail total {total:0.##}s, expected ~{Providers.FfmpegProvider.TargetTotalSeconds}s");
            if (clips[^1] <= Providers.FfmpegProvider.LoopTailSeconds) v15.Add($"n={n}: last clip {clips[^1]:0.##}s is tail-sized — it should hold the final frame");
            if (loop.Length != n + 1 || Math.Abs(loopTotal - total) > 0.5) v15.Add($"n={n}: the loop plan changed ({loop.Length} clips, {loopTotal:0.##}s)");
        }
        var (six, _, _) = Providers.FfmpegProvider.PlanTimeline(6, loopTail: false);
        results.Add(("V15", "A no-tail plan renders n clips and n-1 transitions to the same total, ends on a real hold of the last frame, and leaves the loop plan untouched",
            v15.Count == 0,
            v15.Count == 0
                ? $"n=6 no-tail: {string.Join(", ", six.Select(c => c.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)))}s; loop plan unchanged at n=2/3/6/8"
                : string.Join("; ", v15)));

        results.Add(("V14", "Every clip carries a zoompan push-in: upscaled first, fps before it, output back to 1080x1920, and the ramp spanning that clip's own frame count",
            v14.Count == 0,
            v14.Count == 0
                ? $"zoom to {Providers.FfmpegProvider.ZoomEndScale:0.##}x over each clip, upscaled {upscale} at n={string.Join("/", counts)}"
                : string.Join("; ", v14.Take(5))));

        return results.Select(r => (r.Item1, r.Item2, r.Item3, r.Item4)).ToList();
    }

    // Where each wipe begins, in finished-video seconds. Mirrors the offset walk
    // in BuildArgs: the first starts when clip 0's clean view ends, and each one
    // after advances by its clip's length minus the overlap.
    private static List<double> WipeOffsets(double[] clips)
    {
        var offsets = new List<double>();
        if (clips.Length < 2) return offsets;
        var offset = clips[0] - Providers.FfmpegProvider.TransitionSeconds;
        for (var i = 1; i < clips.Length; i++)
        {
            offsets.Add(offset);
            offset += clips[i] - Providers.FfmpegProvider.TransitionSeconds;
        }
        return offsets;
    }

    private static List<HistoricalImage> FakeImages(int count) =>
        Enumerable.Range(0, count)
            .Select(i => new HistoricalImage(
                Id: $"img{i}", PromptId: $"prompt{i}", Year: 1975 + i * 10,
                FilePath: $"fake{i}.png", Provider: "test", CreatedAt: "2025-01-01T00:00:00Z"))
            .ToList();

    // Captures the image list at the last hop before ffmpeg, instead of shelling
    // out — the order is the whole assertion and a render would say nothing
    // extra. Standing in for the provider rather than the service keeps every
    // layer that could re-sort inside the test.
    private sealed class RecordingFfmpegProvider : IFfmpegProvider
    {
        public IReadOnlyList<HistoricalImage>? Received { get; private set; }

        public Task<Video?> ComposeAsync(IReadOnlyList<HistoricalImage> images, string outputPath, bool loopTail) =>
            ComposeAsync(images, outputPath);

        public Task<Video?> ComposeAsync(IReadOnlyList<HistoricalImage> images, string outputPath)
        {
            Received = images;
            return Task.FromResult<Video?>(new Video(
                Id: "recorded", ImageIds: images.Select(i => i.Id).ToList(),
                FilePath: outputPath, CreatedAt: "2026-01-01T00:00:00Z"));
        }

        // The music side of the interface is exercised by --smoke-publish
        // against real ffmpeg; this fake only records the composition order.
        public Task<double> ProbeDurationAsync(string path) => Task.FromResult(16.0);
        public Task<IReadOnlyDictionary<string, string>> ProbeTagsAsync(string path) =>
            Task.FromResult<IReadOnlyDictionary<string, string>>(new Dictionary<string, string>());
        public Task MuxMusicAsync(string videoPath, string trackPath, double startSeconds, string outputPath) =>
            Task.CompletedTask;
    }

    private static IEnumerable<(string Id, string Desc)> SkippedChecks() => new[]
    {
        ("O1", "stamped/{year}.png exists for every test year, same dimensions as source"),
        ("O2", "stamped output file size differs from the un-stamped source"),
        ("O6", "The whole assembly chain — runner, VideoService, provider — hands ffmpeg the years in the order the caller passed them"),
        ("V1", "Video file exists and has non-zero size"),
        ("V2", $"Video resolution == {ExpectedVideoWidth}x{ExpectedVideoHeight}"),
        ("V3", $"Duration is {ExpectedDurationSeconds(Years.Length)}s ± 0.5s " +
               $"(fixed target; per-frame hold computed dynamically for {Years.Length} frames)"),
        ("V4", "codec_name == h264, pix_fmt == yuv420p"),
        ("V6", "ffmpeg command used filter_complex xfade with a radial transition (not concat)"),
        ("O3", $"Partial year list [{string.Join(", ", PartialYears)}] only waits for/stamps those years and produces a {PartialYears.Length}-frame video"),
        ("O4", $"Overlap guard fires exactly per PlanTimeline: never for n={PartialYears.Length}, and for n={Years.Length} iff the plan was adjusted")
    };

    private static void PrintSummary(List<(string Id, string Desc, bool Pass, string Detail)> findings)
    {
        int passed = findings.Count(f => f.Pass);
        int total  = findings.Count;
        Console.WriteLine();
        Console.WriteLine($"Video smoke test: {passed}/{total} checks passed" +
                          (passed == total ? "" : " — FAILURES DETECTED"));
        Console.WriteLine("See output/smoke-video/report.md for full details.");
    }

    // ── Test image generation ────────────────────────────────────────────────

    private static async Task GenerateTestImageAsync(int year, string outputPath, ILogger logger)
    {
        var color = Colors[year];
        var drawtextArgs =
            $"-y -f lavfi -i \"color=c={color}:s={ImageWidth}x{ImageHeight}\" " +
            $"-vf \"drawtext=text='{year}':fontcolor=white:fontsize=300:" +
            "x=(w-text_w)/2:y=(h-text_h)/2\" " +
            $"-frames:v 1 \"{outputPath}\"";

        var (exitCode, stderr, _) = await RunProcessAsync("ffmpeg", drawtextArgs);
        if (exitCode == 0 && File.Exists(outputPath))
            return;

        logger.LogWarning("[SmokeVideo] {Year}: drawtext unavailable ({Reason}), falling back to plain color frame",
            year, stderr.Split('\n').LastOrDefault(l => l.Length > 0) ?? "unknown error");

        var plainArgs = $"-y -f lavfi -i \"color=c={color}:s={ImageWidth}x{ImageHeight}\" -frames:v 1 \"{outputPath}\"";
        var (plainExit, plainStderr, _) = await RunProcessAsync("ffmpeg", plainArgs);
        if (plainExit != 0 || !File.Exists(outputPath))
            throw new InvalidOperationException($"Failed to generate test image for {year}: {plainStderr}");
    }

    // ── ffprobe validation ───────────────────────────────────────────────────

    private readonly record struct ProbeResult(int Width, int Height, string CodecName, string PixFmt, double Duration);

    private static async Task<ProbeResult> FfprobeAsync(string path)
    {
        var args = "-v error -select_streams v:0 " +
                   "-show_entries stream=width,height,codec_name,pix_fmt:format=duration " +
                   $"-of json \"{path}\"";
        var (exitCode, stderr, stdout) = await RunProcessAsync("ffprobe", args);
        if (exitCode != 0)
            throw new InvalidOperationException($"ffprobe exited with code {exitCode}: {stderr}");

        using var doc = JsonDocument.Parse(stdout);
        var stream = doc.RootElement.GetProperty("streams")[0];
        var format = doc.RootElement.GetProperty("format");

        return new ProbeResult(
            Width:     stream.GetProperty("width").GetInt32(),
            Height:    stream.GetProperty("height").GetInt32(),
            CodecName: stream.GetProperty("codec_name").GetString() ?? "",
            PixFmt:    stream.GetProperty("pix_fmt").GetString() ?? "",
            Duration:  double.Parse(format.GetProperty("duration").GetString() ?? "0"));
    }

    private static async Task<(int Width, int Height)> FfprobeDimensionsAsync(string path)
    {
        var args = $"-v error -select_streams v:0 -show_entries stream=width,height -of json \"{path}\"";
        var (exitCode, stderr, stdout) = await RunProcessAsync("ffprobe", args);
        if (exitCode != 0)
            throw new InvalidOperationException($"ffprobe exited with code {exitCode}: {stderr}");

        using var doc = JsonDocument.Parse(stdout);
        var stream = doc.RootElement.GetProperty("streams")[0];
        return (stream.GetProperty("width").GetInt32(), stream.GetProperty("height").GetInt32());
    }

    // ── Process helpers ──────────────────────────────────────────────────────

    private static async Task<bool> BinaryAvailable(string exe, ILogger logger)
    {
        try
        {
            var (exitCode, _, _) = await RunProcessAsync(exe, "-version");
            return exitCode == 0;
        }
        catch (Win32Exception ex)
        {
            logger.LogError("[SmokeVideo] '{Exe}' not found in PATH: {Reason}", exe, ex.Message);
            return false;
        }
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

    // ── Report ────────────────────────────────────────────────────────────────

    private static async Task WriteReport(
        List<(string Id, string Desc, bool Pass, string Detail)> findings,
        ILogger logger)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Video Smoke Test Report");
        sb.AppendLine();
        sb.AppendLine($"Generated: {DateTimeOffset.UtcNow:o}");
        sb.AppendLine();
        sb.AppendLine("## Check Results");
        sb.AppendLine();
        sb.AppendLine("| Check | Description | Status | Detail |");
        sb.AppendLine("|-------|-------------|--------|--------|");
        foreach (var (id, desc, pass, detail) in findings)
        {
            var status = pass ? "✅ PASS" : "❌ FAIL";
            var safeDetail = detail.Replace("|", "\\|");
            sb.AppendLine($"| {id} | {desc} | {status} | {safeDetail} |");
        }
        sb.AppendLine();

        var outDir = Path.Combine("output", "smoke-video");
        Directory.CreateDirectory(outDir);
        await File.WriteAllTextAsync(Path.Combine(outDir, "report.md"), sb.ToString());

        logger.LogInformation("[SmokeVideo] Check summary:");
        foreach (var (id, _, pass, detail) in findings)
            logger.LogInformation("[SmokeVideo]   {Id} {Status}: {Detail}",
                id, pass ? "PASS" : "FAIL", detail);
    }
}
