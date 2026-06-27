using LifeOverYears.Models;
using LifeOverYears.Services.Interfaces;
using Microsoft.Extensions.Logging;

namespace LifeOverYears.Services;

public sealed class VisionService : IVisionService
{
    private readonly IVisionProvider _vision;
    private readonly IDataService _data;
    private readonly ILogger<VisionService> _logger;

    public VisionService(IVisionProvider vision, IDataService data, ILogger<VisionService> logger)
    {
        _vision = vision;
        _data = data;
        _logger = logger;
    }

    public async Task<SceneDna> AnalyzeAsync(string photoPath)
    {
        _logger.LogInformation("Step 1 — analyzing photo: {Path}", photoPath);

        var prompt = await _data.LoadPromptAsync("vision");
        var sceneDna = await _vision.AnalyzeImageAsync(photoPath, prompt);

        var missing = SceneDnaValidator.Validate(sceneDna);
        if (missing.Count > 0)
        {
            _logger.LogWarning("SceneDna incomplete, missing: {Fields}", string.Join(", ", missing));
            sceneDna = await _vision.EnrichAsync(photoPath, sceneDna, missing);
            missing = SceneDnaValidator.Validate(sceneDna);
            _logger.LogInformation("After enrichment, still missing: {Fields}",
                missing.Count > 0 ? string.Join(", ", missing) : "none");
        }

        await _data.SaveSceneDnaAsync(sceneDna);
        _logger.LogInformation("SceneDna saved: {Id}", sceneDna.Id);
        return sceneDna;
    }
}
