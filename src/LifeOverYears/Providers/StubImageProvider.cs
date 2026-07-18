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

        // Year stamping is YearOverlayService's job, applied by
        // VideoAssemblyRunner into stamped/ after the watcher completes.
        File.Copy(basePath, outputPath, overwrite: true);
        _logger.LogInformation("[Stub] {Year} complete: {Output}", year, outputPath);
    }
}
