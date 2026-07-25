using LifeOverYears.Models;

namespace LifeOverYears.Services.Interfaces;

public interface ICaptionService
{
    Task<Caption> GenerateAsync(SceneDna sceneDna);
}
