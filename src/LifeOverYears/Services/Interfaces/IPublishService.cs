using LifeOverYears.Models;

namespace LifeOverYears.Services.Interfaces;

// Publishes one finished run to every configured target and records the
// outcome. The storage step (a public URL) happens here, once, before the
// targets that need it.
public interface IPublishService
{
    IReadOnlyList<string> Targets { get; }

    // `only` narrows this publish to a subset of the configured targets —
    // a re-run for the one platform that failed. Null means all of them.
    Task<PublishState> PublishAsync(PublishRequest request, IReadOnlyList<string>? only = null, CancellationToken ct = default);
}
