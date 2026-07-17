// TODO: remove smoke test
using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using LifeOverYears.Models;
using LifeOverYears.Services.Interfaces;
using Microsoft.Extensions.Logging;

namespace LifeOverYears.Services;

// TODO: remove smoke test
// Isolated ffmpeg smoke test: validates video assembly only. Generates its
// own test images via ffmpeg's lavfi color source (no vision, no prompts,
// no API keys), then exercises the real IVideoService/FfmpegProvider path.
public static class VideoSmokeTest
{
    private static readonly int[] Years = { 1975, 1985, 1995, 2005, 2015, 2025 };

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
    private const int HoldSeconds       = 3;
    private const int TransitionSeconds = 2;

    // n*HoldSeconds - (n-1)*TransitionSeconds — each xfade overlap eats into
    // the naive sum of per-frame holds. Must track FfmpegProvider's own math.
    private static int ExpectedDurationSeconds(int frameCount) =>
        frameCount * HoldSeconds - Math.Max(0, frameCount - 1) * TransitionSeconds;

    public static async Task<int> RunAsync(
        IVideoService videoService, ILogger logger, CapturingLoggerProvider logCapture)
    {
        logger.LogInformation("[SmokeVideo] VideoSmokeTest starting");

        var findings = new List<(string Id, string Desc, bool Pass, string Detail)>();
        var imagesDir  = Path.Combine("output", "smoke-video", "images");
        var videoDir   = Path.Combine("output", "smoke-video", "video");
        var outputPath = Path.Combine(videoDir, "timeline.mp4");
        Directory.CreateDirectory(imagesDir);
        Directory.CreateDirectory(videoDir);

        // V5 first — every other check depends on these binaries existing.
        var ffmpegOk  = await BinaryAvailable("ffmpeg", logger);
        var ffprobeOk = await BinaryAvailable("ffprobe", logger);
        if (!ffmpegOk || !ffprobeOk)
        {
            var missing = string.Join(", ", new[] { !ffmpegOk ? "ffmpeg" : null, !ffprobeOk ? "ffprobe" : null }
                .Where(m => m is not null));
            findings.Add(("V5", "ffmpeg and ffprobe binaries found in PATH",
                false, $"ffmpeg not found in PATH — missing: {missing}"));
            foreach (var (id, desc) in SkippedChecks())
                findings.Add((id, desc, false, "skipped — ffmpeg not found in PATH"));

            await WriteReport(findings, logger);
            PrintSummary(findings);
            return 1;
        }
        findings.Add(("V5", "ffmpeg and ffprobe binaries found in PATH",
            true, "Both binaries responded to -version"));

        // Generate 6 visually distinct test frames.
        foreach (var year in Years)
            await GenerateTestImageAsync(year, Path.Combine(imagesDir, $"{year}.png"), logger);

        var images = Years
            .Select(y => new HistoricalImage(
                Id:        Guid.NewGuid().ToString(),
                PromptId:  "smoke-video",
                Year:      y,
                FilePath:  Path.Combine(imagesDir, $"{y}.png"),
                Provider:  "smoke-video",
                CreatedAt: DateTimeOffset.UtcNow.ToString("o")))
            .OrderBy(i => i.Year)
            .ToList();

        Video? video = null;
        try
        {
            video = await videoService.ComposeAsync(images, outputPath);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[SmokeVideo] video composition threw");
        }

        var fileInfo = File.Exists(outputPath) ? new FileInfo(outputPath) : null;
        var v1 = video is not null && fileInfo is { Length: > 0 };
        findings.Add(("V1", "Video file exists and has non-zero size",
            v1, v1 ? $"{fileInfo!.Length} bytes at {outputPath}" : "file missing or empty (composition failed)"));

        if (!v1)
        {
            foreach (var (id, desc) in SkippedChecks().Where(c => c.Id != "V1"))
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
            $"Duration is {expectedDuration}s ± 0.5s ({Years.Length}×{HoldSeconds}s holds - {Years.Length - 1}×{TransitionSeconds}s xfade overlaps)",
            v3, $"actual: {probe.Duration:F2}s"));

        var v4 = probe.CodecName == "h264" && probe.PixFmt == "yuv420p";
        findings.Add(("V4", "codec_name == h264, pix_fmt == yuv420p",
            v4, $"actual: codec_name={probe.CodecName}, pix_fmt={probe.PixFmt}"));

        // V6: confirm the xfade radial-wipe path was actually taken, not a
        // hard-cut concat — detecting a wipe vs. a cut from pixels alone is
        // out of scope for a smoke test, so this asserts on the ffmpeg
        // command FfmpegProvider logged at Debug level.
        var commandLine = logCapture.Messages.FirstOrDefault(m =>
            m.Contains("ffmpeg command:", StringComparison.OrdinalIgnoreCase));
        var v6 = commandLine is not null
            && commandLine.Contains("xfade", StringComparison.OrdinalIgnoreCase)
            && commandLine.Contains("radial", StringComparison.OrdinalIgnoreCase);
        findings.Add(("V6", "ffmpeg command used filter_complex xfade with a radial transition (not concat)",
            v6, commandLine is null ? "no 'ffmpeg command:' log line captured" : commandLine));

        await WriteReport(findings, logger);
        PrintSummary(findings);

        return findings.All(f => f.Pass) ? 0 : 1;
    }

    private static IEnumerable<(string Id, string Desc)> SkippedChecks() => new[]
    {
        ("V1", "Video file exists and has non-zero size"),
        ("V2", $"Video resolution == {ExpectedVideoWidth}x{ExpectedVideoHeight}"),
        ("V3", $"Duration is {ExpectedDurationSeconds(Years.Length)}s ± 0.5s " +
               $"({Years.Length}×{HoldSeconds}s holds - {Years.Length - 1}×{TransitionSeconds}s xfade overlaps)"),
        ("V4", "codec_name == h264, pix_fmt == yuv420p"),
        ("V6", "ffmpeg command used filter_complex xfade with a radial transition (not concat)")
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
