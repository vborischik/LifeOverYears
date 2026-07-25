using LifeOverYears.Models;
using LifeOverYears.Services.Interfaces;
using Microsoft.Extensions.Logging;

namespace LifeOverYears.Services;

public sealed class CaptionService : ICaptionService
{
    private readonly IDataService _data;
    private readonly ICaptionProvider _provider;
    private readonly ILogger<CaptionService> _logger;

    public CaptionService(IDataService data, ICaptionProvider provider, ILogger<CaptionService> logger)
    {
        _data = data;
        _provider = provider;
        _logger = logger;
    }

    public async Task<Caption> GenerateAsync(SceneDna sceneDna)
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

        // Minimal, location-agnostic context. We deliberately do NOT feed a
        // specific city/state — the copy stays about a generic small American town.
        var userContext = $"Scene type: {sceneType}. Write the description now.";

        var description = await _provider.GenerateDescriptionAsync(systemPrompt, userContext);

        // Hashtags: one fixed set for everything, loaded from data/captions/hashtags.txt
        var hashtags = await _data.LoadHashtagsAsync();

        var caption = new Caption(
            Id: Guid.NewGuid().ToString("N"),
            Title: string.Empty,
            Description: description,
            Hashtags: hashtags);

        _logger.LogInformation("Caption generated: {Length} chars, {Tags} hashtags",
            description.Length, hashtags.Count);
        return caption;
    }
}
