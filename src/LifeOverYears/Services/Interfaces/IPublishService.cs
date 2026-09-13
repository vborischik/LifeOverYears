using LifeOverYears.Models;

namespace LifeOverYears.Services.Interfaces;

// Publishes one finished run to every configured target and records the
// outcome. The storage step (a public URL) happens here, once, before the
// targets that need it.
public interface IPublishService
{
    IReadOnlyList<string> Targets { get; }

    Task<PublishState> PublishAsync(PublishRequest request, CancellationToken ct = default);
}
