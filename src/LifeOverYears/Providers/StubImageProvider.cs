using System.Text.Json;
using LifeOverYears.Services.Interfaces;
using Microsoft.Extensions.Logging;

namespace LifeOverYears.Providers;

// TODO: replace with OpenAiImageProvider — stub simulates API latency and the
// submit/collect job model as two real phases (job state + staged result on
// disk), so the run-folder contract and the collect CLI can be flow-tested
// for free.
public sealed class StubImageProvider : IImageGenerationProvider
{
    private static readonly JsonSerializerOptions JobJson =
        new(JsonSerializerDefaults.Web) { WriteIndented = true };

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

    public async Task SubmitEraAsync(string basePath, string prompt, int year, string jobsDir)
    {
        var delay = Random.Shared.Next(200, 801);
        _logger.LogInformation("[Stub] {Year}: simulating submit for {Delay}ms (prompt {Length} chars)",
            year, delay, prompt.Length);
        await Task.Delay(delay);

        Directory.CreateDirectory(jobsDir);
        // The "provider result" a later collect will download.
        File.Copy(basePath, Path.Combine(jobsDir, $"{year}.staged.png"), overwrite: true);

        var job = new
        {
            year,
            provider    = "stub",
            jobId       = $"stub-{year}",
            submittedAt = DateTimeOffset.UtcNow.ToString("o")
        };
        await File.WriteAllTextAsync(
            Path.Combine(jobsDir, $"{year}.json"),
            JsonSerializer.Serialize(job, JobJson));
        _logger.LogInformation("[Stub] {Year} submitted: jobId=stub-{Year}", year, year);
    }

    public Task<bool> TryCollectAsync(string jobsDir, int year, string outputPath)
    {
        var jobPath = Path.Combine(jobsDir, $"{year}.json");
        if (!File.Exists(jobPath))
            throw new InvalidOperationException(
                $"No job state for year {year} in {jobsDir} — was this year ever submitted?");

        var stagedPath = Path.Combine(jobsDir, $"{year}.staged.png");
        if (File.Exists(stagedPath))
        {
            File.Move(stagedPath, outputPath, overwrite: true);
            _logger.LogInformation("[Stub] {Year} collected: {Output}", year, outputPath);
            return Task.FromResult(true);
        }

        // Job exists but staged result is gone: a previous collect already moved
        // it. Idempotent resume when the output is in place; otherwise the job
        // state is corrupt.
        if (File.Exists(outputPath))
            return Task.FromResult(true);

        throw new InvalidOperationException(
            $"Job stub-{year} has no staged result and no collected output — job state in {jobsDir} is corrupt");
    }
}
