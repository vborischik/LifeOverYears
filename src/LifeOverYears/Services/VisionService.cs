using LifeOverYears.Models;
using LifeOverYears.Services.Interfaces;
using Microsoft.Extensions.Logging;

namespace LifeOverYears.Services;

public sealed class VisionService : IVisionService
{
    private readonly IVisionProvider _vision;
    private readonly IDataService _data;
    private readonly ILogger<VisionService> _logger;

    // A second, targeted look at the few fields that are cheap to get wrong and
    // expensive to render wrong. On by default because the vision model costs
    // nothing here, and because the failure it catches is silent: a confident
    // wrong "parking" reads as a well-formed prompt and comes back as an
    // invented car park six frames deep.
    private readonly bool _doubleCheck;

    public VisionService(IVisionProvider vision, IDataService data, ILogger<VisionService> logger,
                         bool doubleCheck = true)
    {
        _vision = vision;
        _data = data;
        _logger = logger;
        _doubleCheck = doubleCheck;
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

        // After enrichment, not before: there is no point re-examining a field
        // that was blank a moment ago, and the verify prompt reads better when
        // every claim it lists is actually populated.
        if (_doubleCheck)
            sceneDna = await _vision.VerifyAsync(photoPath, sceneDna);

        await _data.SaveSceneDnaAsync(sceneDna);
        _logger.LogInformation("SceneDna saved: {Id}", sceneDna.Id);
        return sceneDna;
    }
}
