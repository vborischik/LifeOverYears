// TODO: remove smoke test
using System.Text;
using System.Text.Json;
using LifeOverYears.Providers;
using LifeOverYears.Services.Interfaces;
using Microsoft.Extensions.Logging;

namespace LifeOverYears.Services;

// TODO: remove smoke test
// Isolated smoke test for the OpenAI batch submit/collect path. Drives the real
// OpenAiBatchImageProvider against a fake IOpenAiProvider, so every check
// exercises the shipping implementation without touching the network or an API
// key. Job state and input files land in a temp sandbox that is deleted at the
// end — nothing under output/ is written except the report.
public static class BatchSmokeTest
{
    public static async Task<int> RunAsync(ILoggerFactory loggerFactory, ILogger logger)
    {
        logger.LogInformation("[SmokeBatch] BatchSmokeTest starting");

        var findings = new List<(string Id, string Desc, bool Pass, string Detail)>();
        var sandbox = Path.Combine(Path.GetTempPath(), "loy-batch-smoke-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(sandbox);

        try
        {
            await DoB1(loggerFactory, sandbox, findings);
            await DoB2(loggerFactory, sandbox, findings);
            await DoB3(loggerFactory, sandbox, findings);
            await DoB4(loggerFactory, sandbox, findings);
            await DoB5(loggerFactory, sandbox, findings);
            await DoB6(loggerFactory, sandbox, findings);
            await DoB7(loggerFactory, sandbox, findings);
            await DoB8(loggerFactory, sandbox, findings);
            await DoB9(loggerFactory, sandbox, findings);
        }
        finally
        {
            try { Directory.Delete(sandbox, recursive: true); } catch { /* temp dir, best effort */ }
        }

        await WriteReport(findings, logger);

        var passed = findings.Count(f => f.Pass);
        var total  = findings.Count;
        Console.WriteLine();
        Console.WriteLine($"Batch smoke test: {passed}/{total} checks passed" +
                          (passed == total ? "" : " — FAILURES DETECTED"));
        Console.WriteLine("See output/smoke-batch/report.md for full details.");
        logger.LogInformation("[SmokeBatch] Done: {Passed}/{Total} checks passed", passed, total);

        return passed == total ? 0 : 1;
    }

    // ── Checks ────────────────────────────────────────────────────────────────

    // Submit is self-contained: it writes this year's one-line input file and
    // persists the real batch id, so nothing later has to guess when the run
    // finished submitting.
    private static async Task DoB1(
        ILoggerFactory lf, string sandbox, List<(string, string, bool, string)> f)
    {
        var (provider, fake, jobsDir) = NewCase(lf, sandbox, nameof(DoB1));
        var basePath = await WriteBaseAsync(jobsDir, "base.png");

        await provider.SubmitEraAsync(basePath, "prompt for 1975", 1975, jobsDir);

        var errs = new List<string>();

        var inputPath = Path.Combine(jobsDir, "batch-input-1975.jsonl");
        if (!File.Exists(inputPath))
            errs.Add("no batch-input-1975.jsonl written");
        else
        {
            var lines = (await File.ReadAllLinesAsync(inputPath))
                .Where(l => l.Trim().Length > 0).ToList();
            if (lines.Count != 1)
                errs.Add($"input file has {lines.Count} lines, expected exactly 1");
            else
            {
                using var doc = JsonDocument.Parse(lines[0]);
                var root = doc.RootElement;
                if (root.GetProperty("custom_id").GetString() != "era-1975")
                    errs.Add($"custom_id is '{root.GetProperty("custom_id").GetString()}', expected 'era-1975'");
                if (root.GetProperty("url").GetString() != "/v1/images/edits")
                    errs.Add("url is not /v1/images/edits");
                var body = root.GetProperty("body");
                if (body.GetProperty("prompt").GetString() != "prompt for 1975")
                    errs.Add("prompt not carried into the batch body");
                if (!body.GetProperty("image").GetString()!.StartsWith("file-", StringComparison.Ordinal))
                    errs.Add("image is not an uploaded file id");
            }
        }

        var job = await ReadJobAsync(jobsDir, 1975);
        if (job is null)
            errs.Add("no 1975.json job state written");
        else
        {
            if (fake.CreatedBatches.Count != 1)
                errs.Add($"{fake.CreatedBatches.Count} batches created, expected 1");
            else if (job.Value.JobId != fake.CreatedBatches[0])
                errs.Add($"jobId '{job.Value.JobId}' is not the created batch id '{fake.CreatedBatches[0]}'");
            if (job.Value.Provider != "openai-batch")
                errs.Add($"provider is '{job.Value.Provider}'");
        }

        Add(f, "B1", "SubmitEraAsync writes a one-line per-era input file and persists the real batch id", errs,
            "input file, custom_id, prompt, base file id and job state all correct");
    }

    // A year that already has a live batch must not be submitted twice: the job
    // file holds one id, so a second batch would be paid for and never collected.
    private static async Task DoB2(
        ILoggerFactory lf, string sandbox, List<(string, string, bool, string)> f)
    {
        var (provider, fake, jobsDir) = NewCase(lf, sandbox, nameof(DoB2));
        var basePath = await WriteBaseAsync(jobsDir, "base.png");

        await provider.SubmitEraAsync(basePath, "prompt", 1985, jobsDir);
        var firstBatchId = (await ReadJobAsync(jobsDir, 1985))!.Value.JobId;
        await provider.SubmitEraAsync(basePath, "prompt", 1985, jobsDir);

        var errs = new List<string>();
        if (fake.CreatedBatches.Count != 1)
            errs.Add($"re-submit created {fake.CreatedBatches.Count} batches, expected 1");
        var afterJobId = (await ReadJobAsync(jobsDir, 1985))!.Value.JobId;
        if (afterJobId != firstBatchId)
            errs.Add($"jobId changed on re-submit: '{firstBatchId}' -> '{afterJobId}'");

        Add(f, "B2", "Re-submitting a year with a live batch reuses it instead of creating a second", errs,
            $"one batch ({firstBatchId}) across two SubmitEraAsync calls");
    }

    // The regression that per-era batches exist for: in chained mode year N+1 is
    // submitted only after year N has been collected, so a single shared batch
    // would have been created over year N alone and year N+1 would never appear
    // in its output.
    private static async Task DoB3(
        ILoggerFactory lf, string sandbox, List<(string, string, bool, string)> f)
    {
        var (provider, fake, jobsDir) = NewCase(lf, sandbox, nameof(DoB3));
        var errs = new List<string>();

        // Era 1: submit, complete, collect — exactly the chained sequence.
        var base1 = await WriteBaseAsync(jobsDir, "base_synthetic.png");
        await provider.SubmitEraAsync(base1, "prompt 1975", 1975, jobsDir);
        var batch1975 = (await ReadJobAsync(jobsDir, 1975))!.Value.JobId;
        fake.CompleteBatch(batch1975, OutputLine("era-1975", "AAAA"));

        var out1975 = Path.Combine(jobsDir, "1975.png");
        if (!await provider.TryCollectAsync(jobsDir, 1975, out1975))
            errs.Add("1975 did not collect after its batch completed");

        // Era 2 chains from era 1's finished image — a different base file.
        await provider.SubmitEraAsync(out1975, "prompt 1985", 1985, jobsDir);
        var batch1985 = (await ReadJobAsync(jobsDir, 1985))!.Value.JobId;
        if (batch1985 == batch1975)
            errs.Add("1985 reused 1975's batch id");
        fake.CompleteBatch(batch1985, OutputLine("era-1985", "BBBB"));

        var out1985 = Path.Combine(jobsDir, "1985.png");
        try
        {
            if (!await provider.TryCollectAsync(jobsDir, 1985, out1985))
                errs.Add("1985 did not collect after its batch completed");
        }
        catch (Exception ex)
        {
            errs.Add($"1985 collect threw: {ex.Message}");
        }

        if (errs.Count == 0 && !File.Exists(out1985))
            errs.Add("1985.png was not written");
        if (fake.CreatedBatches.Count != 2)
            errs.Add($"{fake.CreatedBatches.Count} batches created across two chained eras, expected 2");

        // Each era uploads its own base, since chaining changes the base image.
        var baseUploads = fake.Calls.Count(c => c.StartsWith("upload:vision:", StringComparison.Ordinal));
        if (baseUploads != 2)
            errs.Add($"{baseUploads} base images uploaded, expected 2 (one per chained era)");

        Add(f, "B3", "Chained mode: each era gets its own batch and collects independently", errs,
            "two eras submitted at different times, two batches, both collected");
    }

    // Non-terminal statuses are "not ready", never an error.
    private static async Task DoB4(
        ILoggerFactory lf, string sandbox, List<(string, string, bool, string)> f)
    {
        var errs = new List<string>();
        foreach (var status in new[] { "validating", "in_progress", "finalizing" })
        {
            var (provider, fake, jobsDir) = NewCase(lf, sandbox, nameof(DoB4) + status);
            var basePath = await WriteBaseAsync(jobsDir, "base.png");
            await provider.SubmitEraAsync(basePath, "prompt", 1995, jobsDir);
            fake.SetStatus((await ReadJobAsync(jobsDir, 1995))!.Value.JobId, status, null, null);

            try
            {
                if (await provider.TryCollectAsync(jobsDir, 1995, Path.Combine(jobsDir, "1995.png")))
                    errs.Add($"'{status}' reported the year as collected");
            }
            catch (Exception ex)
            {
                errs.Add($"'{status}' threw instead of reporting pending: {ex.Message}");
            }
        }

        Add(f, "B4", "Pending batch statuses report not-ready rather than throwing", errs,
            "validating, in_progress and finalizing all return false");
    }

    // A dead batch must surface the provider's own error text, not a generic
    // failure — that text is the only diagnosis available after the fact.
    private static async Task DoB5(
        ILoggerFactory lf, string sandbox, List<(string, string, bool, string)> f)
    {
        var errs = new List<string>();
        foreach (var status in new[] { "failed", "expired", "cancelled" })
        {
            var (provider, fake, jobsDir) = NewCase(lf, sandbox, nameof(DoB5) + status);
            var basePath = await WriteBaseAsync(jobsDir, "base.png");
            await provider.SubmitEraAsync(basePath, "prompt", 2005, jobsDir);
            var batchId = (await ReadJobAsync(jobsDir, 2005))!.Value.JobId;
            var errorFileId = fake.AddFile("the specific provider complaint");
            fake.SetStatus(batchId, status, null, errorFileId);

            try
            {
                await provider.TryCollectAsync(jobsDir, 2005, Path.Combine(jobsDir, "2005.png"));
                errs.Add($"'{status}' did not throw");
            }
            catch (InvalidOperationException ex)
            {
                if (!ex.Message.Contains(status, StringComparison.Ordinal))
                    errs.Add($"'{status}' error text does not name the status: {ex.Message}");
                if (!ex.Message.Contains("the specific provider complaint", StringComparison.Ordinal))
                    errs.Add($"'{status}' error text does not include the error file content");
            }
        }

        Add(f, "B5", "Terminal failure statuses throw with the batch's own error-file text", errs,
            "failed, expired and cancelled all surface the provider complaint");
    }

    // Output lines are matched by custom_id, never by position.
    private static async Task DoB6(
        ILoggerFactory lf, string sandbox, List<(string, string, bool, string)> f)
    {
        var (provider, fake, jobsDir) = NewCase(lf, sandbox, nameof(DoB6));
        var basePath = await WriteBaseAsync(jobsDir, "base.png");
        await provider.SubmitEraAsync(basePath, "prompt", 2015, jobsDir);

        var payload = Convert.ToBase64String(Encoding.UTF8.GetBytes("the 2015 image bytes"));
        // Decoys first: a positional read would take the wrong line.
        fake.CompleteBatch((await ReadJobAsync(jobsDir, 2015))!.Value.JobId,
            OutputLine("era-1975", Convert.ToBase64String(Encoding.UTF8.GetBytes("wrong"))),
            OutputLine("era-2025", Convert.ToBase64String(Encoding.UTF8.GetBytes("also wrong"))),
            OutputLine("era-2015", payload));

        var errs = new List<string>();
        var outputPath = Path.Combine(jobsDir, "2015.png");
        if (!await provider.TryCollectAsync(jobsDir, 2015, outputPath))
            errs.Add("completed batch did not collect");
        else
        {
            var written = await File.ReadAllTextAsync(outputPath);
            if (written != "the 2015 image bytes")
                errs.Add($"wrote the wrong line's payload: '{written}'");
        }

        Add(f, "B6", "Completed batch decodes the line matching this year's custom_id, not the first line", errs,
            "correct payload chosen out of three out-of-order output lines");
    }

    // An unrecognised status is treated as pending: a wrong guess costs one more
    // poll, while throwing would abort a run over a status OpenAI added later.
    private static async Task DoB7(
        ILoggerFactory lf, string sandbox, List<(string, string, bool, string)> f)
    {
        var (provider, fake, jobsDir) = NewCase(lf, sandbox, nameof(DoB7));
        var basePath = await WriteBaseAsync(jobsDir, "base.png");
        await provider.SubmitEraAsync(basePath, "prompt", 2025, jobsDir);
        fake.SetStatus((await ReadJobAsync(jobsDir, 2025))!.Value.JobId, "some_new_status", null, null);

        var errs = new List<string>();
        try
        {
            if (await provider.TryCollectAsync(jobsDir, 2025, Path.Combine(jobsDir, "2025.png")))
                errs.Add("unknown status reported the year as collected");
        }
        catch (Exception ex)
        {
            errs.Add($"unknown status threw: {ex.Message}");
        }

        Add(f, "B7", "An unknown batch status is treated as pending rather than fatal", errs,
            "unrecognised status returned false");
    }

    // Collecting a year that was never submitted is a caller bug, and the message
    // has to say so — this is reachable from the standalone 'collect' CLI mode.
    private static async Task DoB8(
        ILoggerFactory lf, string sandbox, List<(string, string, bool, string)> f)
    {
        var (provider, _, jobsDir) = NewCase(lf, sandbox, nameof(DoB8));

        var errs = new List<string>();
        try
        {
            await provider.TryCollectAsync(jobsDir, 1975, Path.Combine(jobsDir, "1975.png"));
            errs.Add("collecting an unsubmitted year did not throw");
        }
        catch (InvalidOperationException ex)
        {
            if (!ex.Message.Contains("1975", StringComparison.Ordinal))
                errs.Add($"error text does not name the year: {ex.Message}");
        }

        Add(f, "B8", "Collecting a year that was never submitted throws naming that year", errs,
            "missing job state reported against the year");
    }

    // A year whose image is already on disk is done, whoever put it there — a
    // hand-dropped file must not cost an API call.
    private static async Task DoB9(
        ILoggerFactory lf, string sandbox, List<(string, string, bool, string)> f)
    {
        var (provider, fake, jobsDir) = NewCase(lf, sandbox, nameof(DoB9));
        var basePath = await WriteBaseAsync(jobsDir, "base.png");
        await provider.SubmitEraAsync(basePath, "prompt", 1975, jobsDir);

        var outputPath = Path.Combine(jobsDir, "1975.png");
        await File.WriteAllTextAsync(outputPath, "hand-dropped");
        var callsBefore = fake.Calls.Count;

        var errs = new List<string>();
        if (!await provider.TryCollectAsync(jobsDir, 1975, outputPath))
            errs.Add("an existing output file was not reported as collected");
        if (fake.Calls.Count != callsBefore)
            errs.Add($"made {fake.Calls.Count - callsBefore} API calls for an already-present image");
        if (await File.ReadAllTextAsync(outputPath) != "hand-dropped")
            errs.Add("the existing image was overwritten");

        Add(f, "B9", "An era image already on disk collects with no API call and is not overwritten", errs,
            "existing file honoured, zero provider calls");
    }

    // ── Harness ───────────────────────────────────────────────────────────────

    private static (OpenAiBatchImageProvider Provider, FakeOpenAi Fake, string JobsDir) NewCase(
        ILoggerFactory lf, string sandbox, string name)
    {
        var jobsDir = Path.Combine(sandbox, name);
        Directory.CreateDirectory(jobsDir);
        var fake = new FakeOpenAi();
        var provider = new OpenAiBatchImageProvider(
            fake, lf.CreateLogger<OpenAiBatchImageProvider>());
        return (provider, fake, jobsDir);
    }

    private static async Task<string> WriteBaseAsync(string jobsDir, string fileName)
    {
        var path = Path.Combine(jobsDir, fileName);
        await File.WriteAllBytesAsync(path, [0x89, 0x50, 0x4E, 0x47]);
        return path;
    }

    private static string OutputLine(string customId, string b64) =>
        JsonSerializer.Serialize(new
        {
            custom_id = customId,
            response = new { body = new { data = new[] { new { b64_json = b64 } } } }
        });

    // The provider's job file is private to it; the smoke test reads the same
    // JSON off disk rather than reaching into the type.
    private static async Task<(string JobId, string Provider)?> ReadJobAsync(string jobsDir, int year)
    {
        var path = Path.Combine(jobsDir, $"{year}.json");
        if (!File.Exists(path))
            return null;
        using var doc = JsonDocument.Parse(await File.ReadAllTextAsync(path));
        return (doc.RootElement.GetProperty("jobId").GetString()!,
                doc.RootElement.GetProperty("provider").GetString()!);
    }

    private static void Add(
        List<(string, string, bool, string)> f, string id, string desc,
        List<string> errs, string okDetail) =>
        f.Add((id, desc, errs.Count == 0, errs.Count == 0 ? okDetail : string.Join("; ", errs)));

    // In-memory stand-in for the HTTP connector: records every call, hands out
    // ids, and lets each check drive batch status directly.
    private sealed class FakeOpenAi : IOpenAiProvider
    {
        public readonly List<string> Calls = new();
        public readonly List<string> CreatedBatches = new();

        private readonly Dictionary<string, string> _files = new();
        private readonly Dictionary<string, (string Status, string? OutputFileId, string? ErrorFileId)> _batches = new();
        private int _ids;

        public string AddFile(string content)
        {
            var id = $"file-{++_ids}";
            _files[id] = content;
            return id;
        }

        public void SetStatus(string batchId, string status, string? outputFileId, string? errorFileId) =>
            _batches[batchId] = (status, outputFileId, errorFileId);

        public void CompleteBatch(string batchId, params string[] outputLines) =>
            SetStatus(batchId, "completed", AddFile(string.Join("\n", outputLines) + "\n"), null);

        public Task<string> UploadFileAsync(byte[] content, string fileName, string purpose, CancellationToken ct = default)
        {
            Calls.Add($"upload:{purpose}:{fileName}");
            var id = $"file-{++_ids}";
            _files[id] = Encoding.UTF8.GetString(content);
            return Task.FromResult(id);
        }

        public Task<string> CreateBatchAsync(string inputFileId, string endpoint, CancellationToken ct = default)
        {
            Calls.Add($"createBatch:{inputFileId}:{endpoint}");
            var id = $"batch_{++_ids}";
            CreatedBatches.Add(id);
            // Default to in-flight; checks move it on when they want a result.
            _batches[id] = ("in_progress", null, null);
            return Task.FromResult(id);
        }

        public Task<(string Status, string? OutputFileId, string? ErrorFileId)> GetBatchAsync(
            string batchId, CancellationToken ct = default)
        {
            Calls.Add($"getBatch:{batchId}");
            if (!_batches.TryGetValue(batchId, out var state))
                throw new InvalidOperationException($"FakeOpenAi: unknown batch {batchId}");
            return Task.FromResult(state);
        }

        public Task<string> DownloadFileContentAsync(string fileId, CancellationToken ct = default)
        {
            Calls.Add($"download:{fileId}");
            if (!_files.TryGetValue(fileId, out var content))
                throw new InvalidOperationException($"FakeOpenAi: unknown file {fileId}");
            return Task.FromResult(content);
        }

        public Task<byte[]> EditImageAsync(byte[] referenceImage, string prompt, string size, string quality, CancellationToken ct = default)
        {
            Calls.Add("edit");
            return Task.FromResult<byte[]>([1, 2, 3]);
        }

        public Task<byte[]> GenerateImageAsync(string prompt, string size, string quality, CancellationToken ct = default)
        {
            Calls.Add("generate");
            return Task.FromResult<byte[]>([1, 2, 3]);
        }
    }

    // ── Report ────────────────────────────────────────────────────────────────

    private static async Task WriteReport(
        List<(string Id, string Desc, bool Pass, string Detail)> findings, ILogger logger)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Batch Smoke Test Report");
        sb.AppendLine();
        sb.AppendLine($"Generated: {DateTimeOffset.UtcNow:o}");
        sb.AppendLine();
        sb.AppendLine("## Check Results");
        sb.AppendLine();
        sb.AppendLine("| Check | Description | Status | Detail |");
        sb.AppendLine("|-------|-------------|--------|--------|");
        foreach (var (id, desc, pass, detail) in findings)
            sb.AppendLine($"| {id} | {desc} | {(pass ? "✅ PASS" : "❌ FAIL")} | {detail.Replace("|", "\\|")} |");
        sb.AppendLine();

        var outDir = Path.Combine("output", "smoke-batch");
        Directory.CreateDirectory(outDir);
        await File.WriteAllTextAsync(Path.Combine(outDir, "report.md"), sb.ToString());

        logger.LogInformation("[SmokeBatch] Check summary:");
        foreach (var (id, _, pass, detail) in findings)
            logger.LogInformation("[SmokeBatch]   {Id} {Status}: {Detail}", id, pass ? "PASS" : "FAIL", detail);
    }
}
