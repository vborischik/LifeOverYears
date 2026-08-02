using LifeOverYears.Models;

namespace LifeOverYears.Services.Interfaces;

public interface IPromptService
{
    Task<Prompt> BuildAsync(SceneDna sceneDna, EraProfile eraProfile, GenerationContext context);

    // Synthetic base: the base image prompt built from SceneDna alone, with no
    // source photo involved.
    Task<string> BuildBaseAsync(SceneDna sceneDna);
}
