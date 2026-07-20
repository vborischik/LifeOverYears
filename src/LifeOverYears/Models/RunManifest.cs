namespace LifeOverYears.Models;

// Persisted as run.json in the run root so a later collect invocation can
// recover the run's parameters without re-parsing CLI arguments.
public record RunManifest(
    string SceneDnaId,
    string SourcePath,
    IReadOnlyList<int> Years,
    string CreatedAt);
