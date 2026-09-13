namespace LifeOverYears.Models;

// One run waiting for a human. Lives as review.json inside its folder under
// output/on-review/ — the folder IS the queue: a run is copied in when it
// finishes, and the folder is deleted once the decision has been acted on.
// The original run folder is never touched by the queue; the outcome is
// written back there as publish.json.
public record ReviewItem(
    // Folder name under on-review/, the same as the run folder's name.
    string Id,

    // Absolute path of the run this is a copy of — where publish.json goes.
    string RunFolder,

    // Set once the video has been sent for review. A restart must not send
    // it again: this is the id the reply is matched against.
    long? ReviewMessageId,

    string EnqueuedAt);

// What was decided, and what came of it. Written to the run folder as
// publish.json whether the answer was yes or no, so a run that has been
// through review is never queued twice.
public record PublishState(
    // "approved" | "skipped" | "published" | "failed"
    string Status,
    string DecidedAt,
    IReadOnlyList<Publication> Publications,
    string? Error = null);
