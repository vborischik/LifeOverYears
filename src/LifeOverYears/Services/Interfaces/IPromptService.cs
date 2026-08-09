using LifeOverYears.Models;

namespace LifeOverYears.Services.Interfaces;

public interface IPromptService
{
    Task<Prompt> BuildAsync(SceneDna sceneDna, EraProfile eraProfile, GenerationContext context);

    // Synthetic base: the base image prompt built from SceneDna alone, with no
    // source photo involved.
    // baseYear: the era the base is built in. Every era prompt edits this one
    // image, so it is built in the run's earliest year and later eras age it
    // forward — the direction the story runs anyway.
    Task<string> BuildBaseAsync(SceneDna sceneDna, int baseYear);
}
