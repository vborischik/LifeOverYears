using System.Diagnostics;
using System.Text;
using LifeOverYears.Models;
using LifeOverYears.Services.Interfaces;
using Microsoft.Extensions.Logging;

namespace LifeOverYears.Providers;

public sealed class FfmpegProvider : IFfmpegProvider
{
    private const int HoldSeconds       = 3;
    private const int TransitionSeconds = 2;
    private const string TransitionType = "radial";

    private readonly ILogger<FfmpegProvider> _logger;
    private readonly string _ffmpegPath;

    public FfmpegProvider(ILogger<FfmpegProvider> logger, string ffmpegPath = "ffmpeg")
    {
        _logger = logger;
        _ffmpegPath = ffmpegPath;
    }

    public async Task<Video?> ComposeAsync(IReadOnlyList<HistoricalImage> images, string outputPath)
    {
        _logger.LogInformation("Composing video from {Count} images with {Transition} xfade transitions",
            images.Count, TransitionType);

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath))!);

        if (!await SupportsXfadeAsync())
        {
            _logger.LogError(
                "ffmpeg at '{Path}' does not report the 'xfade' filter (requires ffmpeg 4.3+) — " +
                "skipping video assembly rather than falling back to a hard-cut concat.", _ffmpegPath);
            return null;
        }

        var args = BuildArgs(images, outputPath);
        _logger.LogDebug("ffmpeg command: {Args}", args);

        // n*HoldSeconds - (n-1)*TransitionSeconds, per the xfade chaining math below.
        var expectedDuration = images.Count * HoldSeconds - Math.Max(0, images.Count - 1) * TransitionSeconds;
        _logger.LogInformation("Expected output duration: {Duration}s for {Count} frames", expectedDuration, images.Count);

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

    // Builds an xfade chain: each image is a HoldSeconds still-image clip,
    // normalized to 1080x1920/yuv420p/30fps, then chained pairwise with
    // radial-wipe crossfades. Offset for transition i (0-based, i=0..n-2)
    // is (i+1) * (HoldSeconds - TransitionSeconds) — the point, measured
    // from the start of the running merged stream, where the next clip's
    // hold has consumed everything except the upcoming overlap.
    private static string BuildArgs(IReadOnlyList<HistoricalImage> images, string outputPath)
    {
        var sb = new StringBuilder("-y ");
        foreach (var img in images)
            sb.Append($"-loop 1 -t {HoldSeconds} -i \"{Path.GetFullPath(img.FilePath)}\" ");

        var filter = new StringBuilder();
        for (var i = 0; i < images.Count; i++)
            filter.Append(
                $"[{i}:v]scale=1080:1920:force_original_aspect_ratio=decrease," +
                $"pad=1080:1920:(ow-iw)/2:(oh-ih)/2,setsar=1,fps=30,format=yuv420p[v{i}];");

        string outLabel;
        if (images.Count == 1)
        {
            outLabel = "v0";
        }
        else
        {
            var prevLabel = "v0";
            for (var i = 1; i < images.Count; i++)
            {
                var offset = i * (HoldSeconds - TransitionSeconds);
                var stageLabel = i == images.Count - 1 ? "outv" : $"x{i}";
                filter.Append(
                    $"[{prevLabel}][v{i}]xfade=transition={TransitionType}:duration={TransitionSeconds}:offset={offset}[{stageLabel}];");
                prevLabel = stageLabel;
            }
            outLabel = "outv";
        }

        // trim the trailing semicolon ffmpeg's filter parser doesn't need but tolerates; keep it explicit for clarity
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
            return false; // binary itself missing — reported by the caller's RunFfmpegAsync path
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
