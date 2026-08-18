using LifeOverYears.Models;

namespace LifeOverYears.Services.Interfaces;

public interface IDataService
{
    Task<EraProfile> LoadEraProfileAsync(int year);
    Task<SceneDna> LoadSceneDnaAsync(string id);
    Task SaveSceneDnaAsync(SceneDna sceneDna);
    Task<string> LoadPromptAsync(string name);
    Task<IReadOnlyList<(string Name, int From, int To)>> LoadGasBrandsAsync();

    // data/prompts/scene-types.txt — "key = phrase" per line, naming each scene
    // type and its defining physical parts for the synthetic base prompt.
    Task<IReadOnlyDictionary<string, string>> LoadSceneTypePhrasesAsync();
    Task SavePromptAsync(Prompt prompt);
    Task<IReadOnlyList<string>> LoadHashtagsAsync();

    // data/captions/{name}.txt — the caption bodies for one scene type.
    // Throws FileNotFoundException when absent, so callers can fall back.
    Task<string> LoadCaptionBodiesAsync(string name);

    // data/captions/titles/{name}.txt — the YouTube title hooks for one scene
    // type. Throws FileNotFoundException when absent, so callers can fall back.
    Task<string> LoadTitleTemplatesAsync(string name);
}
