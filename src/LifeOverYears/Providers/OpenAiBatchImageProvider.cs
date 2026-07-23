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
public sealed class OpenAiBatchImageProvider : IImageGenerationProvider
{
    private const string Size = "1024x1536";   // 2:3 portrait, non-experimental
    private const string Quality = "medium";
    private const string Endpoint = "/v1/images/edits";

    private const string BaseFileFileName = "base-file.json";
    private const string BatchInputFileName = "batch-input.jsonl";
    private const string BatchFileName = "batch.json";

    private static readonly JsonSerializerOptions JobJson =
        new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private static readonly JsonSerializerOptions ReadJson =
        new() { PropertyNameCaseInsensitive = true };

    private readonly IOpenAiProvider _openai;
    private readonly ILogger<OpenAiBatchImageProvider> _logger;

    // outputFileId -> custom_id -> b64_json, so all six years reuse one
    // download+parse of the batch output file within this process.
    private readonly ConcurrentDictionary<string, Dictionary<string, string>> _parsedOutputs = new();

    public OpenAiBatchImageProvider(IOpenAiProvider openai, ILogger<OpenAiBatchImageProvider> logger)
    {
        _openai = openai;
        _logger = logger;
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

    public async Task SubmitEraAsync(string basePath, string prompt, int year, string jobsDir)
    {
        Directory.CreateDirectory(jobsDir);

        var baseFileId = await GetOrUploadBaseFileAsync(basePath, jobsDir);

        // custom_id is how results are matched back to a year later — output
        // line order is not guaranteed to match input order.
        var line = JsonSerializer.Serialize(new
        {
            custom_id = $"era-{year}",
            method = "POST",
            url = Endpoint,
            body = new
            {
                model = "gpt-image-2",
                image = baseFileId,
                prompt,
                size = Size,
                quality = Quality
            }
        });
        await File.AppendAllTextAsync(Path.Combine(jobsDir, BatchInputFileName), line + "\n");

        var job = new
        {
            year,
            provider    = "openai-batch",
            jobId       = $"batch-pending-{year}",
            size        = Size,
            quality     = Quality,
            submittedAt = DateTimeOffset.UtcNow.ToString("o")
        };
        await File.WriteAllTextAsync(
            Path.Combine(jobsDir, $"{year}.json"),
            JsonSerializer.Serialize(job, JobJson));

        _logger.LogInformation("Queued {Year} for batch submission (gpt-image-2, {Quality}, {Size})",
            year, Quality, Size);
    }

    // Uploads the clean base image once per run and caches its file id in
    // jobsDir/base-file.json, so the years submitted after the first read it
    // back instead of re-uploading the same bytes.
    private async Task<string> GetOrUploadBaseFileAsync(string basePath, string jobsDir)
    {
        var cachePath = Path.Combine(jobsDir, BaseFileFileName);
        if (File.Exists(cachePath))
        {
            var cached = JsonSerializer.Deserialize<BaseFileCache>(
                await File.ReadAllTextAsync(cachePath), ReadJson);
            if (cached is { FileId.Length: > 0 })
                return cached.FileId;
        }

        var bytes = await File.ReadAllBytesAsync(basePath);
        var fileId = await _openai.UploadFileAsync(bytes, "base.png", "vision");
        await File.WriteAllTextAsync(cachePath,
            JsonSerializer.Serialize(new BaseFileCache(fileId), JobJson));
        _logger.LogInformation("Uploaded clean base image, file id {FileId}", fileId);
        return fileId;
    }

    public async Task<bool> TryCollectAsync(string jobsDir, int year, string outputPath)
    {
        var jobPath = Path.Combine(jobsDir, $"{year}.json");
        if (!File.Exists(jobPath))
            throw new InvalidOperationException(
                $"No job state for year {year} in {jobsDir} — was this year ever submitted?");

        if (File.Exists(outputPath))
            return true;

        var batchPath = Path.Combine(jobsDir, BatchFileName);
        if (!File.Exists(batchPath))
        {
            // First collect call for this run: every SubmitEraAsync has already
            // happened (Pipeline submits all years before it starts waiting),
            // so the input file is complete — create the batch now. The
            // provider intentionally didn't do this in SubmitEraAsync, since it
            // has no way to know how many years were still coming.
            var inputPath = Path.Combine(jobsDir, BatchInputFileName);
            var inputBytes = await File.ReadAllBytesAsync(inputPath);
            var inputFileId = await _openai.UploadFileAsync(inputBytes, "batch-input.jsonl", "batch");
            var batchId = await _openai.CreateBatchAsync(inputFileId, Endpoint);

            await File.WriteAllTextAsync(batchPath, JsonSerializer.Serialize(
                new BatchCache(batchId, DateTimeOffset.UtcNow.ToString("o")), JobJson));
            _logger.LogInformation("Created batch {BatchId} over {InputFileId}", batchId, inputFileId);
            return false;
        }

        var batchCache = JsonSerializer.Deserialize<BatchCache>(
            await File.ReadAllTextAsync(batchPath), ReadJson)
            ?? throw new InvalidOperationException($"Could not read batch state from {batchPath}");

        var (status, outputFileId, errorFileId) = await _openai.GetBatchAsync(batchCache.BatchId);

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
                throw new InvalidOperationException(
                    $"OpenAI batch {batchCache.BatchId} ended with status '{status}'" +
                    (errorText is not null ? $": {errorText}" : ""));

            case "completed":
                if (outputFileId is null)
                    throw new InvalidOperationException(
                        $"OpenAI batch {batchCache.BatchId} completed with no output_file_id");

                if (!_parsedOutputs.TryGetValue(outputFileId, out var results))
                {
                    _logger.LogInformation("Downloading batch output {OutputFileId}", outputFileId);
                    var content = await _openai.DownloadFileContentAsync(outputFileId);
                    results = ParseBatchOutput(content);
                    _parsedOutputs[outputFileId] = results;
                }

                var customId = $"era-{year}";
                if (!results.TryGetValue(customId, out var b64))
                    throw new InvalidOperationException(
                        $"No result for {customId} in batch output {outputFileId}");

                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath))!);
                await File.WriteAllBytesAsync(outputPath, Convert.FromBase64String(b64));
                _logger.LogInformation("Collected {Year} -> {Output}", year, outputPath);
                return true;

            default:
                _logger.LogWarning("Unknown batch status '{Status}' for {BatchId} — treating as pending",
                    status, batchCache.BatchId);
                return false;
        }
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

    private sealed record BaseFileCache(string FileId);
    private sealed record BatchCache(string BatchId, string CreatedAt);
}
