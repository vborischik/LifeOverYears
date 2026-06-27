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
        await _data.SaveSceneDnaAsync(sceneDna);

        _logger.LogInformation("SceneDna saved: {Id}", sceneDna.Id);
        return sceneDna;
    }
}
