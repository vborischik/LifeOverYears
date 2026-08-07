using LifeOverYears.Models;
using LifeOverYears.Services.Interfaces;
using Microsoft.Extensions.Logging;

namespace LifeOverYears.Services;

public sealed class CaptionService : ICaptionService
{
    private readonly IDataService _data;
    private readonly ILogger<CaptionService> _logger;

    // Anchors that fit any ordinary American place.
    public static readonly IReadOnlyList<string> CommonAngles = new[]
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
    public static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> AnglesByScene = new Dictionary<string, IReadOnlyList<string>>
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
        ["auto_repair"] = new[]
        {
            "the mechanic who knew your car better than you did",
            "waiting in the little office while they finished the brakes",
            "the smell of motor oil and rubber drifting out of the bay",
            "the parts calendar hanging behind the counter",
            "handing the keys to the same man who worked on your father's car",
            "the sound of the impact wrench through the open bay door",
        },
    };

    public static IReadOnlyList<string> AnglesFor(string sceneType) =>
        AnglesByScene.TryGetValue(sceneType, out var specific)
            ? specific.Concat(CommonAngles).ToArray()
            : CommonAngles;

    public CaptionService(IDataService data, ILogger<CaptionService> logger)
    {
        _data = data;
        _logger = logger;
    }

    // Bodies within a caption file are separated by a line containing only "---".
    private const string BodySeparator = "---";

    public async Task<Caption> GenerateAsync(SceneDna sceneDna, SceneNarrative narrative)
    {
        // Caption bodies are categorized by scene type: data/captions/{sceneType}.txt,
        // falling back to base.txt when no scene-specific file exists — the same
        // lookup the LLM system prompts used, one directory over.
        var sceneType = string.IsNullOrWhiteSpace(sceneDna.SceneType) ? "base" : sceneDna.SceneType;
        string raw;
        try
        {
            raw = await _data.LoadCaptionBodiesAsync(sceneType);
            _logger.LogInformation("Caption: using scene-specific bodies {SceneType}.txt", sceneType);
        }
        catch (FileNotFoundException)
        {
            raw = await _data.LoadCaptionBodiesAsync("base");
            _logger.LogInformation("Caption: no {SceneType}.txt, falling back to base.txt", sceneType);
        }

        var bodies = SplitBodies(raw);
        if (bodies.Count == 0)
            throw new InvalidOperationException(
                $"Caption: data/captions/{sceneType}.txt contains no bodies");

        // Rotate wording weekly so the feed does not repeat itself, offset by the
        // scene id so two scenes captioned in the same week don't come out
        // identical. Deterministic: the same scene in the same week always gets
        // the same body.
        var week  = System.Globalization.ISOWeek.GetWeekOfYear(DateTime.UtcNow);
        var index = SelectBodyIndex(week, sceneDna.Id, bodies.Count);
        var body  = bodies[index];

        var angles = AnglesFor(sceneType);
        var angle  = angles[Random.Shared.Next(angles.Count)];

        var description = body
            .Replace("{firstYear}", narrative.FirstYear.ToString())
            .Replace("{lastYear}",  narrative.LastYear.ToString())
            .Replace("{angle}",     angle)
            .Replace("{condition}", MapFinalCondition(narrative.FinalCondition));

        // Hashtags: one shared pool for every scene type, loaded from
        // data/captions/hashtags.txt, then narrowed to a pinned set plus a
        // couple of sampled tags.
        var hashtags = SelectHashtags(await _data.LoadHashtagsAsync());

        var caption = new Caption(
            Id: Guid.NewGuid().ToString("N"),
            Title: string.Empty,
            Description: description,
            Hashtags: hashtags);

        _logger.LogInformation(
            "Caption assembled: {Length} chars, body {Index}/{Count} (week {Week}), {Tags} hashtags, angle=\"{Angle}\"",
            description.Length, index + 1, bodies.Count, week, caption.Hashtags.Count, angle);
        return caption;
    }

    // Which body a given scene gets in a given ISO week. Advancing the week by
    // one advances the index by one, so any run of bodies.Count consecutive
    // weeks visits every body exactly once — the rotation can never stall on a
    // subset. The scene-id offset keeps two scenes captioned the same week apart.
    public static int SelectBodyIndex(int isoWeek, string? sceneDnaId, int bodyCount) =>
        0;

    // Splits on a line that is exactly the separator, so a "---" inside a body
    // line cannot break it apart. Blank bodies are dropped.
    public static IReadOnlyList<string> SplitBodies(string raw) =>
        raw.Replace("\r\n", "\n")
           .Split($"\n{BodySeparator}\n")
           .Select(b => b.Trim())
           .Where(b => b.Length > 0)
           .ToList();

    // string.GetHashCode is randomized per process in .NET, which would make the
    // body choice differ between runs of the same scene in the same week. FNV-1a
    // keeps it stable across processes and machines.
    private static uint StableHash(string? value)
    {
        if (string.IsNullOrEmpty(value)) return 0;
        unchecked
        {
            var hash = 2166136261u;
            foreach (var c in value)
            {
                hash ^= c;
                hash *= 16777619u;
            }
            return hash;
        }
    }

    private const int PinnedCount = 3;
    private const int RandomCount = 2;

    // The first three lines of hashtags.txt are pinned reach tags and stay
    // in file order; the rest is a pool we sample from so posts do not
    // repeat the same trailing tags every time. Reordering the file is how
    // the pinned set is changed — no code edit needed.
    private static IReadOnlyList<string> SelectHashtags(IReadOnlyList<string> all)
    {
        // Too short to split into pinned + sampled: hand back what there is
        // rather than throwing on a trimmed-down file.
        if (all.Count <= PinnedCount + RandomCount)
            return all;

        var selected = new List<string>(PinnedCount + RandomCount);
        selected.AddRange(all.Take(PinnedCount));

        // Partial Fisher-Yates over the remainder: draws RandomCount distinct
        // entries without shuffling or copying the whole pool.
        var pool = all.Skip(PinnedCount).ToArray();
        for (var i = 0; i < RandomCount; i++)
        {
            var j = Random.Shared.Next(i, pool.Length);
            (pool[i], pool[j]) = (pool[j], pool[i]);
            selected.Add(pool[i]);
        }

        return selected;
    }

    public const string UnknownConditionText = "changed a lot over the years";

    public static string MapFinalCondition(string condition) => condition switch
    {
        "thriving" or "busy" => "still standing and busy",
        "new"                => "rebuilt and freshly reopened",
        "restored"           => "restored and still open",
        "declining"          => "still standing, but showing its age",
        "abandoned"          => "empty and abandoned now",
        "squatted"           => "long closed, taken over by squatters",
        _                    => UnknownConditionText,
    };
}
