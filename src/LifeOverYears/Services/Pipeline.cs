using LifeOverYears.Services.Interfaces;
using Microsoft.Extensions.Logging;

namespace LifeOverYears.Services;

public sealed class Pipeline
{
    private readonly IVisionService _vision;
    private readonly IPromptService _prompt;
    private readonly IDataService _data;
    private readonly ILogger<Pipeline> _logger;

    public Pipeline(
        IVisionService vision,
        IPromptService prompt,
        IDataService data,
        ILogger<Pipeline> logger)
    {
        _vision = vision;
        _prompt = prompt;
        _data = data;
        _logger = logger;
    }

    public async Task RunAsync(string photoPath, int year)
    {
        _logger.LogInformation("Pipeline started for: {PhotoPath}, year: {Year}", photoPath, year);

        // Step 1 — SceneDna
        var sceneDna = await _vision.AnalyzeAsync(photoPath);
        _logger.LogInformation("Step 1 complete — SceneDna: id={Id} terrain={Terrain} buildings={Buildings}",
            sceneDna.Id,
            sceneDna.Environment.Terrain,
            sceneDna.Geometry.Buildings.Count);

        // Step 2 — Prompt
        var eraProfile = await _data.LoadEraProfileAsync(year);
        var prompt = await _prompt.BuildAsync(sceneDna, eraProfile);
        _logger.LogInformation("Step 2 complete — Prompt: id={Id} year={Year} length={Length}\n{Text}",
            prompt.Id,
            prompt.Year,
            prompt.Text.Length,
            prompt.Text);
    }
}
