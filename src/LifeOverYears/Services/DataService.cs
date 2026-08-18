using LifeOverYears.Models;
using LifeOverYears.Services.Interfaces;
using Microsoft.Extensions.Logging;

namespace LifeOverYears.Services;

public sealed class DataService : IDataService
{
    private readonly IFileSystemProvider _fs;
    private readonly IJsonProvider _json;
    private readonly ILogger<DataService> _logger;

    public DataService(
        IFileSystemProvider fs,
        IJsonProvider json,
        ILogger<DataService> logger)
    {
        _fs = fs;
        _json = json;
        _logger = logger;
    }

    public async Task<EraProfile> LoadEraProfileAsync(int year)
    {
        var path = Path.Combine("data", "eras", $"{year}.json");
        _logger.LogInformation("Loading EraProfile for year {Year} from {Path}", year, path);
        return await _json.DeserializeFileAsync<EraProfile>(path);
    }

    public async Task<SceneDna> LoadSceneDnaAsync(string id)
    {
        var path = Path.Combine("data", "scenes", $"{id}.json");
        _logger.LogInformation("Loading SceneDna {Id} from {Path}", id, path);
        return await _json.DeserializeFileAsync<SceneDna>(path);
    }

    public async Task SaveSceneDnaAsync(SceneDna sceneDna)
    {
        var path = Path.Combine("data", "scenes", $"{sceneDna.Id}.json");
        _logger.LogInformation("Saving SceneDna {Id} to {Path}", sceneDna.Id, path);
        await _fs.EnsureDirectoryExistsAsync(Path.GetDirectoryName(path)!);
        await _json.SerializeFileAsync(sceneDna, path);
    }

    public async Task<string> LoadPromptAsync(string name)
    {
        var path = Path.Combine("data", "prompts", $"{name}.txt");
        _logger.LogInformation("Loading prompt {Name} from {Path}", name, path);
        return await _fs.ReadAllTextAsync(path);
    }

    public async Task<IReadOnlyList<(string Name, int From, int To)>> LoadGasBrandsAsync()
    {
        var path = Path.Combine("data", "brands", "gas-brands.txt");
        _logger.LogInformation("Loading gas brands from {Path}", path);
        var text = await _fs.ReadAllTextAsync(path);

        var brands = new List<(string Name, int From, int To)>();
        foreach (var line in text.Split('\n'))
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0)
                continue;
            var parts = trimmed.Split('|');
            if (parts.Length != 3
                || string.IsNullOrWhiteSpace(parts[0])
                || !int.TryParse(parts[1], out var from)
                || !int.TryParse(parts[2], out var to))
            {
                _logger.LogWarning("Skipping malformed gas brand line: {Line}", trimmed);
                continue;
            }
            brands.Add((parts[0].Trim(), from, to));
        }
        return brands;
    }

    public async Task<IReadOnlyDictionary<string, string>> LoadSceneTypePhrasesAsync()
    {
        var path = Path.Combine("data", "prompts", "scene-types.txt");
        _logger.LogInformation("Loading scene type phrases from {Path}", path);
        var text = await _fs.ReadAllTextAsync(path);

        var phrases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in text.Split('\n'))
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0)
                continue;
            // Split on the FIRST '=' only: a phrase may legitimately contain one.
            var sep = trimmed.IndexOf('=');
            if (sep <= 0 || sep == trimmed.Length - 1)
            {
                _logger.LogWarning("Skipping malformed scene type line: {Line}", trimmed);
                continue;
            }
            var key    = trimmed[..sep].Trim();
            var phrase = trimmed[(sep + 1)..].Trim();
            if (key.Length == 0 || phrase.Length == 0)
            {
                _logger.LogWarning("Skipping malformed scene type line: {Line}", trimmed);
                continue;
            }
            phrases[key] = phrase;
        }
        return phrases;
    }

    public async Task SavePromptAsync(Prompt prompt)
    {
        var path = Path.Combine("output", "prompts", prompt.SceneDnaId, $"{prompt.Year}.json");
        _logger.LogInformation("Saving Prompt {Id} for year {Year} to {Path}", prompt.Id, prompt.Year, path);
        await _fs.EnsureDirectoryExistsAsync(Path.GetDirectoryName(path)!);
        await _json.SerializeFileAsync(prompt, path);
    }

    public async Task<string> LoadCaptionBodiesAsync(string name)
    {
        var path = Path.Combine("data", "captions", $"{name}.txt");
        _logger.LogInformation("Loading caption bodies {Name} from {Path}", name, path);
        return await _fs.ReadAllTextAsync(path);
    }

    public async Task<string> LoadTitleTemplatesAsync(string name)
    {
        var path = Path.Combine("data", "captions", "titles", $"{name}.txt");
        _logger.LogInformation("Loading title templates {Name} from {Path}", name, path);
        return await _fs.ReadAllTextAsync(path);
    }

    public async Task<IReadOnlyList<string>> LoadHashtagsAsync()
    {
        var path = Path.Combine("data", "captions", "hashtags.txt");
        _logger.LogInformation("Loading hashtags from {Path}", path);
        var text = await _fs.ReadAllTextAsync(path);
        return text.Split('\n')
            .Select(l => l.Trim())
            .Where(l => l.Length > 0)
            .ToList();
    }
}
