using System.Linq;
using System.Text.Json;
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
    private readonly ICaptionService _caption;
    private readonly string _baseMode;
    private readonly bool _eraChaining;
    private readonly ILogger<Pipeline> _logger;

    public Pipeline(
        IVisionService vision,
        IPromptService prompt,
        IDataService data,
        IRunService runService,
        IImageGenerationProvider images,
        IYearOverlayService overlay,
        IVideoService video,
        ICaptionService caption,
        string baseMode,
        bool eraChaining,
        ILogger<Pipeline> logger)
    {
        _vision = vision;
        _prompt = prompt;
        _data = data;
        _runService = runService;
        _images = images;
        _overlay = overlay;
        _video = video;
        _caption = caption;
        _baseMode = baseMode;
        _eraChaining = eraChaining;
        _logger = logger;
    }

    public async Task<int> RunAsync(string photoPath, IReadOnlyList<int> years)
    {
        _logger.LogInformation("Pipeline started for: {PhotoPath}, years: {Years}",
            photoPath, string.Join(", ", years));

        // Step 1 — SceneDna (once for all years)
        var sceneDna = await _vision.AnalyzeAsync(photoPath);
        _logger.LogInformation(
            "Step 1 complete — SceneDna: id={Id} sceneType={SceneType} buildings={Buildings} roads={Roads} terrain={Terrain}",
            sceneDna.Id,
            sceneDna.SceneType,
            sceneDna.Geometry.Buildings.Count,
            sceneDna.Geometry.Roads.Count,
            sceneDna.Environment.Terrain);

        var run = await _runService.CreateRunAsync(sceneDna, photoPath, years);

        // Full vision output, verbatim — one scene per run, so verbosity here
        // is exactly the point: run.log ends up with the complete SceneDna a
        // human can audit without re-running vision.
        _logger.LogDebug("SceneDna (full): {SceneDna}", JsonSerializer.Serialize(sceneDna));

        // Step 2 — Prompt per year (one GenerationContext shared across all years:
        // guarantees no car model repeats between the images of the same scene)
        var context = new GenerationContext
        {
            Random = new Random(), TotalEras = years.Count, Years = years,
            ChainedFromPreviousEra = _eraChaining
        };
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

        // Step 3a — the base every era image is generated from. Either the
        // source photo emptied of people and vehicles ("clean"), or the scene
        // rebuilt from SceneDna text with the photo never sent at all
        // ("synthetic"). Everything downstream consumes the same single path.
        string basePath;
        if (string.Equals(_baseMode, "synthetic", StringComparison.OrdinalIgnoreCase))
        {
            var basePrompt = await _prompt.BuildBaseAsync(sceneDna, years.Min());
            await File.WriteAllTextAsync(Path.Combine(run.PromptsDir, "base_synthetic.txt"), basePrompt);
            basePath = Path.Combine(run.Root, "base_synthetic.png");
            await _images.SynthesizeBaseAsync(basePrompt, basePath);
            _logger.LogInformation("Step 3a complete — synthetic base: {Path}", basePath);
        }
        else
        {
            var baseCleanPrompt = await _data.LoadPromptAsync("base-clean");
            await File.WriteAllTextAsync(Path.Combine(run.PromptsDir, "base_clean.txt"), baseCleanPrompt);
            basePath = Path.Combine(run.Root, "base_clean.png");
            await _images.CleanBaseAsync(run.SourcePath, baseCleanPrompt, basePath);
            _logger.LogInformation("Step 3a complete — clean base: {Path}", basePath);
        }

        // Step 3b — chained mode: each era is generated on top of the previous
        // era's finished image instead of the shared base, so the place carries
        // forward and stays recognisable across the run. Necessarily sequential:
        // year N+1 cannot be submitted until year N exists.
        if (_eraChaining)
        {
            string chainBase = basePath;
            foreach (var year in years)
            {
                // "{year}.png" or a hand-corrected "{year}-clean.png" — either
                // counts as the year being done, and the chain continues from
                // whichever is there.
                var existing = VideoAssemblyRunner.FindEraImage(run.ImagesDir, year);
                if (existing is not null)
                {
                    _logger.LogInformation("{Year} already present ({File}) — chaining from it",
                        year, Path.GetFileName(existing));
                    chainBase = existing;
                    continue;
                }

                await _images.SubmitEraAsync(chainBase, prompts[year].Text, year, run.JobsDir);
                _logger.LogInformation("Submitted {Year} (chained from {Base})",
                    year, Path.GetFileName(chainBase));

                // Same escape hatch as the parallel path: the year is done the
                // moment the file exists, whoever put it there.
                var writeTarget = Path.Combine(run.ImagesDir, $"{year}.png");
                while ((existing = VideoAssemblyRunner.FindEraImage(run.ImagesDir, year)) is null)
                {
                    if (await _images.TryCollectAsync(run.JobsDir, year, writeTarget))
                        break;
                    _logger.LogInformation("Waiting for {Year} before the next era can start", year);
                    await Task.Delay(TimeSpan.FromSeconds(60));
                }

                chainBase = VideoAssemblyRunner.FindEraImage(run.ImagesDir, year) ?? writeTarget;
            }

            _logger.LogInformation("Step 3 complete — all {Count} era images present (chained)", years.Count);
        }
        else
        {
            // Submit one generation job per year; job state lives in run.JobsDir.
            foreach (var year in years)
            {
                await _images.SubmitEraAsync(basePath, prompts[year].Text, year, run.JobsDir);
                _logger.LogInformation("Submitted {Year}", year);
            }

            // Step 3c — unbounded wait on the run-folder files. A year is done the
            // moment images/{year}.png exists, regardless of who put it there: the
            // provider's collect call or a human dropping the file in. A throwing
            // provider call aborts the run with its error.
            _logger.LogInformation(
                "Waiting for era images in {ImagesDir} — drop files manually or let the provider deliver; checking every 60s",
                run.ImagesDir);
            List<int>? lastLogged = null;
            while (true)
            {
                var missing = new List<int>();
                foreach (var year in years)
                {
                    // A hand-corrected "{year}-clean.png" counts as the year
                    // being delivered, exactly like "{year}.png".
                    if (VideoAssemblyRunner.FindEraImage(run.ImagesDir, year) is not null)
                        continue;
                    var imagePath = Path.Combine(run.ImagesDir, $"{year}.png");
                    if (!await _images.TryCollectAsync(run.JobsDir, year, imagePath))
                        missing.Add(year);
                }

                if (missing.Count == 0)
                    break;

                if (lastLogged is null || !missing.SequenceEqual(lastLogged))
                {
                    _logger.LogInformation("Waiting for images: {Count} missing ({Years})",
                        missing.Count, string.Join(", ", missing));
                    lastLogged = missing;
                }
                await Task.Delay(TimeSpan.FromSeconds(60));
            }
            _logger.LogInformation("Step 3 complete — all {Count} era images present", years.Count);
        }

        // Step 4 — stamp + assemble, the same tail 'collect' and 'assemble' use.
        var (_, video) = await VideoAssemblyRunner.RunAsync(
            _overlay, _video, run.ImagesDir, run.StampedDir,
            Path.Combine(run.VideoDir, "timeline.mp4"), years, _logger);

        if (video is null)
        {
            _logger.LogWarning("Pipeline finished without video (assembly skipped): {Root}", run.Root);
            return 1;
        }
        _logger.LogInformation("Step 4 complete — video: {Path}", video.FilePath);

        // Step 5 — Caption. Best-effort: the images, stamped frames and video are
        // already on disk by this point, so a caption failure (a network blip, an
        // empty model response) must not discard a finished run. The caption is
        // the one artefact that can be regenerated on its own afterwards.
        var captionWritten = false;
        try
        {
            var narrative = new SceneNarrative(
                FirstYear:       years.Min(),
                LastYear:        years.Max(),
                FinalCondition:  context.SceneCondition,
                FirstBrand:      context.FirstBrand,
                LastBrand:       context.LastBrand,
                RebrandOccurred: context.RebrandOccurred);
            var caption = await _caption.GenerateAsync(sceneDna, narrative);
            var captionPath = Path.Combine(run.Root, "caption.txt");
            var captionText = caption.Description
                + "\n\n"
                + string.Join("\n", caption.Hashtags);
            await File.WriteAllTextAsync(captionPath, captionText);
            captionWritten = true;
            _logger.LogInformation("Step 5 complete — caption: {Path}", captionPath);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Step 5 failed — caption not written; the run is otherwise complete: {Root}", run.Root);
        }

        _logger.LogInformation("Pipeline complete — video: {Path}, caption.txt: {CaptionState}",
            video.FilePath, captionWritten ? "written" : "NOT written");

        return 0;
    }
}
