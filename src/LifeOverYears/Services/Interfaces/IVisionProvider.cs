using LifeOverYears.Models;

namespace LifeOverYears.Services.Interfaces;

public interface IVisionProvider
{
    Task<SceneDna> AnalyzeImageAsync(string photoPath, string prompt);
}
