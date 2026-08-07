using LifeOverYears.Models;

namespace LifeOverYears.Services.Interfaces;

public interface IDataService
{
    Task<EraProfile> LoadEraProfileAsync(int year);
    Task<SceneDna> LoadSceneDnaAsync(string id);
    Task SaveSceneDnaAsync(SceneDna sceneDna);
    Task<string> LoadPromptAsync(string name);
    Task<IReadOnlyList<(string Name, int From, int To)>> LoadGasBrandsAsync();
    Task SavePromptAsync(Prompt prompt);
    Task<IReadOnlyList<string>> LoadHashtagsAsync();

    // data/captions/{name}.txt — the caption bodies for one scene type.
    // Throws FileNotFoundException when absent, so callers can fall back.
    Task<string> LoadCaptionBodiesAsync(string name);
}
