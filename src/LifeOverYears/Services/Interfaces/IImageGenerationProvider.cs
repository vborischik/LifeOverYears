namespace LifeOverYears.Services.Interfaces;

// Submit/collect job model for batch-style image providers: SubmitEraAsync
// enqueues generation and persists job state to {jobsDir}/{year}.json
// ({ "year", "provider", "jobId", "submittedAt" }); TryCollectAsync is called
// later — possibly by a separate process — to fetch finished results.
public interface IImageGenerationProvider
{
    Task CleanBaseAsync(string sourcePath, string prompt, string outputPath);

    Task SubmitEraAsync(string basePath, string prompt, int year, string jobsDir);

    // True: result was ready and has been downloaded to outputPath.
    // False: job still pending. Throws on failed jobs with the provider error.
    Task<bool> TryCollectAsync(string jobsDir, int year, string outputPath);
}
