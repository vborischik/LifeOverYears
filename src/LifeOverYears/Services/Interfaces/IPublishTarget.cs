using LifeOverYears.Models;

namespace LifeOverYears.Services.Interfaces;

// One platform. Each implementation owns that platform's quirks — the
// three-step container dance, the resumable session, the multipart form —
// and hands back the same Publication record, so whatever runs them later
// cannot tell a Reel from a Short from a Telegram post.
//
// Not wired: no implementation is registered in AppModule and no CLI mode
// calls one. They compile, they carry the platform knowledge, and the
// orchestration on top is a separate change.
public interface IPublishTarget
{
    // Lower-case platform name, the value Publication.Platform carries.
    string Platform { get; }

    Task<Publication> PublishAsync(PublishRequest request, CancellationToken ct = default);
}
