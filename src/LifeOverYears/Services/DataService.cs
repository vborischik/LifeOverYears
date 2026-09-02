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

    public async Task<IReadOnlyList<(string Name, int From, int To)>> LoadGasBrandsAsync() =>
        Undated(await ReadBrandTimelineAsync("gas-brands.txt"));

    // Motel chains carry the same shape as gas brands — a name and the years it
    // was on the road — so they share one parser rather than two that can drift.
    public async Task<IReadOnlyList<(string Name, int From, int To)>> LoadMotelBrandsAsync() =>
        Undated(await ReadBrandTimelineAsync("motel-brands.txt"));

    // Gas and motel files carry no category, and their callers have no use for
    // one. Projecting it away here keeps a single parser without widening the
    // tuple every one of those callers reads.
    private static IReadOnlyList<(string Name, int From, int To)> Undated(
        IReadOnlyList<(string Name, int From, int To, string Category)> brands) =>
        brands.Select(b => (b.Name, b.From, b.To)).ToList();

    private async Task<IReadOnlyList<(string Name, int From, int To, string Category)>> ReadBrandTimelineAsync(string fileName)
    {
        var path = Path.Combine("data", "brands", fileName);
        _logger.LogInformation("Loading brand timeline from {Path}", path);
        var text = await _fs.ReadAllTextAsync(path);

        var brands = new List<(string Name, int From, int To, string Category)>();
        foreach (var line in text.Split('\n'))
        {
            var trimmed = line.Trim();
            // '#' comments so a file can explain its own columns.
            if (trimmed.Length == 0 || trimmed.StartsWith('#'))
                continue;
            var parts = trimmed.Split('|');
            // Three fields or four: the category is optional, carried only by
            // the files whose callers need to keep two of a kind apart.
            if (parts.Length is not (3 or 4)
                || string.IsNullOrWhiteSpace(parts[0])
                || !int.TryParse(parts[1], out var from)
                || !int.TryParse(parts[2], out var to))
            {
                _logger.LogWarning("Skipping malformed brand line in {File}: {Line}", fileName, trimmed);
                continue;
            }
            var category = parts.Length == 4 ? parts[3].Trim() : "";
            brands.Add((parts[0].Trim(), from, to, category));
        }
        return brands;
    }

    // Corner shop names, grouped by the kind of shop they belong to. The origin
    // trades ("grocery", "pharmacy") and the liquor names the shop turns over to
    // ("liquor_urban", "liquor_suburban") live in two files, because the liquor
    // pool is split by urban register and carries its own rules about which
    // register belongs on which frontage. Both parse the same way and merge into
    // one map, so callers see a single set of kinds. Unlike gas brands these
    // carry no year range: which kind is on the sign follows the run's own arc,
    // not history.
    public async Task<IReadOnlyDictionary<string, IReadOnlyList<string>>> LoadCornerShopNamesAsync()
    {
        var byKind = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        await ReadCornerShopNamesInto(byKind, "corner-shop-names.txt");
        await ReadCornerShopNamesInto(byKind, "corner-shop-liquor-names.txt");

        return byKind.ToDictionary(
            kv => kv.Key,
            kv => (IReadOnlyList<string>)kv.Value,
            StringComparer.OrdinalIgnoreCase);
    }

    // Blank lines and '#' comments are skipped so each file can explain itself.
    private async Task ReadCornerShopNamesInto(Dictionary<string, List<string>> byKind, string fileName)
    {
        var path = Path.Combine("data", "brands", fileName);
        _logger.LogInformation("Loading corner shop names from {Path}", path);
        var text = await _fs.ReadAllTextAsync(path);

        foreach (var line in text.Split('\n'))
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0 || trimmed.StartsWith('#'))
                continue;
            var parts = trimmed.Split('|');
            if (parts.Length != 2
                || string.IsNullOrWhiteSpace(parts[0])
                || string.IsNullOrWhiteSpace(parts[1]))
            {
                _logger.LogWarning("Skipping malformed corner shop name line: {Line}", trimmed);
                continue;
            }
            var kind = parts[0].Trim();
            if (!byKind.TryGetValue(kind, out var names))
                byKind[kind] = names = new List<string>();
            names.Add(parts[1].Trim());
        }
    }

    // Same three-field timeline as the gas and motel brands, so it shares their
    // parser rather than gaining a third that can drift from them.
    public Task<IReadOnlyList<(string Name, int From, int To, string Category)>> LoadCenterReplacementsAsync() =>
        ReadBrandTimelineAsync("center-replacements.txt");

    public async Task<BrandSeries> LoadBrandSeriesAsync(string name)
    {
        var path = Path.Combine("data", "brands", "series", $"{name}.json");
        _logger.LogInformation("Loading brand series {Name} from {Path}", name, path);
        if (!File.Exists(path))
            throw new FileNotFoundException(
                $"No brand series '{name}' — expected {path}. Available: " +
                string.Join(", ", AvailableSeries()), path);
        return await _json.DeserializeFileAsync<BrandSeries>(path);
    }

    // Named in the exception above rather than logged, because the one moment
    // anybody needs this list is when they have just got the name wrong.
    private static IEnumerable<string> AvailableSeries()
    {
        var dir = Path.Combine("data", "brands", "series");
        return Directory.Exists(dir)
            ? Directory.EnumerateFiles(dir, "*.json").Select(Path.GetFileNameWithoutExtension).OfType<string>().Order()
            : Enumerable.Empty<string>();
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
