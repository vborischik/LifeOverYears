using System.Diagnostics;
using LifeOverYears.Services.Interfaces;
using Microsoft.Extensions.Logging;

namespace LifeOverYears.Providers;

// TODO: replace with OpenAiImageProvider — stub simulates API latency and
// out-of-order completion so the run-folder contract, completion watcher,
// and ffmpeg assembly can be flow-tested for free.
public sealed class StubImageProvider : IImageGenerationProvider
{
    private readonly ILogger<StubImageProvider> _logger;

    public StubImageProvider(ILogger<StubImageProvider> logger)
    {
        _logger = logger;
    }

    public async Task CleanBaseAsync(string sourcePath, string prompt, string outputPath)
    {
        var delay = Random.Shared.Next(1000, 2001);
        _logger.LogInformation("[Stub] CleanBase: simulating API for {Delay}ms (prompt {Length} chars)",
            delay, prompt.Length);
        await Task.Delay(delay);

        File.Copy(sourcePath, outputPath, overwrite: true);
        _logger.LogInformation("[Stub] CleanBase complete: {Output}", outputPath);
    }

    public async Task GenerateEraAsync(string basePath, string prompt, int year, string outputPath)
    {
        var delay = Random.Shared.Next(2000, 8001);
        _logger.LogInformation("[Stub] {Year}: simulating API for {Delay}ms (prompt {Length} chars)",
            year, delay, prompt.Length);
        await Task.Delay(delay);

        if (!await TryStampYearAsync(basePath, year, outputPath))
            File.Copy(basePath, outputPath, overwrite: true);

        _logger.LogInformation("[Stub] {Year} complete: {Output}", year, outputPath);
    }

    // Best-effort year stamp via ffmpeg drawtext so assembled video frames are
    // visually distinguishable; falls back to a plain copy when unavailable.
    private async Task<bool> TryStampYearAsync(string basePath, int year, string outputPath)
    {
        try
        {
            var filter = $"drawtext=text='{year}':fontcolor=white:fontsize=h/8:" +
                         "box=1:boxcolor=black@0.6:boxborderw=24:" +
                         "x=(w-text_w)/2:y=h-text_h-h/10";
            var psi = new ProcessStartInfo("ffmpeg",
                $"-y -i \"{basePath}\" -vf \"{filter}\" \"{outputPath}\"")
            {
                RedirectStandardError  = true,
                RedirectStandardOutput = true,
                UseShellExecute        = false,
                CreateNoWindow         = true
            };

            using var process = Process.Start(psi);
            if (process is null) return false;
            await process.WaitForExitAsync();
            return process.ExitCode == 0 && File.Exists(outputPath);
        }
        catch (Exception ex)
        {
            _logger.LogDebug("[Stub] year stamp unavailable ({Reason}), copying instead", ex.Message);
            return false;
        }
    }
}
