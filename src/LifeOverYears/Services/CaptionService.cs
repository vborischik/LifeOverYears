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
        "the exact route you took to get there without thinking about it",
        "how quiet the whole street went on a Sunday",
        "being sent out on an errand you were proud to be trusted with",
        "waiting in the car while somebody ran in for one thing",
        "the smell of the place, which you would still know today",
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
            "the sound of the pump handle clicking off when the tank was full",
            "counting out exact change through the window",
            "the map rack by the door that nobody has needed since",
            "waiting in the car with the windows down while the tank filled",
            "the vending machine out front that took dimes",
            "getting your first job pumping gas at sixteen",
        },
        ["downtown_street"] = new[]
        {
            "storefront windows decorated for Christmas",
            "the soda fountain counter at the drugstore",
            "Saturday afternoon downtown when everyone was out",
            "the parade coming down the main street",
            "the smell of the bakery on the corner",
            "meeting friends under the theater marquee",
            "walking the whole block to see who was out",
            "the record shop where you spent your first paycheck",
            "buying your school shoes at the same store every August",
            "the bell over the door of every shop on the street",
            "meeting your parents outside the bank at a set time",
            "how long it took to walk two blocks because everyone stopped you",
        },
        ["strip_mall"] = new[]
        {
            "browsing the aisles of the video rental place on a Friday night",
            "the arcade cabinets humming in the corner",
            "takeout from the Chinese place at the end of the row",
            "pushing a cart out to the car at the anchor supermarket",
            "sitting on the curb in the parking lot with friends",
            "the hum of the fluorescent lights under the storefront overhang",
            "the laundromat lit up at night when everything else was dark",
            "sitting on the curb outside waiting for a ride",
            "the dry cleaner rack turning behind the window",
            "your mother's standing appointment at the salon in the row",
            "the smell of the pizza place at the end of the row",
            "learning to drive in that parking lot on a Sunday morning",
        },
        ["auto_repair"] = new[]
        {
            "the mechanic who knew your car better than you did",
            "waiting in the little office while they finished the brakes",
            "the smell of motor oil and rubber drifting out of the bay",
            "the parts calendar hanging behind the counter",
            "handing the keys to the same man who worked on your father's car",
            "the sound of the impact wrench through the open bay door",
            "the calendar behind the counter that never got changed",
            "the four chairs in the waiting room and the coffee nobody drank",
            "the handwritten invoice with carbon paper underneath",
            "the shop dog asleep in the office window",
            "the one car that sat in the lot for years",
            "being told what your car did not need",
        },
        ["corner_shop"] = new[]
        {
            "being sent up to the corner for a loaf of bread and told to keep the change",
            "the shopkeeper who knew your family's name and let it go on the tab",
            "the screen door banging shut behind everyone who went in",
            "buying penny candy with coins counted out on the counter",
            "the bench out front where the same men sat every afternoon",
            "when the whole block could get by without ever driving anywhere",
            "the bell over the door announcing you before you were inside",
            "the freezer by the door that you could hear from outside",
            "the tab your family kept until payday",
            "buying the Sunday paper for the whole house",
            "the phone behind the counter you were allowed to use",
            "how close it was — four minutes, and you went alone",
        },
        ["highway_urban"] = new[]
        {
            "watching the skyline come up over the last rise into town",
            "the exact interchange where the traffic always stopped",
            "counting the exits until yours from the back seat",
            "the commute you could drive without remembering any of it",
            "the radio station you kept until it went to static",
            "the overhead signs lighting up green as you came under them",
            "the one billboard that was there your whole childhood",
            "coming home late with the whole city lit up on the left",
            "the lane everybody knew to be in before the merge",
            "sitting still in traffic with the windows down in August",
            "the toll booth where somebody always had to find change",
            "the sound the tyres made changing from concrete to asphalt",
        },
        ["highway_rural"] = new[]
        {
            "the mile marker that meant you were nearly there",
            "not seeing another car for ten minutes at a stretch",
            "the radio going to static in the same dead stretch every trip",
            "the barn you watched for from the back seat every single trip",
            "the smell of cut fields coming in through the vent",
            "how far you could see down the road on a clear day",
            "the one gas station between here and the county line",
            "headlights of a truck a mile off in the dark",
            "the crest of the hill where you could finally see town",
            "falling asleep in the back and waking up almost home",
            "the lone billboard for a place still forty miles away",
            "having the whole road to yourself early on a Sunday",
        },
        ["mall"] = new[]
        {
            "the smell of Cinnabon drifting through the food court",
            "riding the escalator up and down for no reason",
            "picking a movie at the multiplex and committing to it",
            "meeting up by the fountain in center court",
            "walking the whole mall twice before buying anything",
            "the arcade tokens that never lasted as long as you wanted",
            "being dropped off with your friends and no way to be reached",
            "the pet store window that stopped every kid who walked past",
            "the department store perfume counter you had to walk through",
            "seeing a teacher out of school and not knowing what to say",
            "the bookstore where you read half a book without buying it",
            "the last lap of the whole mall before anybody went home",
        },
        ["shopping_center"] = new[]
        {
            "walking the whole row while a parent finished up at the anchor store",
            "the tenant panels on the pylon sign out by the road",
            "loading the trunk at the far end of the lot on a Saturday morning",
            "the five-and-dime counter between two much wider stores",
            "cutting across the parking lot instead of walking around it",
            "the anchor store that everyone in town called by its old name",
            "the Santa line in front of the anchor store every December",
            "back-to-school shopping that took an entire Saturday",
            "the coin ride outside the door that barely moved",
            "the buffet where your family went after church",
            "the snow pile at the edge of the lot that lasted into March",
            "seeing three people you knew between the car and the door",
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
        // lookup the LLM system prompts used, one directory over. Resolved
        // through SceneContentKey for the same reason the era content is: a
        // highway remembered from a city commute and one remembered from an
        // empty county road have nothing in common to say.
        var sceneType = string.IsNullOrWhiteSpace(sceneDna.SceneType)
            ? "base"
            : SceneContentKey.Resolve(sceneDna.SceneType, sceneDna.Environment.Terrain);
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

        // YouTube titles come from their own pool, data/captions/titles/{sceneType}.txt,
        // with the same base.txt fallback. A title is one line and far shorter than a
        // body, so it only ever carries the year placeholders — no angle, no condition.
        string rawTitles;
        try
        {
            rawTitles = await _data.LoadTitleTemplatesAsync(sceneType);
            _logger.LogInformation("Caption: using scene-specific titles {SceneType}.txt", sceneType);
        }
        catch (FileNotFoundException)
        {
            rawTitles = await _data.LoadTitleTemplatesAsync("base");
            _logger.LogInformation("Caption: no titles/{SceneType}.txt, falling back to titles/base.txt", sceneType);
        }

        var titles = SplitTitles(rawTitles);
        if (titles.Count == 0)
            throw new InvalidOperationException(
                $"Caption: data/captions/titles/{sceneType}.txt contains no titles");

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

        var title = SubstituteTitle(
            titles[Random.Shared.Next(titles.Count)], narrative.FirstYear, narrative.LastYear);

        var caption = new Caption(
            Id: Guid.NewGuid().ToString("N"),
            Title: title,
            Description: description,
            Hashtags: hashtags);

        _logger.LogInformation(
            "Caption assembled: {Length} chars, body {Index}/{Count} (week {Week}), {Tags} hashtags, angle=\"{Angle}\"",
            description.Length, index + 1, bodies.Count, week, caption.Hashtags.Count, angle);
        _logger.LogInformation(
            "Title assembled: {Length} chars, from {Count} candidates — \"{Title}\"",
            title.Length, titles.Count, title);
        return caption;
    }

    // Which body a given scene gets in a given ISO week. Advancing the week by
    // one advances the index by one, so any run of bodies.Count consecutive
    // weeks visits every body exactly once — the rotation can never stall on a
    // subset. The scene-id offset keeps two scenes captioned the same week apart.
    public static int SelectBodyIndex(int isoWeek, string? sceneDnaId, int bodyCount) =>
        (int)(((uint)isoWeek + StableHash(sceneDnaId)) % (uint)bodyCount);

    // Splits on a line that is exactly the separator, so a "---" inside a body
    // line cannot break it apart. Blank bodies are dropped.
    public static IReadOnlyList<string> SplitBodies(string raw) =>
        raw.Replace("\r\n", "\n")
           .Split($"\n{BodySeparator}\n")
           .Select(b => b.Trim())
           .Where(b => b.Length > 0)
           .ToList();

    // Title files are a plain one-per-line list — no body separators, since a
    // title is always a single line. Blank lines are dropped.
    public static IReadOnlyList<string> SplitTitles(string raw) =>
        raw.Replace("\r\n", "\n")
           .Split('\n')
           .Select(t => t.Trim())
           .Where(t => t.Length > 0)
           .ToList();

    public static string SubstituteTitle(string template, int firstYear, int lastYear) =>
        template
            .Replace("{firstYear}", firstYear.ToString())
            .Replace("{lastYear}",  lastYear.ToString());

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

    // A line in hashtags.txt may carry a weight — "#nostalgia 70%" — meaning the
    // tag appears in that share of posts instead of taking its chances in the
    // pool. Weighted lines sit out both the pinned set and the sample; each is
    // rolled on its own, and a winner takes one of the sampled slots so the tag
    // count per post does not drift.
    private static readonly System.Text.RegularExpressions.Regex WeightedTag =
        new(@"^(?<tag>\S+)\s+(?<chance>\d{1,3})%$", System.Text.RegularExpressions.RegexOptions.Compiled);

    // The first three unweighted lines of hashtags.txt are pinned reach tags and
    // stay in file order; the rest is a pool we sample from so posts do not
    // repeat the same trailing tags every time. Reordering the file is how the
    // pinned set is changed, and a "NN%" suffix is how a tag is boosted — no
    // code edit needed for either.
    public static IReadOnlyList<string> SelectHashtags(IReadOnlyList<string> all)
    {
        var plain    = new List<string>(all.Count);
        var weighted = new List<(string Tag, int Chance)>();
        foreach (var line in all)
        {
            var m = WeightedTag.Match(line);
            if (m.Success)
                weighted.Add((m.Groups["tag"].Value, int.Parse(m.Groups["chance"].Value)));
            else
                plain.Add(line);
        }

        // Too short to split into pinned + sampled: hand back what there is
        // rather than throwing on a trimmed-down file.
        if (plain.Count <= PinnedCount + RandomCount)
            return plain.Concat(weighted.Select(w => w.Tag)).ToList();

        var selected = new List<string>(PinnedCount + RandomCount);
        selected.AddRange(plain.Take(PinnedCount));

        foreach (var (tag, chance) in weighted)
            if (Random.Shared.Next(100) < chance)
                selected.Add(tag);

        // Partial Fisher-Yates over the remainder: draws distinct entries
        // without shuffling or copying the whole pool. Weighted winners have
        // already taken their slots, so this tops the post back up to
        // PinnedCount + RandomCount and no further.
        var draws = Math.Min(PinnedCount + RandomCount - selected.Count, RandomCount);
        var pool  = plain.Skip(PinnedCount).ToArray();
        for (var i = 0; i < draws; i++)
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
