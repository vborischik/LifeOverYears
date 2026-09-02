namespace LifeOverYears.Services.Interfaces;

// Submit/collect job model for batch-style image providers: SubmitEraAsync
// enqueues generation and persists job state to {jobsDir}/{year}.json
// ({ "year", "provider", "jobId", "submittedAt" }); TryCollectAsync is called
// later — possibly by a separate process — to fetch finished results.
public interface IImageGenerationProvider
{
    // Text-to-image: builds the base from a SceneDna description with no
    // source photo, so no pixel of the input image survives into the run.
    Task SynthesizeBaseAsync(string prompt, string outputPath);

    Task CleanBaseAsync(string sourcePath, string prompt, string outputPath);

    // referenceImagePath is a second image sent alongside the base for the
    // model to copy something specific out of — a brand series sends the era's
    // logo sheet. Optional and defaulted so the photo-driven path, which has
    // nothing to reference, is unchanged.
    Task SubmitEraAsync(
        string basePath, string prompt, int year, string jobsDir,
        string? referenceImagePath = null);

    // True: result was ready and has been downloaded to outputPath.
    // False: job still pending. Throws on failed jobs with the provider error.
    Task<bool> TryCollectAsync(string jobsDir, int year, string outputPath);
}
