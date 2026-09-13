using LifeOverYears.Models;

namespace LifeOverYears.Services.Interfaces;

// The human in the loop. Sends a finished video somewhere a person will see
// it, then reports what they answered. Telegram today; the loop that drives
// it neither knows nor cares.
public interface IReviewChannel
{
    // Sends the video with its caption and the two choices. Returns the
    // message id the decision will refer back to.
    Task<long> SendForReviewAsync(ReviewItem item, PublishRequest request, CancellationToken ct = default);

    // Waits up to `timeout` for new decisions and returns those that arrived.
    // Empty on timeout — the caller loops. Decisions that do not belong to a
    // known item are dropped here, not surfaced.
    Task<IReadOnlyList<ReviewDecision>> PollDecisionsAsync(TimeSpan timeout, CancellationToken ct = default);

    // Tells the reviewer what happened — the URL on success, the error
    // otherwise — as a reply to the review message.
    Task ReportAsync(ReviewItem item, string text, CancellationToken ct = default);
}

public record ReviewDecision(
    // The review message the reply refers to, or null for a bare text reply
    // that names no message — matched to the one item currently pending.
    long? ReviewMessageId,
    bool Approved);
