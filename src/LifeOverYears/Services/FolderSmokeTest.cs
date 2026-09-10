// TODO: remove smoke test
using System.Reflection;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace LifeOverYears.Services;

// TODO: remove smoke test
// Isolated smoke test for the configurable Pipeline folders and the
// retire-on-failure behavior: PipelineFolders.Resolve is called directly
// (it's public), while ResolvePhotoPaths and MoveProcessedPhoto are Program.cs
// top-level local functions, reached here via reflection so the checks
// exercise the real implementation rather than a reimplementation. Runs
// entirely against a temp sandbox — no appsettings, no DI container.
public static class FolderSmokeTest
{
    public static async Task<int> RunAsync(ILogger logger)
    {
        logger.LogInformation("[SmokeFolders] FolderSmokeTest starting");

        var findings = new List<(string Id, string Desc, bool Pass, string Detail)>();

        // F1 — defaults match pre-existing hardcoded behavior when the
        // Pipeline section has no folder keys.
        var defaultConfig = new ConfigurationBuilder()
            .AddInMemoryCollection(new[] { new KeyValuePair<string, string?>("Pipeline:BaseMode", "clean") })
            .Build();
        var defaults = PipelineFolders.Resolve(defaultConfig);
        var defaultsOk = defaults is { InputDir: "testImage", ProcessedDir: "processed", FailedDir: "failed", OutputDir: "output/runs" };
        findings.Add(("F1", "PipelineFolders.Resolve defaults with no folder keys set",
            defaultsOk, defaultsOk ? "matched pre-existing hardcoded paths" : $"got {defaults}"));

        // F2 — configured values are honored instead of the defaults.
        var overrideConfig = new ConfigurationBuilder()
            .AddInMemoryCollection(new[]
            {
                new KeyValuePair<string, string?>("Pipeline:InputDir", "in"),
                new KeyValuePair<string, string?>("Pipeline:ProcessedDir", "done"),
                new KeyValuePair<string, string?>("Pipeline:FailedDir", "bad"),
                new KeyValuePair<string, string?>("Pipeline:OutputDir", "out/runs"),
            })
            .Build();
        var overridden = PipelineFolders.Resolve(overrideConfig);
        var overrideOk = overridden is { InputDir: "in", ProcessedDir: "done", FailedDir: "bad", OutputDir: "out/runs" };
        findings.Add(("F2", "PipelineFolders.Resolve honors configured overrides",
            overrideOk, overrideOk ? "all four keys read back correctly" : $"got {overridden}"));

        // F3 — appsettings.example.json itself carries the four keys with the
        // spec'd default values, so the checked-in template stays correct.
        var examplePath = Path.Combine(FindProjectRoot(), "appsettings.example.json");
        string f3Detail;
        bool f3Ok;
        try
        {
            using var doc = JsonDocument.Parse(await File.ReadAllTextAsync(examplePath));
            var pipeline = doc.RootElement.GetProperty("Pipeline");
            f3Ok = pipeline.GetProperty("InputDir").GetString() == "testImage"
                && pipeline.GetProperty("ProcessedDir").GetString() == "processed"
                && pipeline.GetProperty("FailedDir").GetString() == "failed"
                && pipeline.GetProperty("OutputDir").GetString() == "output/runs";
            f3Detail = f3Ok ? "appsettings.example.json Pipeline section matches defaults" : "appsettings.example.json Pipeline section is missing or mismatched";
        }
        catch (Exception ex)
        {
            f3Ok = false;
            f3Detail = $"could not read/parse {examplePath}: {ex.Message}";
        }
        findings.Add(("F3", "appsettings.example.json Pipeline section has the four folder keys", f3Ok, f3Detail));

        // Reflection handles into Program.cs's private top-level local
        // functions (compiler-mangled names, e.g. "<<Main>$>g__ResolvePhotoPaths|0_4").
        var programType = typeof(Program);
        var resolvePhotoPaths = FindMethod(programType, "ResolvePhotoPaths");
        var moveProcessedPhoto = FindMethod(programType, "MoveProcessedPhoto");

        var sandbox = Directory.CreateTempSubdirectory("lifeoveryears-smoke-folders-");
        try
        {
            // F4 — every image in the configured InputDir is picked up, in name
            // order, and an explicit argument still means that one photo. Dropping
            // several in and getting one back is the behaviour this replaced, so
            // the count is the assertion, not just the path.
            var inputDir = Path.Combine(sandbox.FullName, "custom-input");
            Directory.CreateDirectory(inputDir);
            var seeded = new List<string>();
            // Mixed casing and extensions on purpose: a "*.jpg" pattern is
            // case-sensitive on a case-sensitive filesystem and would skip ".JPG".
            foreach (var name in new[] { "b_second.jpg", "a_first.JPG", "c_third.png", "notes.txt" })
            {
                var path = Path.Combine(inputDir, name);
                await File.WriteAllBytesAsync(path, new byte[] { 1, 2, 3 });
                if (!name.EndsWith(".txt", StringComparison.OrdinalIgnoreCase))
                    seeded.Add(path);
            }

            var resolved = ((IReadOnlyList<string>)resolvePhotoPaths.Invoke(
                null, new object[] { Array.Empty<string>(), sandbox.FullName, "custom-input" })!).ToList();
            var expected = seeded.OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase).ToList();

            var f4Errors = new List<string>();
            if (!resolved.SequenceEqual(expected))
                f4Errors.Add($"expected [{string.Join(", ", expected.Select(Path.GetFileName))}], " +
                             $"got [{string.Join(", ", resolved.Select(Path.GetFileName))}]");

            // An explicit path is still exactly that one photo, folder or no folder.
            var explicitOne = ((IReadOnlyList<string>)resolvePhotoPaths.Invoke(
                null, new object[] { new[] { "/tmp/given.jpg" }, sandbox.FullName, "custom-input" })!).ToList();
            if (explicitOne.Count != 1 || explicitOne[0] != "/tmp/given.jpg")
                f4Errors.Add($"an explicit path resolved to [{string.Join(", ", explicitOne)}] instead of just itself");

            findings.Add(("F4", "Every image in the configured InputDir is returned in name order, whatever the extension's casing; an explicit argument still means one photo",
                f4Errors.Count == 0,
                f4Errors.Count == 0
                    ? $"{resolved.Count} photos picked up in order ({string.Join(", ", resolved.Select(Path.GetFileName))}), .txt ignored"
                    : string.Join("; ", f4Errors)));

            // F5 — successful move lands in the configured ProcessedDir, dir auto-created.
            var successSource = Path.Combine(sandbox.FullName, "success.jpg");
            await File.WriteAllBytesAsync(successSource, new byte[] { 1 });
            moveProcessedPhoto.Invoke(null, new object?[] { successSource, sandbox.FullName, "custom-processed", logger });
            var successDest = Path.Combine(sandbox.FullName, "custom-processed", "success.jpg");
            var f5Ok = !File.Exists(successSource) && File.Exists(successDest);
            findings.Add(("F5", "MoveProcessedPhoto(result==0) moves into the configured ProcessedDir",
                f5Ok, f5Ok ? "moved and directory auto-created" : "photo not found at expected destination"));

            // F6 — failed-run move lands in the configured FailedDir, same auto-create behavior.
            var failSource = Path.Combine(sandbox.FullName, "failure.jpg");
            await File.WriteAllBytesAsync(failSource, new byte[] { 1 });
            moveProcessedPhoto.Invoke(null, new object?[] { failSource, sandbox.FullName, "custom-failed", logger });
            var failDest = Path.Combine(sandbox.FullName, "custom-failed", "failure.jpg");
            var f6Ok = !File.Exists(failSource) && File.Exists(failDest);
            findings.Add(("F6", "MoveProcessedPhoto(result!=0 or catch) moves into the configured FailedDir",
                f6Ok, f6Ok ? "moved and directory auto-created" : "photo not found at expected destination"));

            // F7 — name collision appends a timestamp instead of overwriting.
            var collisionDir = Path.Combine(sandbox.FullName, "custom-processed");
            var existingDest = Path.Combine(collisionDir, "dup.jpg");
            await File.WriteAllBytesAsync(existingDest, new byte[] { 9, 9, 9 });
            var collisionSource = Path.Combine(sandbox.FullName, "dup.jpg");
            await File.WriteAllBytesAsync(collisionSource, new byte[] { 1 });
            moveProcessedPhoto.Invoke(null, new object?[] { collisionSource, sandbox.FullName, "custom-processed", logger });
            var untouchedOriginal = (await File.ReadAllBytesAsync(existingDest)).SequenceEqual(new byte[] { 9, 9, 9 });
            var timestampedCopyExists = Directory.EnumerateFiles(collisionDir, "dup_*.jpg").Any();
            var f7Ok = untouchedOriginal && timestampedCopyExists && !File.Exists(collisionSource);
            findings.Add(("F7", "MoveProcessedPhoto appends a timestamp on name collision instead of overwriting",
                f7Ok, f7Ok ? "original preserved, timestamped copy created" : "collision was not handled safely"));

            // F8 — a move failure (source vanished) must not throw and must not affect the exit code.
            var missingSource = Path.Combine(sandbox.FullName, "does-not-exist.jpg");
            Exception? thrown = null;
            try
            {
                moveProcessedPhoto.Invoke(null, new object?[] { missingSource, sandbox.FullName, "custom-failed", logger });
            }
            catch (Exception ex)
            {
                thrown = ex;
            }
            var f8Ok = thrown is null;
            findings.Add(("F8", "MoveProcessedPhoto never throws when the move itself fails",
                f8Ok, f8Ok ? "no exception raised for a missing source" : $"threw {thrown}"));
        }
        finally
        {
            try { sandbox.Delete(recursive: true); } catch { /* best-effort sandbox cleanup */ }
        }

        await WriteReport(findings, logger);
        PrintSummary(findings);
        return findings.All(f => f.Pass) ? 0 : 1;
    }

    private static MethodInfo FindMethod(Type type, string containedName) =>
        type.GetMethods(BindingFlags.NonPublic | BindingFlags.Static)
            .SingleOrDefault(m => m.Name.Contains(containedName))
        ?? throw new InvalidOperationException($"Could not find a local function containing '{containedName}' on {type.FullName}");

    private static string FindProjectRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (dir.GetFiles("*.csproj").Length > 0)
                return dir.FullName;
            dir = dir.Parent;
        }
        throw new InvalidOperationException("Could not locate project root: no .csproj file found walking up from " + AppContext.BaseDirectory);
    }

    private static void PrintSummary(List<(string Id, string Desc, bool Pass, string Detail)> findings)
    {
        int passed = findings.Count(f => f.Pass);
        int total  = findings.Count;
        Console.WriteLine();
        Console.WriteLine($"Folder smoke test: {passed}/{total} checks passed" +
                          (passed == total ? "" : " — FAILURES DETECTED"));
        Console.WriteLine("See output/smoke-folders/report.md for full details.");
    }

    private static async Task WriteReport(List<(string Id, string Desc, bool Pass, string Detail)> findings, ILogger logger)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Folder Smoke Test Report");
        sb.AppendLine();
        sb.AppendLine($"Generated: {DateTimeOffset.UtcNow:o}");
        sb.AppendLine();
        sb.AppendLine("## Check Results");
        sb.AppendLine();
        sb.AppendLine("| Check | Description | Status | Detail |");
        sb.AppendLine("|-------|-------------|--------|--------|");
        foreach (var (id, desc, pass, detail) in findings)
        {
            var status = pass ? "✅ PASS" : "❌ FAIL";
            var safeDetail = detail.Replace("|", "\\|");
            sb.AppendLine($"| {id} | {desc} | {status} | {safeDetail} |");
        }
        sb.AppendLine();

        var outDir = Path.Combine("output", "smoke-folders");
        Directory.CreateDirectory(outDir);
        await File.WriteAllTextAsync(Path.Combine(outDir, "report.md"), sb.ToString());

        logger.LogInformation("[SmokeFolders] Check summary:");
        foreach (var (id, _, pass, detail) in findings)
            logger.LogInformation("[SmokeFolders]   {Id} {Status}: {Detail}",
                id, pass ? "PASS" : "FAIL", detail);
    }
}
