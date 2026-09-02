using System.Text.Json;
using LifeOverYears.Models;
using LifeOverYears.Services.Interfaces;
using Microsoft.Extensions.Logging;

namespace LifeOverYears.Services;

public sealed class RunService : IRunService
{
    private readonly string _outputDir;
    private readonly ILogger<RunService> _logger;

    public RunService(string outputDir, ILogger<RunService> logger)
    {
        _outputDir = outputDir;
        _logger = logger;
    }

    private static readonly JsonSerializerOptions ManifestJson =
        new(JsonSerializerDefaults.Web) { WriteIndented = true };

    // The folder shape, shared by both entry points. Everything downstream —
    // the provider's jobs dir, the overlay, video assembly, collect — addresses
    // a run through these five subfolders, so a brand run that built its own
    // would be a second definition of the same thing, free to drift.
    private (string Root, string Prompts, string Images, string Stamped, string Video, string Jobs)
        CreateFolders(string runId)
    {
        // OutputDir is a single config string (default "output/runs") — split
        // on '/' so a configured value combines correctly on any platform.
        var segments = _outputDir.Split('/').ToList();
        segments.Add($"{runId}_{DateTimeOffset.Now:yyyyMMdd-HHmm}");
        var root = Path.Combine(segments.ToArray());

        var prompts = Path.Combine(root, "prompts");
        var images  = Path.Combine(root, "images");
        var stamped = Path.Combine(root, "stamped");
        var video   = Path.Combine(root, "video");
        var jobs    = Path.Combine(root, "jobs");

        Directory.CreateDirectory(prompts);
        Directory.CreateDirectory(images);
        Directory.CreateDirectory(stamped);
        Directory.CreateDirectory(video);
        Directory.CreateDirectory(jobs);

        RunLogProvider.Attach(root);
        return (root, prompts, images, stamped, video, jobs);
    }

    public async Task<RunFolder> CreateRunAsync(SceneDna sceneDna, string sourcePhotoPath, IReadOnlyList<int> years)
    {
        var (root, prompts, images, stamped, video, jobs) = CreateFolders(sceneDna.Id);

        var sourcePath = Path.Combine(root, "source.png");
        await using (var src = File.OpenRead(sourcePhotoPath))
        await using (var dst = File.Create(sourcePath))
            await src.CopyToAsync(dst);

        // Manifest lets a later 'collect' invocation recover the run's years
        // without re-parsing CLI arguments.
        var manifest = new RunManifest(sceneDna.Id, sourcePhotoPath, years, DateTimeOffset.UtcNow.ToString("o"));
        await File.WriteAllTextAsync(
            Path.Combine(root, "run.json"),
            JsonSerializer.Serialize(manifest, ManifestJson));

        // The run folder keeps its own copy of the vision output, independent
        // of data/scenes/ — a run is fully self-contained even if that scene
        // file is later moved, overwritten, or cleaned up.
        await File.WriteAllTextAsync(
            Path.Combine(root, "scene.json"),
            JsonSerializer.Serialize(sceneDna, ManifestJson));

        _logger.LogInformation("Run folder created: {Root} (source: {Source})", root, sourcePhotoPath);
        return new RunFolder(root, prompts, images, stamped, video, jobs, sourcePath);
    }

    public async Task<RunFolder> CreateBrandRunAsync(BrandSeries series, IReadOnlyList<int> years)
    {
        var runId = BrandSeriesPromptService.SeriesId(series);
        var (root, prompts, images, stamped, video, jobs) = CreateFolders(runId);

        // The series file is this run's source, in the sense the photograph is
        // for a photo run: copied in so the folder still explains itself after
        // the file in data/ has been edited for the next brand.
        var sourcePath = Path.Combine(root, "series.json");
        await File.WriteAllTextAsync(sourcePath, JsonSerializer.Serialize(series, ManifestJson));

        var manifest = new RunManifest(runId, sourcePath, years, DateTimeOffset.UtcNow.ToString("o"));
        await File.WriteAllTextAsync(
            Path.Combine(root, "run.json"),
            JsonSerializer.Serialize(manifest, ManifestJson));

        // A brand run has no Vision output, but the caption tail reads
        // scene.json and there is no reason for it to know that. A stand-in with
        // the series' own scene type is enough: nothing downstream reads the
        // geometry, and an unmapped scene type falls back to the base caption
        // pool exactly as it is designed to.
        var standIn = new SceneDna(
            Id:        runId,
            CreatedAt: DateTimeOffset.UtcNow.ToString("o"),
            SceneType: series.SceneType,
            Camera:    new Camera("eye-level", "storefront-facing", 75),
            Geometry:  new Geometry(
                Roads:     Array.Empty<Road>(),
                Sidewalks: true,
                Curbs:     true,
                Buildings: Array.Empty<Building>(),
                Driveways: Array.Empty<string>(),
                Parking:   "open asphalt lot in front of the entrance"),
            Environment: new Models.Environment(
                Terrain:   "suburban",
                Utilities: Array.Empty<string>(),
                Trees:     Array.Empty<Tree>(),
                Landscape: Array.Empty<string>()),
            ImmutableElements: new[] { series.StoreDescription });
        await File.WriteAllTextAsync(
            Path.Combine(root, "scene.json"),
            JsonSerializer.Serialize(standIn, ManifestJson));

        _logger.LogInformation("Brand run folder created: {Root} (series: {Brand})", root, series.Brand);
        return new RunFolder(root, prompts, images, stamped, video, jobs, sourcePath);
    }
}
