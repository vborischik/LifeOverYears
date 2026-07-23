using System.Collections.Concurrent;
using System.Text.Json;
using LifeOverYears.Services.Interfaces;
using Microsoft.Extensions.Logging;

namespace LifeOverYears.Providers;

// Real Step 3 provider. Implements the submit/collect contract with
// fire-and-forget in-process tasks: SubmitEraAsync kicks off the OpenAI call
// and returns immediately, so the Pipeline's sequential submit loop actually
// runs all eras in parallel. TryCollectAsync polls each task and writes the
// finished PNG into the run folder — matching the exact contract the wait-loop
// and the 'collect' CLI mode already rely on.
public sealed class OpenAiImageProvider : IImageGenerationProvider
{
    private const string Size = "1024x1536";   // 2:3 portrait, non-experimental
    private const string Quality = "medium";

    private static readonly JsonSerializerOptions JobJson =
        new(JsonSerializerDefaults.Web) { WriteIndented = true };

    private readonly IOpenAiProvider _openai;
    private readonly ILogger<OpenAiImageProvider> _logger;

    // Keyed by year: the in-flight (or finished) generation task.
    private readonly ConcurrentDictionary<int, Task<byte[]>> _jobs = new();

    public OpenAiImageProvider(IOpenAiProvider openai, ILogger<OpenAiImageProvider> logger)
    {
        _openai = openai;
        _logger = logger;
    }

    public async Task CleanBaseAsync(string sourcePath, string prompt, string outputPath)
    {
        var source = await File.ReadAllBytesAsync(sourcePath);
        _logger.LogInformation("CleanBase: editing {Source} ({Prompt} chars) via gpt-image-2",
            sourcePath, prompt.Length);
        var result = await _openai.EditImageAsync(source, prompt, Size, Quality);
        await File.WriteAllBytesAsync(outputPath, result);
        _logger.LogInformation("CleanBase complete: {Output}", outputPath);
    }

    public Task SubmitEraAsync(string basePath, string prompt, int year, string jobsDir)
    {
        Directory.CreateDirectory(jobsDir);

        // Fire-and-forget: start the generation now, record the task, return.
        var baseBytes = File.ReadAllBytes(basePath);
        var task = Task.Run(() => _openai.EditImageAsync(baseBytes, prompt, Size, Quality));
        _jobs[year] = task;

        var job = new
        {
            year,
            provider    = "openai-gpt-image-2",
            jobId       = $"openai-{year}",
            size        = Size,
            quality     = Quality,
            submittedAt = DateTimeOffset.UtcNow.ToString("o")
        };
        File.WriteAllText(
            Path.Combine(jobsDir, $"{year}.json"),
            JsonSerializer.Serialize(job, JobJson));

        _logger.LogInformation("Submitted {Year} to gpt-image-2 ({Quality}, {Size})",
            year, Quality, Size);
        return Task.CompletedTask;
    }

    public async Task<bool> TryCollectAsync(string jobsDir, int year, string outputPath)
    {
        var jobPath = Path.Combine(jobsDir, $"{year}.json");
        if (!File.Exists(jobPath))
            throw new InvalidOperationException(
                $"No job state for year {year} in {jobsDir} — was this year ever submitted?");

        // If this is a fresh process (e.g. the 'collect' CLI after a restart)
        // the in-memory task is gone. We can't recover an in-flight OpenAI
        // call, so treat it as not-yet-ready and let a re-run resubmit.
        if (!_jobs.TryGetValue(year, out var task))
        {
            _logger.LogWarning(
                "No in-process job for {Year} (new process?) — cannot collect; re-run to resubmit",
                year);
            return false;
        }

        if (!task.IsCompleted)
            return false;

        // Completed: propagate a failure as a throw (contract), else write PNG.
        var bytes = await task;   // throws here if the generation faulted
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath))!);
        await File.WriteAllBytesAsync(outputPath, bytes);
        _logger.LogInformation("Collected {Year} -> {Output}", year, outputPath);
        return true;
    }
}
