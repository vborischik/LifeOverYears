using System.Text.Json;
using LifeOverYears.Models;
using LifeOverYears.Services.Interfaces;
using Microsoft.Extensions.Logging;

namespace LifeOverYears.Services;

public sealed class RunService : IRunService
{
    private readonly ILogger<RunService> _logger;

    public RunService(ILogger<RunService> logger)
    {
        _logger = logger;
    }

    private static readonly JsonSerializerOptions ManifestJson =
        new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public async Task<RunFolder> CreateRunAsync(string sceneId, string sourcePhotoPath, IReadOnlyList<int> years)
    {
        var root = Path.Combine("output", "runs",
            $"{sceneId}_{DateTimeOffset.Now:yyyyMMdd-HHmm}");

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

        var sourcePath = Path.Combine(root, "source.png");
        await using (var src = File.OpenRead(sourcePhotoPath))
        await using (var dst = File.Create(sourcePath))
            await src.CopyToAsync(dst);

        // Manifest lets a later 'collect' invocation recover the run's years
        // without re-parsing CLI arguments.
        var manifest = new RunManifest(sceneId, sourcePhotoPath, years, DateTimeOffset.UtcNow.ToString("o"));
        await File.WriteAllTextAsync(
            Path.Combine(root, "run.json"),
            JsonSerializer.Serialize(manifest, ManifestJson));

        _logger.LogInformation("Run folder created: {Root} (source: {Source})", root, sourcePhotoPath);
        return new RunFolder(root, prompts, images, stamped, video, jobs, sourcePath);
    }
}
