using LifeOverYears.Models;
using LifeOverYears.Services.Interfaces;
using Microsoft.Extensions.Logging;

namespace LifeOverYears.Services;

public sealed class CaptionService : ICaptionService
{
    private readonly IDataService _data;
    private readonly ICaptionProvider _provider;
    private readonly ILogger<CaptionService> _logger;

    // Sampled fresh each call so the model anchors on a different concrete
    // memory instead of defaulting to whichever detail it favors most — this,
    // combined with the run-specific facts below, is what breaks caption
    // repetition across runs.
    private static readonly IReadOnlyList<string> MemoryAngles = new[]
    {
        "the sound of the bell when a car pulled in",
        "the smell of gasoline mixed with rain",
        "learning to pump gas for the first time",
        "a summer road trip stop with the whole family packed in the car",
        "an attendant who knew every regular by name",
        "checking the oil and washing the windshield by hand",
        "grabbing a cold soda or candy bar after school",
        "the glow of the sign at night on an otherwise dark road",
        "waiting in the back seat while a parent paid inside",
        "the last time anyone remembers stopping there before it closed",
    };

    public CaptionService(IDataService data, ICaptionProvider provider, ILogger<CaptionService> logger)
    {
        _data = data;
        _provider = provider;
        _logger = logger;
    }

    public async Task<Caption> GenerateAsync(SceneDna sceneDna, SceneNarrative narrative)
    {
        // System prompt is categorized by scene type: caption-{sceneType}.txt,
        // falling back to caption-base.txt when no scene-specific file exists.
        var sceneType = string.IsNullOrWhiteSpace(sceneDna.SceneType) ? "base" : sceneDna.SceneType;
        string systemPrompt;
        try
        {
            systemPrompt = await _data.LoadPromptAsync($"caption-{sceneType}");
            _logger.LogInformation("Caption: using scene-specific prompt caption-{SceneType}", sceneType);
        }
        catch (FileNotFoundException)
        {
            systemPrompt = await _data.LoadPromptAsync("caption-base");
            _logger.LogInformation("Caption: no caption-{SceneType}, falling back to caption-base", sceneType);
        }

        var angle = MemoryAngles[Random.Shared.Next(MemoryAngles.Count)];

        // Rich, run-specific context. We deliberately do NOT feed a specific
        // city/state — the copy stays about a generic small American town —
        // but everything else about THIS run's arc is handed over so the model
        // can't default to a one-size-fits-all caption.
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"Scene type: {sceneType}.");
        sb.AppendLine($"The video spans {narrative.FirstYear} to {narrative.LastYear}.");
        sb.AppendLine($"By the last year shown, the place is: {MapFinalCondition(narrative.FinalCondition)}.");
        if (sceneType == "gas_station" && narrative.FirstBrand is not null)
        {
            sb.AppendLine(narrative.RebrandOccurred
                && !string.Equals(narrative.FirstBrand, narrative.LastBrand, StringComparison.OrdinalIgnoreCase)
                ? $"It was known as {narrative.FirstBrand}, and later became {narrative.LastBrand}."
                : $"It was known as {narrative.FirstBrand}.");
        }
        sb.AppendLine($"Anchor the caption on this specific memory: {angle}.");
        sb.AppendLine("Write the description now, in your own words — do not reuse stock opening lines from your instructions.");
        var userContext = sb.ToString();

        var description = await _provider.GenerateDescriptionAsync(systemPrompt, userContext);

        // Hashtags: one fixed set for everything, loaded from data/captions/hashtags.txt
        var hashtags = await _data.LoadHashtagsAsync();

        var caption = new Caption(
            Id: Guid.NewGuid().ToString("N"),
            Title: string.Empty,
            Description: description,
            Hashtags: hashtags);

        _logger.LogInformation("Caption generated: {Length} chars, {Tags} hashtags, angle=\"{Angle}\"",
            description.Length, hashtags.Count, angle);
        return caption;
    }

    private static string MapFinalCondition(string condition) => condition switch
    {
        "thriving" or "busy"      => "still standing and busy",
        "new"                     => "rebuilt and freshly reopened",
        "restored"                => "restored and still open",
        "declining"               => "still standing, but showing its age",
        "abandoned"               => "empty and abandoned now",
        "squatted"                => "long closed, taken over by squatters",
        _                         => "changed a lot over the years",
    };
}
