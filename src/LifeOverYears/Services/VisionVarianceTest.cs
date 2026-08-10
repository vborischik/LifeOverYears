using System.Text;
using System.Text.Json;
using Autofac;
using LifeOverYears.Models;
using LifeOverYears.Services.Interfaces;
using Microsoft.Extensions.Logging;

namespace LifeOverYears.Services;

// Runs the real vision path over a folder of photos and reports how much each
// SceneDna field actually varies between them. A field that comes back identical
// for every photo carries no information about the specific place — those fields
// are why different sources render as the same scene.
public static class VisionVarianceTest
{
    private static readonly string[] ImageExtensions = [".png", ".jpg", ".jpeg", ".webp"];

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static async Task<int> RunAsync(
        string[] args, string launchDir, IContainer container, ILoggerFactory loggerFactory)
    {
        var logger = loggerFactory.CreateLogger("VisionVariance");

        if (args.Length < 1)
        {
            logger.LogError("usage: vision-variance <folder> [--repeat N]");
            return 1;
        }

        var repeats = 1;
        var repeatAt = Array.FindIndex(args, a => a == "--repeat");
        if (repeatAt >= 0 &&
            (repeatAt + 1 >= args.Length ||
             !int.TryParse(args[repeatAt + 1], out repeats) || repeats < 1))
        {
            logger.LogError("--repeat needs a positive integer, e.g. --repeat 2");
            return 1;
        }

        var folder = Path.GetFullPath(args[0], launchDir);
        if (!Directory.Exists(folder))
        {
            logger.LogError("Folder not found: {Folder}", folder);
            return 1;
        }

        var images = Directory.EnumerateFiles(folder)
            .Where(f => ImageExtensions.Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase))
            .OrderBy(f => Path.GetFileName(f), StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (images.Count < 2)
        {
            logger.LogError("Need at least 2 images to compare, found {Count} in {Folder}",
                images.Count, folder);
            return 1;
        }

        var outDir = Path.Combine("output", "vision-variance",
            DateTimeOffset.Now.ToString("yyyyMMdd-HHmm"));
        Directory.CreateDirectory(outDir);

        var vision  = container.Resolve<IVisionService>();
        var results = new List<(string Name, List<SceneDna> Runs)>();
        var failed  = new List<string>();

        for (var i = 0; i < images.Count; i++)
        {
            var path = images[i];
            var name = Path.GetFileNameWithoutExtension(path);
            var runs = new List<SceneDna>();

            for (var r = 1; r <= repeats; r++)
            {
                logger.LogInformation("Analyzing {Name} ({Index}/{Total}, repeat {Repeat}/{Repeats})",
                    name, i + 1, images.Count, r, repeats);
                try
                {
                    var dna = await vision.AnalyzeAsync(path);
                    runs.Add(dna);
                    var suffix = repeats > 1 ? $"_r{r}" : string.Empty;
                    await File.WriteAllTextAsync(
                        Path.Combine(outDir, $"{name}{suffix}.json"),
                        JsonSerializer.Serialize(dna, JsonOpts));
                }
                catch (Exception ex)
                {
                    // One bad photo must not cost the whole comparison.
                    logger.LogError(ex, "Vision failed for {Name} (repeat {Repeat})", name, r);
                }
            }

            if (runs.Count > 0) results.Add((name, runs));
            else failed.Add(name);
        }

        if (results.Count < 2)
        {
            logger.LogError("Fewer than 2 photos produced a SceneDna — nothing to compare");
            return 1;
        }

        var report = BuildReport(results, repeats, failed);
        Console.WriteLine();
        Console.WriteLine(report);

        var reportPath = Path.Combine(outDir, "report.txt");
        await File.WriteAllTextAsync(reportPath, report, Encoding.UTF8);
        logger.LogInformation("Report written to {Path}", reportPath);
        return 0;
    }

    private static string BuildReport(
        List<(string Name, List<SceneDna> Runs)> results, int repeats, List<string> failed)
    {
        var flat = results
            .Select(r => (r.Name, Runs: r.Runs.Select(Flatten).ToList()))
            .ToList();

        var keys  = flat[0].Runs[0].Keys.ToList();
        var width = keys.Max(k => k.Length);
        var sb    = new StringBuilder();

        sb.AppendLine($"VISION VARIANCE — {flat.Count} photos, {repeats} run(s) each");
        sb.AppendLine($"photos: {string.Join(", ", flat.Select(p => p.Name))}");
        if (failed.Count > 0)
            sb.AppendLine($"failed entirely: {string.Join(", ", failed)}");
        sb.AppendLine();

        // The first run of each photo is the comparison set; repeats only feed
        // the jitter section below.
        var rows = keys
            .Select(k =>
            {
                var vals = flat.Select(p => p.Runs[0][k]).ToList();
                var distinct = vals.Distinct(StringComparer.OrdinalIgnoreCase).Count();
                return (Key: k, Distinct: distinct, Collapsed: distinct == 1, Values: vals);
            })
            .OrderBy(r => r.Collapsed ? 0 : 1)
            .ThenBy(r => r.Key, StringComparer.Ordinal)
            .ToList();

        foreach (var r in rows)
            sb.AppendLine($"{(r.Collapsed ? "COLLAPSED " : "          ")}" +
                          $"{r.Key.PadRight(width)}  {r.Distinct} of {flat.Count}   " +
                          $"{string.Join(" | ", r.Values)}");

        sb.AppendLine();
        sb.AppendLine($"{rows.Count(r => r.Collapsed)} of {rows.Count} fields identical across all photos");

        // distinctive is free text, so it is measured by overlap rather than by
        // distinct-value count: a phrase shared by two places is generic.
        sb.AppendLine();
        sb.AppendLine("DISTINCTIVE");

        var phrases = results.ToDictionary(
            r => r.Name,
            r => (r.Runs[0].Distinctive ?? [])
                .Select(p => p.Trim()).Where(p => p.Length > 0).ToList());

        sb.AppendLine($"  {phrases.Values.Sum(v => v.Count)} phrases total, " +
                      $"{phrases.Values.SelectMany(v => v).Distinct(StringComparer.OrdinalIgnoreCase).Count()} unique");

        var empty = phrases.Where(kv => kv.Value.Count == 0).Select(kv => kv.Key).ToList();
        if (empty.Count > 0)
            sb.AppendLine($"  EMPTY — no distinctive phrases returned for: {string.Join(", ", empty)}");

        var shared = phrases.Values
            .SelectMany(v => v.Distinct(StringComparer.OrdinalIgnoreCase))
            .GroupBy(p => p, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .OrderByDescending(g => g.Count())
            .ToList();

        if (shared.Count == 0)
            sb.AppendLine("  no phrase is shared between photos");
        else
        {
            sb.AppendLine("  SHARED between photos — generic, each should describe one place only:");
            foreach (var g in shared)
                sb.AppendLine($"    ({g.Count()}x) {g.Key}");
        }

        foreach (var (name, list) in phrases)
        {
            sb.AppendLine($"  {name}:");
            foreach (var p in list) sb.AppendLine($"    - {p}");
        }

        // Without this a reader cannot tell a schema that erases differences from
        // a model that simply answers differently each time.
        if (repeats > 1)
        {
            sb.AppendLine();
            sb.AppendLine("MODEL JITTER (same photo, repeated)");

            var jitter = keys
                .Select(k => (Key: k, Unstable: flat.Count(p =>
                    p.Runs.Count > 1 &&
                    p.Runs.Select(run => run[k]).Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1)))
                .Where(x => x.Unstable > 0)
                .OrderByDescending(x => x.Unstable)
                .ToList();

            if (jitter.Count == 0)
                sb.AppendLine("  every field was stable across repeats");
            else
                foreach (var j in jitter)
                    sb.AppendLine($"  {j.Key.PadRight(width)}  differed across repeats for " +
                                  $"{j.Unstable} of {flat.Count} photos");
        }

        return sb.ToString();
    }

    private static Dictionary<string, string> Flatten(SceneDna s)
    {
        var geo  = s.Geometry;
        var env  = s.Environment;
        var road = geo.Roads.Count > 0 ? geo.Roads[0] : null;
        var b    = geo.Buildings.Count > 0 ? geo.Buildings[0] : null;

        return new Dictionary<string, string>
        {
            ["scene_type"]                      = V(s.SceneType),
            ["camera.height"]                   = V(s.Camera.Height),
            ["camera.direction"]                = V(s.Camera.Direction),
            ["camera.fov"]                      = s.Camera.Fov.ToString(),
            ["composition.subject_distance"]    = V(s.Composition?.SubjectDistance),
            ["composition.subject_frame_share"] = V(s.Composition?.FrameShare),
            ["composition.horizon"]             = V(s.Composition?.Horizon),
            ["geometry.sidewalks"]              = geo.Sidewalks ? "true" : "false",
            ["geometry.curbs"]                  = geo.Curbs ? "true" : "false",
            ["geometry.parking"]                = V(geo.Parking),
            ["geometry.driveways"]              = V(geo.Driveways),
            ["geometry.roads.count"]            = geo.Roads.Count.ToString(),
            ["road[0].type"]                    = V(road?.Type),
            ["road[0].lanes"]                   = road is null ? "(none)" : road.Lanes.ToString(),
            ["road[0].surface"]                 = V(road?.Surface),
            ["road[0].markings"]                = road is null ? "(none)" : V(road.Markings),
            ["geometry.buildings.count"]        = geo.Buildings.Count.ToString(),
            ["building[0].type"]                = V(b?.Type),
            ["building[0].position"]            = V(b?.Position),
            ["building[0].stories"]             = b is null ? "(none)" : b.Stories.ToString(),
            ["building[0].materials"]           = b is null ? "(none)" : V(b.Materials),
            ["building[0].roof"]                = V(b?.Roof),
            ["building[0].setback"]             = V(b?.Setback),
            ["environment.terrain"]             = V(env.Terrain),
            ["environment.utilities"]           = V(env.Utilities),
            ["environment.landscape"]           = V(env.Landscape),
            ["environment.trees.count"]         = env.Trees.Count.ToString(),
            ["immutable_elements"]              = V(s.ImmutableElements),
        };
    }

    private static string V(string? s) =>
        string.IsNullOrWhiteSpace(s) ? "(none)" : s.Trim();

    private static string V(IReadOnlyList<string>? list) =>
        list is null || list.Count == 0 ? "(none)" : string.Join(", ", list);
}
