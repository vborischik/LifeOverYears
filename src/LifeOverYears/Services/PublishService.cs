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
    private readonly IMusicService _music;
    private readonly ILogger<PublishService> _logger;

    public IReadOnlyList<string> Targets { get; }

    // `targets` is the configured list, in order; each must have a provider
    // in `available` or the service refuses to be built — a misspelled
    // platform in config is not something to discover on the first publish.
    public PublishService(
        IReadOnlyList<string> targets,
        IReadOnlyList<IPublishTarget> available,
        IPublicStorage? storage,
        IMusicService music,
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
        _music   = music;
        _logger  = logger;
    }

    // Targets are worked family by family, because the family decides the
    // file: YouTube's bed is muxed under the video, Meta's under another copy,
    // and the copy Instagram pulls by URL has to be the Meta one. So per
    // family: mux, then upload once if anything in it needs a URL, then post.
    public async Task<PublishState> PublishAsync(PublishRequest request, CancellationToken ct = default)
    {
        var publications = new List<Publication>();
        var errors       = new List<string>();
        var music        = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var group in Targets.GroupBy(_music.FamilyOf))
        {
            PublishRequest familyRequest;
            try
            {
                var (withMusic, track) = await _music.WithMusicAsync(group.Key, request, ct);
                familyRequest = withMusic;
                if (track.Length > 0) music[group.Key] = track;
            }
            catch (Exception ex)
            {
                // No bed, no post — the rule this project publishes under.
                _logger.LogError(ex, "Music for {Family} failed; its targets are skipped", group.Key);
                foreach (var name in group) errors.Add($"{name}: no music — {ex.Message}");
                continue;
            }

            if (group.Any(t => NeedsPublicUrl.Contains(t)) && familyRequest.PublicVideoUrl is null)
            {
                try
                {
                    var url = await _storage!.UploadPublicAsync(
                        familyRequest.Video.FilePath, $"{request.Video.Id}.{group.Key}.mp4", ct);
                    familyRequest = familyRequest with { PublicVideoUrl = url };
                }
                catch (Exception ex)
                {
                    // Nothing in this family that needs the URL can run; the
                    // byte-taking targets still can, so it is recorded and
                    // the loop continues.
                    _logger.LogError(ex, "Storage upload failed; URL-based {Family} targets will be skipped", group.Key);
                    errors.Add($"storage: {ex.Message}");
                }
            }

            foreach (var name in group)
            {
                if (NeedsPublicUrl.Contains(name) && familyRequest.PublicVideoUrl is null)
                {
                    errors.Add($"{name}: no public URL");
                    continue;
                }
                try
                {
                    var publication = await _targets[name].PublishAsync(familyRequest, ct);
                    publications.Add(publication);
                    _logger.LogInformation("Published to {Platform}: {Url}", name, publication.Url);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Publish to {Platform} failed", name);
                    errors.Add($"{name}: {ex.Message}");
                }
            }
        }

        return new PublishState(
            Status:       errors.Count == 0 ? "published" : "failed",
            DecidedAt:    DateTimeOffset.UtcNow.ToString("o"),
            Publications: publications,
            Error:        errors.Count == 0 ? null : string.Join("; ", errors),
            Music:        music.Count == 0 ? null : music);
    }
}
