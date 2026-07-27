using LifeOverYears.Models;
using LifeOverYears.Services.Interfaces;
using Microsoft.Extensions.Logging;

namespace LifeOverYears.Services;

public sealed class CaptionService : ICaptionService
{
    private readonly IDataService _data;
    private readonly ICaptionProvider _provider;
    private readonly ILogger<CaptionService> _logger;

    // Anchors that fit any ordinary American place.
    private static readonly string[] CommonAngles =
    {
        "a summer afternoon with nothing in particular to do",
        "riding along while a parent ran errands",
        "the way the light looked there late on a summer evening",
        "running into someone you knew every single time",
        "the last time anyone remembers going there before it closed",
    };

    // Scene-specific anchors. Feeding forecourt memories (pumping gas, checking
    // the oil) to a main street or a strip mall is what made captions read as
    // interchangeable, so each type draws from its own vocabulary first.
    private static readonly Dictionary<string, string[]> AnglesByScene = new()
    {
        ["gas_station"] = new[]
        {
            "the sound of the bell when a car pulled in",
            "the smell of gasoline mixed with rain",
            "learning to pump gas for the first time",
            "an attendant who knew every regular by name",
            "checking the oil and washing the windshield by hand",
            "a cold bottle of soda from the machine out front",
        },
        ["downtown_street"] = new[]
        {
            "storefront windows decorated for Christmas",
            "the soda fountain counter at the drugstore",
            "Saturday afternoon downtown when everyone was out",
            "the parade coming down the main street",
            "the smell of the bakery on the corner",
            "meeting friends under the theater marquee",
        },
        ["strip_mall"] = new[]
        {
            "browsing the aisles of the video rental place on a Friday night",
            "the arcade cabinets humming in the corner",
            "takeout from the Chinese place at the end of the row",
            "pushing a cart out to the car at the anchor supermarket",
            "sitting on the curb in the parking lot with friends",
            "the hum of the fluorescent lights under the storefront overhang",
        },
    };

    private static string[] AnglesFor(string sceneType) =>
        AnglesByScene.TryGetValue(sceneType, out var specific)
            ? specific.Concat(CommonAngles).ToArray()
            : CommonAngles;

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

        var angles = AnglesFor(sceneType);
        var angle = angles[Random.Shared.Next(angles.Length)];

        // Rich, run-specific context. We deliberately do NOT feed a specific
        // city/state — the copy stays about a generic small American town — but
        // everything else about THIS run's arc is handed over, so the model
        // cannot fall back on a one-size-fits-all caption.
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
        sb.AppendLine("Write the description now, in your own words — do not reuse stock opening lines from your instructions. Every sentence must be grammatically correct.");
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
        "thriving" or "busy" => "still standing and busy",
        "new"                => "rebuilt and freshly reopened",
        "restored"           => "restored and still open",
        "declining"          => "still standing, but showing its age",
        "abandoned"          => "empty and abandoned now",
        "squatted"           => "long closed, taken over by squatters",
        _                    => "changed a lot over the years",
    };
}
