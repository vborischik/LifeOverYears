namespace LifeOverYears.Models;

public record HistoricalImage(
    string Id,
    string PromptId,
    int Year,
    string FilePath,
    string Provider,
    string CreatedAt);
