using System.Collections.Concurrent;
using System.Text.Json;
using LifeOverYears.Services.Interfaces;
using Microsoft.Extensions.Logging;

namespace LifeOverYears.Providers;

// Batch-mode Step 3 provider. Unlike OpenAiImageProvider, all state lives on
// disk under jobsDir rather than in an in-process Task, so a process can be
// killed between submit and collect (or during the wait) and TryCollectAsync
// still works correctly from a fresh process — the exact scenario the
// 'collect' CLI mode exists for. Trades per-year latency (the Batch API has
// up to a 24h completion window) for that resumability plus OpenAI's lower
// batch pricing.
//
// One batch per era, not one batch per run. A shared batch would have to be
// created at some point after every SubmitEraAsync had happened, which the
// provider cannot detect — and in chained mode it never happens at all, since
// each era is submitted only after the previous one's image exists. Per-era
// batches make submit self-contained: the batch is created inside
// SubmitEraAsync and its id is the job's real jobId. Non-chained runs submit
// all years back to back, so their batches still run concurrently and finish
// in the same wall-clock time a single combined batch would.
public sealed class OpenAiBatchImageProvider : IImageGenerationProvider
{
    private const string Size =  "720x1280";   
    private const string Quality = "medium";
    private const string Endpoint = "/v1/images/edits";

    private const string BaseFileFileName = "base-file.json";

    // Job ids written by the pre-per-era-batch format: a placeholder, never a
    // real batch id. Treated as "not submitted yet" so an old run folder
    // re-submits cleanly instead of polling an id that never existed.
    private const string LegacyPendingJobIdPrefix = "batch-pending-";

    private static string InputFileNameFor(int year) => $"batch-input-{year}.jsonl";

    private static readonly JsonSerializerOptions JobJson =
        new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private static readonly JsonSerializerOptions ReadJson =
        new() { PropertyNameCaseInsensitive = true };

    private readonly IOpenAiProvider _openai;
    private readonly ILogger<OpenAiBatchImageProvider> _logger;

    // outputFileId -> custom_id -> b64_json, so all six years reuse one
    // download+parse of the batch output file within this process.
    private readonly ConcurrentDictionary<string, Dictionary<string, string>> _parsedOutputs = new();

    // When true the base image travels inside the batch line as a base64 data
    // URL instead of being uploaded and referenced by id. Off by default: the
    // file id is the documented shape and keeps the .jsonl small. It exists
    // because OpenAI's Batch API has been failing to resolve uploaded file ids
    // since 19 Aug 2026 while the same files work in a direct call, and
    // inlining removes the reference the executor cannot follow. Whether the
    // endpoint accepts image_url in a batch body is exactly what this flag is
    // for finding out — an earlier attempt recorded it as rejected, and a
    // rejected batch is not billed, so the experiment is free.
    private readonly bool _inlineImage;

    public OpenAiBatchImageProvider(
        IOpenAiProvider openai, ILogger<OpenAiBatchImageProvider> logger, bool inlineImage = false)
    {
        _openai = openai;
        _logger = logger;
        _inlineImage = inlineImage;
    }

    public async Task SynthesizeBaseAsync(string prompt, string outputPath)
    {
        // Synchronous, same reasoning as CleanBaseAsync below: the pipeline
        // blocks on the base before any era submit, so batching this single
        // call would add a 24h window for no benefit.
        _logger.LogInformation("SynthesizeBase: generating from text ({Prompt} chars) via gpt-image-2",
            prompt.Length);
        var result = await _openai.GenerateImageAsync(prompt, Size, Quality);
        await File.WriteAllBytesAsync(outputPath, result);
        _logger.LogInformation("SynthesizeBase complete: {Output}", outputPath);
    }

    public async Task CleanBaseAsync(string sourcePath, string prompt, string outputPath)
    {
        // Synchronous, same as OpenAiImageProvider: the pipeline blocks on this
        // before any era submit, so batching the one clean-base call would add
        // a 24h window for no benefit.
        var source = await File.ReadAllBytesAsync(sourcePath);
        _logger.LogInformation("CleanBase: editing {Source} ({Prompt} chars) via gpt-image-2",
            sourcePath, prompt.Length);
        var result = await _openai.EditImageAsync(source, prompt, Size, Quality);
        await File.WriteAllBytesAsync(outputPath, result);
        _logger.LogInformation("CleanBase complete: {Output}", outputPath);
    }

    public async Task SubmitEraAsync(
        string basePath, string prompt, int year, string jobsDir,
        string? referenceImagePath = null)
    {
        Directory.CreateDirectory(jobsDir);

        // Re-submitting a year that already has a live batch would create a
        // second one and orphan the first — paid for, then never collected,
        // because the job file can only hold one id. Resume instead.
        if (await ReadJobAsync(jobsDir, year) is { } existing
            && !string.IsNullOrEmpty(existing.JobId)
            && !existing.JobId.StartsWith(LegacyPendingJobIdPrefix, StringComparison.Ordinal))
        {
            _logger.LogInformation("{Year} already submitted as batch {BatchId} — reusing it",
                year, existing.JobId);
            return;
        }

        // Either a reference the executor has to resolve, or the bytes themselves.
        object imageEntry;
        if (_inlineImage)
        {
            var bytes = await File.ReadAllBytesAsync(basePath);
            imageEntry = new { image_url = $"data:image/png;base64,{Convert.ToBase64String(bytes)}" };
            _logger.LogInformation(
                "Inlining base image {BasePath} into the batch line ({Kb} KB before base64, no file upload)",
                basePath, bytes.Length / 1024);
        }
        else
        {
            imageEntry = new { file_id = await GetOrUploadBaseFileAsync(basePath, jobsDir) };
        }

        // "images" is already an array, so a reference sheet is simply a second
        // entry, built the same way as the first and cached by its own path. It
        // goes after the base: the first image is the one being edited, and the
        // prompt refers to this one as the reference. A path that is not there
        // downgrades the request rather than failing it — the prompt still
        // states the logo in words, and losing a chained run over one absent PNG
        // costs far more than the sheet was worth.
        var images = new List<object> { imageEntry };
        if (referenceImagePath is not null)
        {
            if (!File.Exists(referenceImagePath))
            {
                _logger.LogWarning(
                    "Reference image for {Year} not found at {Path} — submitting without it; the prompt still states the logo in words",
                    year, referenceImagePath);
            }
            else if (_inlineImage)
            {
                var refBytes = await File.ReadAllBytesAsync(referenceImagePath);
                images.Add(new { image_url = $"data:image/png;base64,{Convert.ToBase64String(refBytes)}" });
            }
            else
            {
                images.Add(new { file_id = await GetOrUploadBaseFileAsync(referenceImagePath, jobsDir) });
            }
        }

        // custom_id is how the result is matched back to the year later — output
        // line order is not guaranteed to match input order, even in a one-line
        // batch.
        var line = JsonSerializer.Serialize(new
        {
            custom_id = $"era-{year}",
            method = "POST",
            url = Endpoint,
            body = new
            {
                model = "gpt-image-2",
                // On application/json — which every batch line is — the image is
                // "images": an array of objects, each one {"file_id": "..."}.
                // Both other spellings are rejected outright:
                //   image: "file-..."   -> Unknown parameter: 'image'. For
                //                          application/json use 'images' (array).
                //   images: ["file-..."] -> Invalid type for 'images[0]':
                //                          expected an object, got a string.
                // "type" and "url" inside the object are unknown parameters.
                // "file_id" is the documented key; "image_url" (a data URL) is
                // what OpenAi:BatchInlineImage sends instead, and was recorded
                // as rejected once — the flag is there to retest it cheaply.
                images = images.ToArray(),
                prompt,
                size = Size,
                quality = Quality
            }
        });

        // Written, not appended: the file holds exactly this year's one line, so
        // a re-submit after a failure replaces it rather than producing a batch
        // with a duplicate custom_id, which OpenAI rejects outright.
        var inputPath = Path.Combine(jobsDir, InputFileNameFor(year));
        await File.WriteAllTextAsync(inputPath, line + "\n");

        var inputFileId = await _openai.UploadFileAsync(
            await File.ReadAllBytesAsync(inputPath), InputFileNameFor(year), "batch");

        // An uploaded file reports "uploaded" before it reports "processed", and
        // nothing in the API stops a batch being created over one that has not
        // got there yet. Creating it anyway is how a batch ends up failing with
        // "Cannot find file …" over an id that plainly exists — the executor
        // looks for the file before it is resolvable. The upload and the create
        // call sat one second apart, so this costs nothing in the normal case
        // and is simply correct in the abnormal one.
        await WaitForFileAsync(inputFileId);

        var batchId = await _openai.CreateBatchAsync(inputFileId, Endpoint);

        await WriteJobAsync(jobsDir, new BatchJob(
            Year:        year,
            Provider:    "openai-batch",
            JobId:       batchId,
            Size:        Size,
            Quality:     Quality,
            SubmittedAt: DateTimeOffset.UtcNow.ToString("o")));

        _logger.LogInformation(
            "Submitted {Year} as batch {BatchId} over {InputFileId} (gpt-image-2, {Quality}, {Size})",
            year, batchId, inputFileId, Quality, Size);
    }

    // Polls until the file leaves "uploaded". Bounded and non-fatal: if it never
    // settles the batch is created anyway, because a slow status is a guess at
    // the cause and must not be the thing that stops a run.
    private async Task WaitForFileAsync(string fileId)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(FileReadyTimeoutSeconds);
        while (DateTimeOffset.UtcNow < deadline)
        {
            string status;
            try
            {
                status = await _openai.GetFileStatusAsync(fileId);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not read status of {FileId} — creating the batch anyway", fileId);
                return;
            }

            if (string.Equals(status, "processed", StringComparison.OrdinalIgnoreCase))
                return;

            if (string.Equals(status, "error", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(
                    $"OpenAI reports file {fileId} in state 'error' — it cannot be used as batch input");

            _logger.LogInformation("Waiting for {FileId} to be processed (currently '{Status}')", fileId, status);
            await Task.Delay(TimeSpan.FromSeconds(2));
        }

        _logger.LogWarning(
            "File {FileId} still not processed after {Seconds}s — creating the batch anyway",
            fileId, FileReadyTimeoutSeconds);
    }

    private const int FileReadyTimeoutSeconds = 30;

    // Uploads a base image once per distinct basePath and caches its file id in
    // jobsDir/base-file.json, keyed by the base image's full path — NOT once per
    // run. Chained mode generates a fresh base per era, so a single run can
    // submit several distinct base images; each one is uploaded exactly once
    // and reused only by the eras that share that same basePath.
    private async Task<string> GetOrUploadBaseFileAsync(string basePath, string jobsDir)
    {
        var cachePath = Path.Combine(jobsDir, BaseFileFileName);
        var key = Path.GetFullPath(basePath);

        var cache = File.Exists(cachePath)
            ? JsonSerializer.Deserialize<Dictionary<string, string>>(
                  await File.ReadAllTextAsync(cachePath), ReadJson)
              ?? new Dictionary<string, string>()
            : new Dictionary<string, string>();

        if (cache.TryGetValue(key, out var cachedFileId) && cachedFileId.Length > 0)
            return cachedFileId;

        var bytes = await File.ReadAllBytesAsync(basePath);
        var fileId = await _openai.UploadFileAsync(bytes, Path.GetFileName(basePath), "vision");
        await WaitForFileAsync(fileId);
        cache[key] = fileId;
        await File.WriteAllTextAsync(cachePath, JsonSerializer.Serialize(cache, JobJson));
        _logger.LogInformation("Uploaded base image {BasePath}, file id {FileId}", basePath, fileId);
        return fileId;
    }

    public async Task<bool> TryCollectAsync(string jobsDir, int year, string outputPath)
    {
        var job = await ReadJobAsync(jobsDir, year)
            ?? throw new InvalidOperationException(
                $"No job state for year {year} in {jobsDir} — was this year ever submitted?");

        if (File.Exists(outputPath))
            return true;

        if (string.IsNullOrEmpty(job.JobId)
            || job.JobId.StartsWith(LegacyPendingJobIdPrefix, StringComparison.Ordinal))
            throw new InvalidOperationException(
                $"Job state for year {year} in {jobsDir} carries no batch id ('{job.JobId}') — " +
                "it predates per-era batches; re-submit this run.");

        var (status, outputFileId, errorFileId) = await _openai.GetBatchAsync(job.JobId);

        switch (status)
        {
            case "validating":
            case "in_progress":
            case "finalizing":
                return false;

            case "failed":
            case "expired":
            case "cancelled":
                var errorText = errorFileId is not null
                    ? await _openai.DownloadFileContentAsync(errorFileId)
                    : null;
                // A file-access failure here is almost certainly not this run's
                // fault: OpenAI's Batch API has been failing to resolve file_id
                // references since 19 Aug 2026 (community thread 1391232), while
                // the same files work in a direct call. Say so, so the next
                // person does not spend an evening on the key and the payload.
                var accessProblem = errorText is not null &&
                    (errorText.Contains("does not have access", StringComparison.OrdinalIgnoreCase) ||
                     errorText.Contains("Cannot find file", StringComparison.OrdinalIgnoreCase) ||
                     errorText.Contains("authorize file access", StringComparison.OrdinalIgnoreCase));

                throw new InvalidOperationException(
                    $"OpenAI batch {job.JobId} ended with status '{status}'" +
                    (errorText is not null ? $": {errorText}" : "") +
                    (accessProblem
                        ? " — this is a file-reference failure on OpenAI's side, not a bad payload: the Batch API " +
                          "has been failing to resolve uploaded file ids since 19 Aug 2026. Retrying costs nothing " +
                          "(a failed batch is not billed); OpenAi:Mode=sync bypasses the Files API entirely."
                        : ""));

            case "completed":
                // Every line failed: OpenAI produces no output file at all, only
                // an error file. This is the ordinary shape of a bad payload —
                // a one-line batch that is rejected lands here, not in the
                // missing-custom_id branch below — so the complaint has to be
                // read here too.
                if (outputFileId is null)
                {
                    var onlyErrors = errorFileId is not null
                        ? ErrorFor($"era-{year}", await _openai.DownloadFileContentAsync(errorFileId))
                        : null;

                    throw new InvalidOperationException(onlyErrors is not null
                        ? $"OpenAI rejected era-{year}: {onlyErrors}"
                        : $"OpenAI batch {job.JobId} completed with no output_file_id" +
                          (errorFileId is null ? " and no error file" : ""));
                }

                if (!_parsedOutputs.TryGetValue(outputFileId, out var results))
                {
                    _logger.LogInformation("Downloading batch output {OutputFileId}", outputFileId);
                    var content = await _openai.DownloadFileContentAsync(outputFileId);
                    results = ParseBatchOutput(content);
                    _parsedOutputs[outputFileId] = results;
                }

                var customId = $"era-{year}";
                if (!results.TryGetValue(customId, out var b64))
                {
                    // A batch completes even when individual lines failed: those
                    // go to the error file, never the output file. Without this
                    // the only symptom is a missing custom_id, and the actual
                    // complaint (a rejected parameter, a moderation block) has to
                    // be fetched by hand from the dashboard.
                    var lineError = errorFileId is not null
                        ? ErrorFor(customId, await _openai.DownloadFileContentAsync(errorFileId))
                        : null;

                    throw new InvalidOperationException(lineError is not null
                        ? $"OpenAI rejected {customId}: {lineError}"
                        : $"No result for {customId} in batch output {outputFileId}" +
                          (errorFileId is null ? " and the batch reported no error file" : ""));
                }

                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath))!);
                await File.WriteAllBytesAsync(outputPath, Convert.FromBase64String(b64));
                _logger.LogInformation("Collected {Year} -> {Output}", year, outputPath);
                return true;

            default:
                _logger.LogWarning("Unknown batch status '{Status}' for {BatchId} — treating as pending",
                    status, job.JobId);
                return false;
        }
    }

    // The error file has the same shape as the output file: one line per failed
    // request, keyed by custom_id. Returns the provider's own message for this
    // year, or null if this year is not in there.
    private static string? ErrorFor(string customId, string jsonl)
    {
        foreach (var line in jsonl.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            JsonDocument doc;
            try { doc = JsonDocument.Parse(line); }
            catch (JsonException) { continue; }

            using (doc)
            {
                var root = doc.RootElement;
                if (!root.TryGetProperty("custom_id", out var id) || id.GetString() != customId)
                    continue;

                if (root.TryGetProperty("response", out var resp)
                    && resp.TryGetProperty("body", out var body)
                    && body.TryGetProperty("error", out var err)
                    && err.TryGetProperty("message", out var msg))
                    return msg.GetString();

                // Shape differed from the documented one — the raw line still
                // says more than "no result".
                return root.ToString();
            }
        }
        return null;
    }

    // Output line order is not guaranteed to match input order — always key
    // results by custom_id, never by position.
    private static Dictionary<string, string> ParseBatchOutput(string jsonl)
    {
        var results = new Dictionary<string, string>();
        foreach (var line in jsonl.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;
            var customId = root.GetProperty("custom_id").GetString()!;
            var body = root.GetProperty("response").GetProperty("body");
            var b64 = body.GetProperty("data")[0].GetProperty("b64_json").GetString()!;
            results[customId] = b64;
        }
        return results;
    }

    // {jobsDir}/{year}.json — the whole per-era state. JobId is the OpenAI batch
    // id, so a fresh process can collect with nothing else on disk.
    private static async Task<BatchJob?> ReadJobAsync(string jobsDir, int year)
    {
        var jobPath = Path.Combine(jobsDir, $"{year}.json");
        if (!File.Exists(jobPath))
            return null;
        return JsonSerializer.Deserialize<BatchJob>(await File.ReadAllTextAsync(jobPath), ReadJson)
            ?? throw new InvalidOperationException($"Could not read job state from {jobPath}");
    }

    private static Task WriteJobAsync(string jobsDir, BatchJob job) =>
        File.WriteAllTextAsync(
            Path.Combine(jobsDir, $"{job.Year}.json"),
            JsonSerializer.Serialize(job, JobJson));

    private sealed record BatchJob(
        int Year, string Provider, string JobId, string Size, string Quality, string SubmittedAt);
}
