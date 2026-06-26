namespace LifeOverYears.Models;

public record Publication(
    string Id,
    string VideoId,
    string CaptionId,
    string Platform,
    string Url,
    string PublishedAt);
