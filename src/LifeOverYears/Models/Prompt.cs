namespace LifeOverYears.Models;

public record Prompt(
    string Id,
    string SceneDnaId,
    int Year,
    string Text,
    IReadOnlyList<string> SelectedVehicles,
    string CreatedAt);
