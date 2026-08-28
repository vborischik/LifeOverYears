using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace LifeOverYears.Services;

// A second, shorter line of prompts, written beside the full ones.
//
// The full prompt is built for a model that will read 900 words and honour an
// exact count of nine people. Most will not. A short prompt says the same thing
// in a third of the length: fewer figures, plainer actions, no count it cannot
// hold, and physical description in place of anything that reads as a story
// about the people in the frame. That is what Meta's tools need, but nothing
// here is specific to them — the same version is the one to paste into any tool
// that mangles the long form, and the one to try first when a frame comes back
// as a different place entirely.
//
// Deliberately a rewriter, not a second builder. Everything that decides what a
// frame contains — which storefront, which car, which sign, how big the tree —
// already happened when the run's prompts were written, and duplicating that
// would give two versions of the arc that drift apart. This restates a finished
// prompt, so the two lines always describe the same scene.
//
// It touches nothing in the pipeline. Runs offline, costs nothing, and if its
// output is wrong the normal path is unaffected.
public static class ShortPromptWriter
{
    public const string OutputDirName = "short-prompts";

    public static async Task<int> RunAsync(string[] args, string launchDir, ILogger logger)
    {
        if (args.Length < 1)
        {
            logger.LogError("short-prompts requires a run folder: short-prompts <runFolder>");
            return 1;
        }

        var folder = Path.GetFullPath(args[0], launchDir);
        var promptsDir = Path.Combine(folder, "prompts");
        if (!Directory.Exists(promptsDir))
        {
            logger.LogError("short-prompts: no prompts/ in {Folder} — nothing to rewrite", folder);
            return 1;
        }

        var sources = Directory.GetFiles(promptsDir, "*.txt")
            .Where(p => Regex.IsMatch(Path.GetFileNameWithoutExtension(p), @"^\d{4}$"))
            .OrderBy(p => p)
            .ToList();

        if (sources.Count == 0)
        {
            logger.LogError("short-prompts: prompts/ holds no era files in {Folder}", folder);
            return 1;
        }

        var outDir = Path.Combine(folder, OutputDirName);
        Directory.CreateDirectory(outDir);

        foreach (var source in sources)
        {
            var year = Path.GetFileNameWithoutExtension(source);
            var rewritten = Rewrite(await File.ReadAllTextAsync(source));
            var target = Path.Combine(outDir, $"{year}.txt");
            await File.WriteAllTextAsync(target, rewritten);
            logger.LogInformation("{Year}: {Chars} chars -> {Target}", year, rewritten.Length, target);
        }

        logger.LogInformation(
            "short-prompts: {Count} prompts written to {OutDir} — paste one per frame, in year order",
            sources.Count, outDir);
        return 0;
    }

    // ── Rewrite ───────────────────────────────────────────────────────────────

    public static string Rewrite(string prompt)
    {
        var year      = Match(prompt, @"TRANSFORM TO (\d{4})") ?? "the target year";
        var condition = ConditionOf(prompt);

        var sb = new StringBuilder();
        sb.AppendLine("One continuous photograph, TRUE 9:16 vertical portrait. No panels, no grid, no split frames.");
        sb.AppendLine("Keep the uploaded photo's exact composition, camera angle, road and building geometry. Change only what is listed below.");
        sb.AppendLine();
        sb.AppendLine($"TRANSFORM TO {year} — condition: {condition}");

        var details = PeriodDetails(prompt);
        if (details.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("PERIOD DETAILS");
            foreach (var d in details)
                sb.AppendLine($"- {d}");
            sb.AppendLine("Place each detail where it plausibly belongs. If there is nowhere for one, leave it out. Nothing stands in the roadway.");
        }

        var signage = Section(prompt, "SIGNAGE RESTRICTION");
        if (signage.Length > 0)
        {
            sb.AppendLine();
            sb.AppendLine("SIGNAGE");
            sb.AppendLine(signage.Trim());
        }

        sb.AppendLine();
        sb.AppendLine("PEOPLE");
        sb.AppendLine(People(prompt, condition));

        sb.AppendLine();
        sb.AppendLine("VEHICLES");
        sb.AppendLine(Vehicles(prompt, year, condition));

        var trees = TreeLines(prompt);
        if (trees.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("TREES");
            foreach (var t in trees)
                sb.AppendLine($"- {t}");
        }

        var style = Style(prompt);
        if (style.Length > 0)
        {
            sb.AppendLine();
            sb.AppendLine("STYLE");
            sb.AppendLine(style);
        }

        sb.Append("Blur all faces and licence plates.");
        return sb.ToString();
    }

    // ── Condition ─────────────────────────────────────────────────────────────

    // "squatted" and "abandoned" carry a story about who is inside. A filter
    // reads that as a scene about homelessness and refuses it outright, and a
    // model that does render it renders the story rather than the building. The
    // physical state is the whole of what the frame needs either way.
    private static string ConditionOf(string prompt)
    {
        var raw = Match(prompt, @"CONDITION: (\w+)") ?? "";
        return raw.ToLowerInvariant() switch
        {
            "abandoned" or "squatted" => "closed and vacant — boarded windows, weeds through the pavement, weathered surfaces",
            "declining"               => "declining — faded paint, aging signage, little activity",
            "restored"                => "reopened and tidy — clean surfaces, the same building back in use",
            "new"                     => "newly built — pristine surfaces, new signage",
            "busy"                    => "busy — well kept, plenty of custom",
            _                         => "well kept — freshly painted, open for business",
        };
    }

    // ── People ────────────────────────────────────────────────────────────────

    private static readonly string[] NeutralActions =
    {
        "walking to a parked car",
        "standing near the entrance looking at the window",
        "carrying a bag towards the pavement",
        "a delivery driver checking a phone beside a van",
        "walking along the pavement past the frontage",
    };

    // Rebuilt from scratch rather than filtered. The source block names each
    // figure and what they are doing, and on a derelict era that description is
    // the part that gets refused; there is nothing to salvage line by line. A
    // count plus one plain action is also all most models hold together.
    private static string People(string prompt, string condition)
    {
        if (condition.StartsWith("closed and vacant", StringComparison.Ordinal))
            return "NO people anywhere. The place is empty.";

        if (prompt.Contains("NO people on foot anywhere", StringComparison.Ordinal))
            return "NO people on foot. Everyone in the scene is inside a moving vehicle.";

        if (prompt.Contains("NO people anywhere", StringComparison.Ordinal))
            return "NO people anywhere. The place is empty.";

        var stated = int.TryParse(Match(prompt, @"EXACTLY (\d+) people TOTAL"), out var n) ? n : 0;
        var packed = prompt.Contains("A DENSE CROWD", StringComparison.Ordinal);

        // Above three, an exact count stops being rendered as a count at all, so
        // ask for a small group instead of a number that will not be honoured.
        if (packed || stated > 3)
            return "2-3 people in the background as small figures, plus a few more further off, no detail on any face. " +
                   "All on the pavement or beside parked cars, never in the road. Faces blurred.";

        if (stated == 0)
            return "NO people anywhere. The place is empty.";

        var action = NeutralActions[Math.Abs(prompt.Length) % NeutralActions.Length];
        var noun = stated == 1 ? "1 person" : $"{stated} people";
        return $"{noun}, {action}. On the pavement, the lot apron or by the entrance — never in the road or a driving lane. Faces blurred.";
    }

    // ── Vehicles ──────────────────────────────────────────────────────────────

    // Capped at two. Beyond that the extra traffic gets invented and the model
    // years drift, and the model year is the one period cue a viewer reads.
    private static string Vehicles(string prompt, string year, string condition)
    {
        if (prompt.Contains("NO vehicles anywhere", StringComparison.Ordinal))
            return "NO vehicles anywhere.";

        var models = Regex.Matches(prompt, @"^- (\d{4}(?:-\d{4})? [^\n]+)$", RegexOptions.Multiline)
            .Select(m => m.Groups[1].Value.Trim())
            .Take(2)
            .ToList();

        var sb = new StringBuilder();
        if (models.Count == 0)
            sb.AppendLine($"1-2 period vehicles, {year} model year.");
        else
        {
            sb.AppendLine($"EXACTLY {models.Count} vehicle{(models.Count == 1 ? "" : "s")}, {year} model year, and no others:");
            foreach (var m in models)
                sb.AppendLine($"- {m}");
        }

        var derelict = prompt.Contains("old, worn", StringComparison.Ordinal)
                    || condition.StartsWith("closed and vacant", StringComparison.Ordinal);

        // "left standing here, not driven for a long time" reads as an
        // abandonment scene; the same car is describable by its condition alone.
        sb.Append(derelict
            ? "Worn and dirty, dulled paint, rust spots, one flat tyre. Parked, with gaps. Plates blurred."
            : prompt.Contains("travelling in its own lane", StringComparison.Ordinal)
                ? "Moving in their own lanes at highway speed, natural spacing, all heading the same way. None parked on the shoulder. Plates blurred."
                : "Parked, with gaps between them, none in a driving lane. Plates blurred.");
        return sb.ToString();
    }

    // ── Section helpers ───────────────────────────────────────────────────────

    private static List<string> PeriodDetails(string prompt)
    {
        var body = Section(prompt, "PERIOD DETAILS");
        var all = body.Split('\n')
            .Select(l => l.Trim())
            .Where(l => l.StartsWith("- ", StringComparison.Ordinal))
            .Select(l => l[2..].Trim())
            .Where(IsSafeDetail)
            .ToList();

        // The main sign is what dates the frame and what the arc turns on, so it
        // leads and is never the entry that falls off the end of the list.
        var mainSign = all.Where(d => d.StartsWith("main sign", StringComparison.OrdinalIgnoreCase)).ToList();
        return mainSign.Concat(all.Except(mainSign)).Take(5).ToList();
    }

    // Narrow on purpose. A liquor store is a shop like any other and its sign is
    // the whole point of the corner-shop arc, so the trade is never the problem
    // — people are. What gets refused is a person drinking or with nowhere to
    // be, so only that vocabulary is dropped and a storefront keeps its name.
    private static readonly string[] UnsafeWords =
    {
        "drinking", "drunk", "loiter", "homeless", "squat", "vagrant",
        "tent", "tarp", "bedding", "shopping cart", "belongings",
    };

    private static bool IsSafeDetail(string detail) =>
        !UnsafeWords.Any(w => detail.Contains(w, StringComparison.OrdinalIgnoreCase));

    private static List<string> TreeLines(string prompt)
    {
        var body = Section(prompt, "TREES");
        return body.Split('\n')
            .Select(l => l.Trim())
            .Where(l => l.StartsWith("- ", StringComparison.Ordinal))
            .Select(l => l[2..].Trim())
            .ToList();
    }

    private static string Style(string prompt)
    {
        var body = Section(prompt, "PHOTOGRAPHIC STYLE");
        var lines = body.Split('\n')
            .Select(l => l.Trim())
            .Where(l => l.Length > 0)
            .Take(4)
            .ToList();
        return string.Join(" ", lines);
    }

    // Everything between a heading and the next blank-line-separated heading.
    private static string Section(string prompt, string heading)
    {
        var start = prompt.IndexOf(heading + "\n", StringComparison.Ordinal);
        if (start < 0) return "";
        start += heading.Length + 1;
        var end = prompt.IndexOf("\n\n", start, StringComparison.Ordinal);
        return end < 0 ? prompt[start..] : prompt[start..end];
    }

    private static string? Match(string text, string pattern)
    {
        var m = Regex.Match(text, pattern);
        return m.Success ? m.Groups[1].Value : null;
    }
}
