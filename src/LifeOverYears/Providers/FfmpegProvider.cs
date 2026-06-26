using System.Diagnostics;
using LifeOverYears.Models;
using LifeOverYears.Services.Interfaces;
using Microsoft.Extensions.Logging;

namespace LifeOverYears.Providers;

public sealed class FfmpegProvider : IFfmpegProvider
{
    private readonly ILogger<FfmpegProvider> _logger;
    private readonly string _ffmpegPath;
    private readonly string _outputDir;

    public FfmpegProvider(
        ILogger<FfmpegProvider> logger,
        string ffmpegPath = "ffmpeg",
        string outputDir = "output/videos")
    {
        _logger = logger;
        _ffmpegPath = ffmpegPath;
        _outputDir = outputDir;
    }

    public async Task<Video> ComposeAsync(IReadOnlyList<HistoricalImage> images)
    {
        _logger.LogInformation("Composing video from {Count} images", images.Count);

        Directory.CreateDirectory(_outputDir);

        var listFile = Path.GetTempFileName();
        var entries = images.Select(img => $"file '{img.FilePath}'\nduration 3");
        await File.WriteAllTextAsync(listFile, string.Join("\n", entries));

        var videoId = Guid.NewGuid().ToString();
        var outputPath = Path.Combine(_outputDir, $"{videoId}.mp4");

        var args = $"-f concat -safe 0 -i \"{listFile}\" " +
                   "-vf \"scale=1920:1080:force_original_aspect_ratio=decrease," +
                   "pad=1920:1080:(ow-iw)/2:(oh-ih)/2\" " +
                   $"-c:v libx264 -pix_fmt yuv420p -r 30 \"{outputPath}\"";

        await RunFfmpegAsync(args);
        File.Delete(listFile);

        _logger.LogInformation("Video composed: {Path}", outputPath);

        return new Video(
            Id:       videoId,
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

        await process.WaitForExitAsync();

        if (process.ExitCode != 0)
        {
            var error = await process.StandardError.ReadToEndAsync();
            throw new InvalidOperationException($"ffmpeg exited with code {process.ExitCode}: {error}");
        }
    }
}
