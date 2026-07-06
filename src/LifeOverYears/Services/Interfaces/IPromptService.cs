using LifeOverYears.Models;

namespace LifeOverYears.Services.Interfaces;

public interface IPromptService
{
    Task<Prompt> BuildAsync(SceneDna sceneDna, EraProfile eraProfile, GenerationContext context);
}
