using LifeOverYears.Models;
using LifeOverYears.Services.Interfaces;
using Microsoft.Extensions.Logging;

namespace LifeOverYears.Services;

// The `review` mode's body: one item at a time, in the order the runs
// finished. Send it, wait for the answer, act on it, delete it, next. One
// at a time on purpose — a reviewer answering "publish" to a stack of six
// videos has to scroll back to see which one, and the text fallback ("yes",
// "no") only means something when exactly one question is open.
//
// Restart-safe by construction: an item whose ReviewMessageId is set is not
// sent again, and the decision is matched to that id. The one thing a
// restart loses is a reply that arrived while the process was down and the
// channel's offset had already moved past it — Telegram keeps updates for a
// day, and the channel persists its offset, so in practice it does not.
public sealed class ReviewLoop
{
    private readonly ReviewQueue _queue;
    private readonly IReviewChannel _channel;
    private readonly IPublishService _publisher;
    private readonly string _privacy;
    private readonly ILogger<ReviewLoop> _logger;

    private static readonly TimeSpan PollTimeout    = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan IdleRescan     = TimeSpan.FromSeconds(15);

    public ReviewLoop(
        ReviewQueue queue, IReviewChannel channel, IPublishService publisher, string privacy,
        ILogger<ReviewLoop> logger)
    {
        _queue     = queue;
        _channel   = channel;
        _publisher = publisher;
        _privacy   = privacy;
        _logger    = logger;
    }

    // Runs until cancelled. Returns the number of items it completed.
    public async Task<int> RunAsync(CancellationToken ct)
    {
        var completed = 0;
        _logger.LogInformation("Review loop: watching {Root}, targets {Targets}",
            _queue.Root, string.Join(", ", _publisher.Targets));

        while (!ct.IsCancellationRequested)
        {
            var items = await _queue.ListAsync();
            if (items.Count == 0)
            {
                await Task.Delay(IdleRescan, ct);
                continue;
            }

            var item = items[0];
            if (await ProcessOneAsync(item, ct))
                completed++;
        }
        return completed;
    }

    // One item through to its decision. True once it has been completed
    // (published or skipped); false means the loop should look again.
    public async Task<bool> ProcessOneAsync(ReviewItem item, CancellationToken ct)
    {
        var folder  = _queue.ItemFolder(item);
        var request = await RunPublishSource.ReadAsync(folder, _privacy);

        if (item.ReviewMessageId is null)
        {
            var messageId = await _channel.SendForReviewAsync(item, request, ct);
            await _queue.MarkSentAsync(item, messageId);
            item = item with { ReviewMessageId = messageId };
            _logger.LogInformation("Sent for review: {Id} (message {Msg})", item.Id, messageId);
        }

        var decisions = await _channel.PollDecisionsAsync(PollTimeout, ct);
        var decision  = decisions.FirstOrDefault(d =>
            d.ReviewMessageId == item.ReviewMessageId || d.ReviewMessageId is null);
        if (decision is null)
            return false;

        if (!decision.Approved)
        {
            await _queue.CompleteAsync(item, new PublishState(
                "skipped", DateTimeOffset.UtcNow.ToString("o"), Array.Empty<Publication>()));
            await _channel.ReportAsync(item, "Skipped.", ct);
            return true;
        }

        _logger.LogInformation("Approved: {Id} — publishing to {Targets}", item.Id, string.Join(", ", _publisher.Targets));
        var state = await _publisher.PublishAsync(request, ct);
        await _queue.CompleteAsync(item, state);

        var report = state.Status == "published"
            ? "Published:\n" + string.Join("\n", state.Publications.Select(p => $"{p.Platform}: {p.Url}"))
            : $"Publish FAILED: {state.Error}" +
              (state.Publications.Count > 0
                  ? "\nSucceeded: " + string.Join(", ", state.Publications.Select(p => $"{p.Platform} {p.Url}"))
                  : "");
        await _channel.ReportAsync(item, report, ct);
        return true;
    }
}
