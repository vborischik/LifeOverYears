using LifeOverYears.Models;
using LifeOverYears.Services.Interfaces;
using Microsoft.Extensions.Logging;

namespace LifeOverYears.Services;

public sealed class Pipeline
{
    private readonly IVisionService _vision;
    private readonly IPromptService _prompt;
    private readonly IDataService _data;
    private readonly IRunService _runService;
    private readonly IImageGenerationProvider _images;
    private readonly IYearOverlayService _overlay;
    private readonly IVideoService _video;
    private readonly ILogger<Pipeline> _logger;

    public Pipeline(
        IVisionService vision,
        IPromptService prompt,
        IDataService data,
        IRunService runService,
        IImageGenerationProvider images,
        IYearOverlayService overlay,
        IVideoService video,
        ILogger<Pipeline> logger)
    {
        _vision = vision;
        _prompt = prompt;
        _data = data;
        _runService = runService;
        _images = images;
        _overlay = overlay;
        _video = video;
        _logger = logger;
    }

    public async Task<int> RunAsync(string photoPath, IReadOnlyList<int> years)
    {
        _logger.LogInformation("Pipeline started for: {PhotoPath}, years: {Years}",
            photoPath, string.Join(", ", years));

        // Step 1 — SceneDna (once for all years)
        var sceneDna = await _vision.AnalyzeAsync(photoPath);
        _logger.LogInformation("Step 1 complete — SceneDna: id={Id} terrain={Terrain} buildings={Buildings}",
            sceneDna.Id,
            sceneDna.Environment.Terrain,
            sceneDna.Geometry.Buildings.Count);

        var run = await _runService.CreateRunAsync(sceneDna.Id, photoPath);

        // Step 2 — Prompt per year (one GenerationContext shared across all years:
        // guarantees no car model repeats between the images of the same scene)
        var context = new GenerationContext { Random = new Random() };
        var prompts = new Dictionary<int, Prompt>();
        foreach (var year in years)
        {
            var eraProfile = await _data.LoadEraProfileAsync(year);
            var prompt = await _prompt.BuildAsync(sceneDna, eraProfile, context);
            prompts[year] = prompt;
            await _data.SavePromptAsync(prompt);
            await File.WriteAllTextAsync(Path.Combine(run.PromptsDir, $"{year}.txt"), prompt.Text);
            _logger.LogInformation("Step 2 — prompt built: year={Year} id={Id} length={Length}",
                year, prompt.Id, prompt.Text.Length);
        }

        // Step 3a — clean base: source photo emptied of people and vehicles
        var baseCleanPrompt = await _data.LoadPromptAsync("base-clean");
        var baseCleanPath   = Path.Combine(run.Root, "base_clean.png");
        await _images.CleanBaseAsync(run.SourcePath, baseCleanPrompt, baseCleanPath);
        _logger.LogInformation("Step 3a complete — clean base: {Path}", baseCleanPath);

        // Step 3b — era images, generated concurrently; completion is observed
        // by the watcher via the folder contract, not by task order.
        var generation = Task.WhenAll(years.Select(year =>
            _images.GenerateEraAsync(
                baseCleanPath, prompts[year].Text, year,
                Path.Combine(run.ImagesDir, $"{year}.png"))));

        // Step 3c — overlay years onto the stamped set, then assemble the video.
        // years here is exactly the caller's requested list, threaded through
        // unchanged from the watcher to the overlay to the composed video.
        var (missing, video) = await VideoAssemblyRunner.RunAsync(
            _overlay, _video, run.ImagesDir, run.StampedDir,
            Path.Combine(run.VideoDir, "timeline.mp4"), years, generation, _logger);
        if (missing.Count > 0)
            return 1;
        _logger.LogInformation("Step 3b complete — all {Count} era images present", years.Count);

        if (video is null)
            _logger.LogWarning("Pipeline finished without video (assembly skipped): {Root}", run.Root);
        else
            _logger.LogInformation("Pipeline complete — video: {Path}", video.FilePath);

        return 0;
    }
}
