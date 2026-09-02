using LifeOverYears.Models;

namespace LifeOverYears.Services.Interfaces;

public interface IDataService
{
    Task<EraProfile> LoadEraProfileAsync(int year);
    Task<SceneDna> LoadSceneDnaAsync(string id);
    Task SaveSceneDnaAsync(SceneDna sceneDna);
    Task<string> LoadPromptAsync(string name);
    Task<IReadOnlyList<(string Name, int From, int To)>> LoadGasBrandsAsync();

    // data/brands/motel-brands.txt — motel chains and the years each was on
    // the road, same Name|from|to shape as the gas brands.
    Task<IReadOnlyList<(string Name, int From, int To)>> LoadMotelBrandsAsync();
    Task<IReadOnlyDictionary<string, IReadOnlyList<string>>> LoadCornerShopNamesAsync();

    // data/brands/series/{name}.json — one brand's whole fifty-year arc, the
    // input the brand-series mode uses in place of a photograph and a Vision
    // call. Throws when the file is absent: a mistyped series name has no
    // sensible fallback, unlike a missing caption pool.
    Task<BrandSeries> LoadBrandSeriesAsync(string name);

    // data/brands/center-replacements.txt — the trades that move into a dead
    // retail box. Name|from|to|category, the same parser as the gas and motel
    // brands with the optional fourth field they do not carry: the category is
    // what stops two gyms opening in adjacent bays. Read only by the
    // brand-series path; the photo path has no era that redevelops.
    Task<IReadOnlyList<(string Name, int From, int To, string Category)>> LoadCenterReplacementsAsync();

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
