using System.Diagnostics;
using LifeOverYears.Models;
using LifeOverYears.Services.Interfaces;
using Microsoft.Extensions.Logging;

namespace LifeOverYears.Providers;

public sealed class FfmpegProvider : IFfmpegProvider
{
    private const int SecondsPerFrame = 3;

    private readonly ILogger<FfmpegProvider> _logger;
    private readonly string _ffmpegPath;

    public FfmpegProvider(ILogger<FfmpegProvider> logger, string ffmpegPath = "ffmpeg")
    {
        _logger = logger;
        _ffmpegPath = ffmpegPath;
    }

    public async Task<Video?> ComposeAsync(IReadOnlyList<HistoricalImage> images, string outputPath)
    {
        _logger.LogInformation("Composing video from {Count} images", images.Count);

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath))!);

        // concat demuxer ignores the duration of the last entry, so the final
        // image is listed once more without a duration to hold its full 3s.
        var entries = images
            .Select(img => $"file '{Path.GetFullPath(img.FilePath)}'\nduration {SecondsPerFrame}")
            .Append($"file '{Path.GetFullPath(images[^1].FilePath)}'");

        var listFile = Path.GetTempFileName();
        await File.WriteAllTextAsync(listFile, string.Join("\n", entries));

        // 9:16 portrait: fit each frame into 1080x1920, pad the rest black.
        var args = $"-y -f concat -safe 0 -i \"{listFile}\" " +
                   "-vf \"scale=1080:1920:force_original_aspect_ratio=decrease," +
                   "pad=1080:1920:(ow-iw)/2:(oh-ih)/2\" " +
                   "-c:v libx264 -pix_fmt yuv420p -r 30 " +
                   // trim the tail the repeated last entry adds, so every frame holds exactly 3s
                   $"-t {images.Count * SecondsPerFrame} \"{outputPath}\"";

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
        finally
        {
            File.Delete(listFile);
        }

        _logger.LogInformation("Video composed: {Path}", outputPath);

        return new Video(
            Id:       Guid.NewGuid().ToString(),
            ImageIds: images.Select(i => i.Id).ToList(),
            FilePath: outputPath,
            CreatedAt: DateTimeOffset.UtcNow.ToString("o"));
    }

    private async Task RunFfmpegAsync(string arguments)
    {
        var psi = new ProcessStartInfo(_ffmpegPath, arguments)
        {
            RedirectStandardError  = true,
            RedirectStandardOutput = true,
            UseShellExecute        = false,
            CreateNoWindow         = true
        };

        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start ffmpeg");

        var stderr = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        if (process.ExitCode != 0)
            throw new InvalidOperationException(
                $"ffmpeg exited with code {process.ExitCode}: {await stderr}");
    }
}
