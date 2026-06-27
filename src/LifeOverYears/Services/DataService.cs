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
}
