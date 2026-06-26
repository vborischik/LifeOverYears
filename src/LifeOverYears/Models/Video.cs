namespace LifeOverYears.Models;

public record Video(
    string Id,
    IReadOnlyList<string> ImageIds,
    string FilePath,
    string CreatedAt);
