namespace LifeOverYears.Models;

public record Caption(
    string Id,
    string Title,
    string Description,
    IReadOnlyList<string> Hashtags);
