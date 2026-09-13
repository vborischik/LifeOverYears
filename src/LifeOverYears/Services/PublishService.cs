using LifeOverYears.Models;
using LifeOverYears.Services.Interfaces;
using Microsoft.Extensions.Logging;

namespace LifeOverYears.Services;

// One run, every configured target. Order is fixed by what the platforms
// need: the ones that pull from a URL (Instagram, Facebook) cannot start
// until storage has produced one, so that step runs first and once. A
// target that fails does not stop the others — a Reel that is up is not
// undone by a YouTube quota error — but the run is recorded "failed" if any
// target failed, so the reviewer sees it.
public sealed class PublishService : IPublishService
{
    // The targets whose provider takes a URL rather than bytes.
    private static readonly HashSet<string> NeedsPublicUrl =
        new(StringComparer.OrdinalIgnoreCase) { "instagram", "facebook" };

    private readonly IReadOnlyDictionary<string, IPublishTarget> _targets;
    private readonly IPublicStorage? _storage;
    private readonly ILogger<PublishService> _logger;

    public IReadOnlyList<string> Targets { get; }

    // `targets` is the configured list, in order; each must have a provider
    // in `available` or the service refuses to be built — a misspelled
    // platform in config is not something to discover on the first publish.
    public PublishService(
        IReadOnlyList<string> targets,
        IReadOnlyList<IPublishTarget> available,
        IPublicStorage? storage,
        ILogger<PublishService> logger)
    {
        // Nothing to post to is a configuration error, not a no-op: a publish
        // that "succeeds" with zero publications is how a reviewer's yes
        // quietly does nothing.
        if (targets.Count == 0)
            throw new InvalidOperationException("Publish:Targets is empty — name at least one platform");

        var byName = available.ToDictionary(t => t.Platform, StringComparer.OrdinalIgnoreCase);
        var missing = targets.Where(t => !byName.ContainsKey(t)).ToList();
        if (missing.Count > 0)
            throw new InvalidOperationException(
                $"Publish:Targets names platforms with no provider: {string.Join(", ", missing)}. " +
                $"Known: {string.Join(", ", byName.Keys)}");
        if (targets.Any(t => NeedsPublicUrl.Contains(t)) && storage is null)
            throw new InvalidOperationException(
                "Publish:Targets includes a platform that pulls from a URL, but no storage (Dropbox) is configured");

        Targets  = targets;
        _targets = byName;
        _storage = storage;
        _logger  = logger;
    }

    public async Task<PublishState> PublishAsync(PublishRequest request, CancellationToken ct = default)
    {
        var publications = new List<Publication>();
        var errors       = new List<string>();

        if (Targets.Any(t => NeedsPublicUrl.Contains(t)) && request.PublicVideoUrl is null)
        {
            try
            {
                var url = await _storage!.UploadPublicAsync(
                    request.Video.FilePath, $"{request.Video.Id}.mp4", ct);
                request = request with { PublicVideoUrl = url };
            }
            catch (Exception ex)
            {
                // Nothing that needs the URL can run. The byte-taking targets
                // still can, so this is recorded and the loop continues.
                _logger.LogError(ex, "Storage upload failed; URL-based targets will be skipped");
                errors.Add($"storage: {ex.Message}");
            }
        }

        foreach (var name in Targets)
        {
            if (NeedsPublicUrl.Contains(name) && request.PublicVideoUrl is null)
            {
                errors.Add($"{name}: no public URL");
                continue;
            }
            try
            {
                var publication = await _targets[name].PublishAsync(request, ct);
                publications.Add(publication);
                _logger.LogInformation("Published to {Platform}: {Url}", name, publication.Url);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Publish to {Platform} failed", name);
                errors.Add($"{name}: {ex.Message}");
            }
        }

        return new PublishState(
            Status:       errors.Count == 0 ? "published" : "failed",
            DecidedAt:    DateTimeOffset.UtcNow.ToString("o"),
            Publications: publications,
            Error:        errors.Count == 0 ? null : string.Join("; ", errors));
    }
}
