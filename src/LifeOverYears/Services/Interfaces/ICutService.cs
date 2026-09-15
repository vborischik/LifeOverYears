using LifeOverYears.Models;

namespace LifeOverYears.Services.Interfaces;

// Which edit of the run a platform family gets. The run's master is the
// looping cut — present first, rewind, wipe back to the present — and it
// stays the master. A family whose cut differs is re-assembled from the
// run's stamped frames at publish time, the way its music is laid down.
public interface ICutService
{
    // "loop" (the master, untouched) or "chronological" (oldest to newest,
    // ends on the present, no loop tail).
    string CutFor(string family);

    // Returns the request with Video.FilePath pointing at the family's cut.
    // For "loop" that is the master itself; otherwise a silent re-cut beside
    // it, built once and reused.
    Task<PublishRequest> WithCutAsync(string family, PublishRequest request, CancellationToken ct = default);
}
