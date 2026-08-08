using Microsoft.Extensions.Configuration;

namespace LifeOverYears;

// Working-folder paths for the pipeline, all relative to projectRoot. Bound
// from the "Pipeline" config section with defaults that match the app's
// pre-existing hardcoded behavior, so a run with no Pipeline folder keys set
// behaves exactly as before this was made configurable. Deliberately does
// not cover anything under data/ — prompts, eras, captions and brands are
// application resources, not working folders.
public sealed record PipelineFolders(string InputDir, string ProcessedDir, string FailedDir, string OutputDir)
{
    public static PipelineFolders Resolve(IConfiguration configuration) => new(
        configuration["Pipeline:InputDir"] ?? "testImage",
        configuration["Pipeline:ProcessedDir"] ?? "processed",
        configuration["Pipeline:FailedDir"] ?? "failed",
        configuration["Pipeline:OutputDir"] ?? "output/runs");
}
