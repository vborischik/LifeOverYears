namespace LifeOverYears.Models;

// Run-level facts about how the scene evolved, handed to caption generation so
// it writes about THIS run's specific arc instead of a generic template filler.
public sealed record SceneNarrative(
    int FirstYear,
    int LastYear,
    string FinalCondition,
    string? FirstBrand,
    string? LastBrand,
    bool RebrandOccurred);
