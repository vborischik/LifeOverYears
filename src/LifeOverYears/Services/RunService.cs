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

    public async Task<RunFolder> CreateRunAsync(string sceneId, string sourcePhotoPath)
    {
        var root = Path.Combine("output", "runs",
            $"{sceneId}_{DateTimeOffset.Now:yyyyMMdd-HHmm}");

        var prompts = Path.Combine(root, "prompts");
        var images  = Path.Combine(root, "images");
        var video   = Path.Combine(root, "video");

        Directory.CreateDirectory(prompts);
        Directory.CreateDirectory(images);
        Directory.CreateDirectory(video);

        var sourcePath = Path.Combine(root, "source.png");
        await using (var src = File.OpenRead(sourcePhotoPath))
        await using (var dst = File.Create(sourcePath))
            await src.CopyToAsync(dst);

        _logger.LogInformation("Run folder created: {Root} (source: {Source})", root, sourcePhotoPath);
        return new RunFolder(root, prompts, images, video, sourcePath);
    }
}
