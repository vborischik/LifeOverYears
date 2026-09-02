using LifeOverYears.Models;
using LifeOverYears.Services.Interfaces;
using Microsoft.Extensions.Logging;

namespace LifeOverYears.Services;

// The brand-series run, from a series file to a finished video. Deliberately a
// sibling of Pipeline rather than a branch inside it: the two share no step 1 at
// all — there is no photograph, no Vision call and no SceneDna — and everything
// they do share is already factored out (VideoAssemblyRunner for the video tail,
// CaptionRunner for the caption tail, the provider's submit/collect contract for
// the images). A flag through Pipeline would have bought reuse of the parts that
// are three lines long and coupled the parts that are not.
public sealed class BrandSeriesRunner
{
    private readonly IBrandSeriesPromptService _prompt;
    private readonly IDataService _data;
    private readonly IRunService _runService;
    private readonly IImageGenerationProvider _images;
    private readonly IYearOverlayService _overlay;
    private readonly IVideoService _video;
    private readonly ICaptionService _caption;
    private readonly bool _shortPrompts;
    private readonly ILogger<BrandSeriesRunner> _logger;

    public BrandSeriesRunner(
        IBrandSeriesPromptService prompt,
        IDataService data,
        IRunService runService,
        IImageGenerationProvider images,
        IYearOverlayService overlay,
        IVideoService video,
        ICaptionService caption,
        bool shortPrompts,
        ILogger<BrandSeriesRunner> logger)
    {
        _prompt = prompt;
        _data = data;
        _runService = runService;
        _images = images;
        _overlay = overlay;
        _video = video;
        _caption = caption;
        _shortPrompts = shortPrompts;
        _logger = logger;
    }

    public async Task<int> RunAsync(string seriesName, IReadOnlyList<int>? requestedYears)
    {
        var series = await _data.LoadBrandSeriesAsync(seriesName);

        // The series file owns the year list; a CLI argument narrows it, and a
        // year the file has no era for is a mistake worth stopping on rather
        // than quietly skipping — it would leave a gap in the finished video.
        var years = requestedYears is { Count: > 0 } ? requestedYears : series.Years;
        var unknown = years.Where(y => !series.Eras.ContainsKey(y.ToString())).ToList();
        if (unknown.Count > 0)
        {
            _logger.LogError("Brand series '{Name}' has no era for {Years} — years present: {Present}",
                seriesName, string.Join(", ", unknown), string.Join(", ", series.Eras.Keys));
            return 1;
        }

        _logger.LogInformation("Brand series run started: {Brand}, years: {Years}",
            series.Brand, string.Join(", ", years));

        var run = await _runService.CreateBrandRunAsync(series, years);

        // Step 1 — one prompt per era, one context across all of them, so the
        // vehicle classes cannot repeat between the frames of the same run.
        var context = new GenerationContext
        {
            Random = new Random(), TotalEras = years.Count, Years = years,
            // Never a choice in this mode: with no shared base, era N+1 has only
            // era N to edit.
            ChainedFromPreviousEra = true
        };
        // One read for the whole run: the pool is the same in every era, only
        // the year filter over it changes.
        var centerReplacements = await _data.LoadCenterReplacementsAsync();

        var prompts = new Dictionary<int, Prompt>();
        foreach (var year in years)
        {
            var prompt = _prompt.Build(series, year, context, centerReplacements);
            prompts[year] = prompt;
            await _data.SavePromptAsync(prompt);
            await File.WriteAllTextAsync(Path.Combine(run.PromptsDir, $"{year}.txt"), prompt.Text);

            if (_shortPrompts)
            {
                var shortDir = Path.Combine(run.Root, ShortPromptWriter.OutputDirName);
                Directory.CreateDirectory(shortDir);
                await File.WriteAllTextAsync(
                    Path.Combine(shortDir, $"{year}.txt"), ShortPromptWriter.Rewrite(prompt.Text));
            }
            _logger.LogInformation("Prompt built: year={Year} length={Length}", year, prompt.Text.Length);
        }

        // The caption tail runs off this whether it is reached now or by a later
        // 'collect', and the GenerationContext it comes from dies with this
        // process. A brand series never rebrands — the name is the point of it —
        // so the first and last brand are the same and RebrandOccurred is false.
        await CaptionRunner.SaveNarrativeAsync(run.Root, new SceneNarrative(
            FirstYear:       years.Min(),
            LastYear:        years.Max(),
            FinalCondition:  prompts[years.Max()].SceneCondition,
            FirstBrand:      series.Brand,
            LastBrand:       series.Brand,
            RebrandOccurred: false));

        // Step 2 — era images. The first one is drawn from text straight into
        // images/: there is no photograph and no separate empty-premises base,
        // so that frame IS the base, and it is the only prompt carrying the 9:16
        // line. Every era after it edits the one before, inheriting that canvas
        // and whatever the place already looks like.
        //
        // Always sequential, whatever Pipeline:EraChaining says: without a
        // shared base there is nothing for a fan-out to edit. That setting
        // belongs to the photo path, which has one.
        string? chainBase = null;
        foreach (var year in years)
        {
            if (VideoAssemblyRunner.FindEraImage(run.ImagesDir, year) is { } done)
            {
                _logger.LogInformation("{Year} already present ({File}) — continuing from it",
                    year, Path.GetFileName(done));
                chainBase = done;
                continue;
            }

            var target = Path.Combine(run.ImagesDir, $"{year}.png");
            if (chainBase is null)
            {
                // Text-to-image takes no input image, so this is the one era
                // whose logo reference cannot be sent. Its LOGO block still
                // states the letterforms in words, and the next era carries the
                // reference over the top of this frame.
                _logger.LogInformation("Generating {Year} from text — the run's first frame", year);
                await _images.SynthesizeBaseAsync(prompts[year].Text, target);
            }
            else
            {
                await _images.SubmitEraAsync(
                    chainBase, prompts[year].Text, year, run.JobsDir, LogoRefPath(series, year));
                _logger.LogInformation("Submitted {Year} (chained from {Base})",
                    year, Path.GetFileName(chainBase));
            }

            // Either way the year is done the moment its file exists, whoever
            // put it there — the provider, or a human dropping one in. Only a
            // submitted year has a job to collect; the first was written
            // synchronously or not at all.
            var submitted = chainBase is not null;
            while (VideoAssemblyRunner.FindEraImage(run.ImagesDir, year) is null)
            {
                if (submitted && await _images.TryCollectAsync(run.JobsDir, year, target))
                    break;
                _logger.LogInformation("Waiting for {Year} before the next era can start", year);
                await Task.Delay(TimeSpan.FromSeconds(60));
            }

            chainBase = VideoAssemblyRunner.FindEraImage(run.ImagesDir, year) ?? target;
        }

        _logger.LogInformation("All {Count} era images present", years.Count);

        // Step 4 — stamp + assemble, unchanged.
        var (_, video) = await VideoAssemblyRunner.RunAsync(
            _overlay, _video, run.ImagesDir, run.StampedDir,
            Path.Combine(run.VideoDir, "timeline.mp4"), years, _logger);

        if (video is null)
        {
            _logger.LogWarning("Brand run finished without video (assembly skipped): {Root}", run.Root);
            return 1;
        }

        // Step 5 — caption, unchanged and best-effort: the video is already on
        // disk, and the caption is the one artefact that can be redone alone.
        var scene = await CaptionRunner.ReadSceneAsync(run.Root);
        var narrative = await CaptionRunner.ReadNarrativeAsync(run.Root);
        var captionWritten = scene is not null && narrative is not null
            && await CaptionRunner.WriteAsync(_caption, scene, narrative, run.Root, _logger);

        _logger.LogInformation("Brand run complete — video: {Path}, caption.txt: {CaptionState}",
            video.FilePath, captionWritten ? "written" : "NOT written");
        return 0;
    }

    // Logo references are stored relative to data/, the way every other data
    // path in the project is written. Null once an era has no sign left.
    private static string? LogoRefPath(BrandSeries series, int year) =>
        series.Eras.TryGetValue(year.ToString(), out var era) && era.LogoRef is { } reference
            ? Path.Combine("data", reference.Replace('/', Path.DirectorySeparatorChar))
            : null;
}
