using System.Text.Json;
using LifeOverYears.Models;
using Microsoft.Extensions.Logging;

namespace LifeOverYears.Services;

// The on-review folder. A run is copied in when it is ready for a human,
// and its folder is deleted once the decision has been acted on — presence
// in the folder is the whole queue state, readable with `ls`. Only the files
// a publisher needs are copied (~5 MB), never the prompts, jobs or era
// images; the copy is a review item, not a second run.
//
// The outcome goes back to the ORIGINAL run folder as publish.json, so a
// run that has been through review is never enqueued a second time, and the
// record survives the queue folder's deletion.
public sealed class ReviewQueue
{
    public const string ReviewFileName = "review.json";

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    private readonly string _root;
    private readonly string? _runsDir;
    private readonly ILogger<ReviewQueue> _logger;

    // Off means the end-of-run hook does nothing. The explicit CLI enqueue
    // still works — a folder typed by hand is consent.
    public bool AutoEnqueue { get; }

    // runsDir is where the originals live, so a folder dropped into the
    // queue by hand can be matched back to its run by name.
    public ReviewQueue(string root, bool autoEnqueue, ILogger<ReviewQueue> logger, string? runsDir = null)
    {
        _root       = root;
        _runsDir    = runsDir;
        AutoEnqueue = autoEnqueue;
        _logger     = logger;
    }

    public string Root => _root;

    // The end-of-run hook. Never throws: a run that finished is not a failed
    // run because its review copy could not be made.
    public async Task TryEnqueueAfterRunAsync(string runFolder)
    {
        if (!AutoEnqueue) return;
        try
        {
            await EnqueueAsync(runFolder);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Run finished but could not be queued for review: {Run}", runFolder);
        }
    }

    // Copies the run's publishable files into on-review/{name}/ and writes
    // review.json beside them. Idempotent: an item already queued, or a run
    // that already carries a decision, is left alone.
    public async Task<ReviewItem?> EnqueueAsync(string runFolder)
    {
        runFolder = Path.GetFullPath(runFolder);
        if (!RunPublishSource.IsPublishable(runFolder))
            throw new InvalidOperationException(
                $"Run is not publishable — needs {string.Join(", ", RunPublishSource.RequiredRelativePaths)}: {runFolder}");
        if (RunPublishSource.HasDecision(runFolder))
        {
            _logger.LogInformation("Run already has a publish decision, not queuing: {Run}", runFolder);
            return null;
        }

        var id  = Path.GetFileName(runFolder.TrimEnd(Path.DirectorySeparatorChar));
        var dir = Path.Combine(_root, id);
        if (Directory.Exists(dir))
        {
            _logger.LogInformation("Already queued for review: {Id}", id);
            return await ReadItemAsync(dir);
        }

        Directory.CreateDirectory(dir);
        var paths = RunPublishSource.RequiredRelativePaths.ToList();
        if (RunPublishSource.CoverRelativePath(runFolder) is { } cover)
            paths.Add(cover);
        foreach (var rel in paths)
        {
            var target = Path.Combine(dir, rel);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(Path.Combine(runFolder, rel), target, overwrite: true);
        }

        var item = new ReviewItem(id, runFolder, null, DateTimeOffset.UtcNow.ToString("o"));
        await WriteItemAsync(item);
        _logger.LogInformation("Queued for review: {Id} ← {Run}", id, runFolder);
        return item;
    }

    // Oldest first — the order the runs finished in. A folder that was put
    // here by hand — a whole run copied in, no review.json — is adopted on
    // sight: the queue is a folder people can drop things into, and a loop
    // that only honoured its own bookkeeping would sit idle over a video
    // somebody plainly wanted reviewed. The original is the run of the same
    // name under runs/ when it exists, otherwise the dropped folder itself.
    public async Task<IReadOnlyList<ReviewItem>> ListAsync()
    {
        if (!Directory.Exists(_root)) return Array.Empty<ReviewItem>();
        var items = new List<ReviewItem>();
        foreach (var dir in Directory.EnumerateDirectories(_root).Order(StringComparer.Ordinal))
        {
            var item = await ReadItemAsync(dir);
            if (item is null && RunPublishSource.IsPublishable(dir))
            {
                var name     = Path.GetFileName(dir);
                var original = _runsDir is not null && Directory.Exists(Path.Combine(_runsDir, name))
                    ? Path.Combine(_runsDir, name)
                    : dir;
                item = new ReviewItem(name, Path.GetFullPath(original), null, DateTimeOffset.UtcNow.ToString("o"));
                await WriteItemAsync(item);
                _logger.LogInformation("Adopted a folder placed in the queue by hand: {Id} (original: {Run})", name, original);
            }
            if (item is not null) items.Add(item);
        }
        return items;
    }

    public string ItemFolder(ReviewItem item) => Path.Combine(_root, item.Id);

    public Task MarkSentAsync(ReviewItem item, long messageId) =>
        WriteItemAsync(item with { ReviewMessageId = messageId });

    // Records the outcome on the original run and removes the review copy.
    // The record is written first: if the delete fails the worst case is a
    // stale copy that the next scan sees as already decided and drops.
    public async Task CompleteAsync(ReviewItem item, PublishState state)
    {
        var record = Path.Combine(item.RunFolder, RunPublishSource.PublishFileName);
        if (Directory.Exists(item.RunFolder))
            await File.WriteAllTextAsync(record, JsonSerializer.Serialize(state, Json));
        else
            _logger.LogWarning("Original run folder is gone; decision recorded only in the log: {Run} → {Status}",
                item.RunFolder, state.Status);

        var dir = ItemFolder(item);
        if (Directory.Exists(dir))
            Directory.Delete(dir, recursive: true);
        _logger.LogInformation("Review complete: {Id} → {Status}", item.Id, state.Status);
    }

    private async Task<ReviewItem?> ReadItemAsync(string dir)
    {
        var path = Path.Combine(dir, ReviewFileName);
        if (!File.Exists(path)) return null;
        return JsonSerializer.Deserialize<ReviewItem>(await File.ReadAllTextAsync(path), Json);
    }

    private Task WriteItemAsync(ReviewItem item) =>
        File.WriteAllTextAsync(Path.Combine(ItemFolder(item), ReviewFileName), JsonSerializer.Serialize(item, Json));
}
