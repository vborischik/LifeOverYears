using LifeOverYears.Models;

namespace LifeOverYears.Services.Interfaces;

public interface INvidiaProvider
{
    Task<SceneDna> AnalyzeImageAsync(string photoPath, string prompt);
    Task<HistoricalImage> GenerateImageAsync(Prompt prompt);
}
