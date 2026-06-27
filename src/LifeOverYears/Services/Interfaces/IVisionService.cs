using LifeOverYears.Models;

namespace LifeOverYears.Services.Interfaces;

public interface IVisionService
{
    Task<SceneDna> AnalyzeAsync(string photoPath);
}
