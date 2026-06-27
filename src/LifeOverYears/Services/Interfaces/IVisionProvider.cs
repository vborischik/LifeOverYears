using LifeOverYears.Models;

namespace LifeOverYears.Services.Interfaces;

public interface IVisionProvider
{
    Task<SceneDna> AnalyzeImageAsync(string photoPath, string prompt);
    Task<SceneDna> EnrichAsync(string photoPath, SceneDna current, IReadOnlyList<string> missingFields);
}
