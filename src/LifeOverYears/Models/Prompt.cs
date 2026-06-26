namespace LifeOverYears.Models;

public record Prompt(
    string Id,
    string SceneDnaId,
    int Year,
    string Text,
    string CreatedAt);
