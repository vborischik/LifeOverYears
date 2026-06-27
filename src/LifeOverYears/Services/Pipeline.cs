using LifeOverYears.Services.Interfaces;
using Microsoft.Extensions.Logging;

namespace LifeOverYears.Services;

public sealed class Pipeline
{
    private readonly IVisionService _vision;
    private readonly ILogger<Pipeline> _logger;

    public Pipeline(IVisionService vision, ILogger<Pipeline> logger)
    {
        _vision = vision;
        _logger = logger;
    }

    public async Task RunAsync(string photoPath)
    {
        _logger.LogInformation("Pipeline started for: {PhotoPath}", photoPath);

        var sceneDna = await _vision.AnalyzeAsync(photoPath);
        _logger.LogInformation("Step 1 complete — SceneDna: id={Id} terrain={Terrain} buildings={Buildings}",
            sceneDna.Id,
            sceneDna.Environment.Terrain,
            sceneDna.Geometry.Buildings.Count);
    }
}
