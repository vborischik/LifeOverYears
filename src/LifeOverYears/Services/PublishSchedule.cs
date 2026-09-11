using System.Globalization;
using Microsoft.Extensions.Configuration;

namespace LifeOverYears.Services;

// Hands each upload its publication slot. Ported from YoutubePublisher.
//
// This is what decouples upload rate from publish rate: without it the tool
// has to run daily for a month because uploading *is* publishing; with it a
// whole batch goes up in one session and the platform releases the videos
// on the clock. YouTube and Facebook both take a scheduled time; the other
// targets publish immediately and ignore the slot.
public sealed class PublishSchedule
{
    public bool Enabled { get; }
    public DateTimeOffset First { get; }
    public double IntervalHours { get; }

    private PublishSchedule(bool enabled, DateTimeOffset first, double intervalHours)
    {
        Enabled       = enabled;
        First         = first;
        IntervalHours = intervalHours;
    }

    public static PublishSchedule Disabled => new(false, DateTimeOffset.UtcNow, 24);

    // Publish:Schedule:Enabled / FirstPublishUtc / IntervalHours.
    public static PublishSchedule From(IConfiguration config)
    {
        if (!config.GetValue("Publish:Schedule:Enabled", false))
            return Disabled;

        var raw = config["Publish:Schedule:FirstPublishUtc"];
        if (!DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture,
                DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var first))
            throw new InvalidOperationException(
                $"Publish:Schedule:FirstPublishUtc is not a parseable timestamp: '{raw}'. " +
                "Use an ISO 8601 UTC value such as 2026-10-01T17:00:00Z");

        var interval = config.GetValue("Publish:Schedule:IntervalHours", 24.0);
        if (interval <= 0)
            throw new InvalidOperationException("Publish:Schedule:IntervalHours must be positive");

        return new PublishSchedule(true, first, interval);
    }

    // The slot for the next video, given how many already hold one.
    //
    // Slots are counted, not looked up by position in a list, so a resumed
    // run continues the sequence instead of colliding with what it scheduled
    // yesterday. A slot already in the past is pushed to the next future one:
    // the platform rejects a publish time in the past, and silently dumping
    // the backlog at once is worse than a shifted schedule.
    public DateTimeOffset SlotFor(int alreadyScheduled, DateTimeOffset now)
    {
        var slot = First.AddHours(IntervalHours * alreadyScheduled);
        if (slot > now)
            return slot;

        // Whole intervals from the first slot, so the series keeps its time of
        // day rather than drifting to whenever the tool happened to run.
        var missed = Math.Ceiling((now - First).TotalHours / IntervalHours);
        return First.AddHours(IntervalHours * Math.Max(missed, alreadyScheduled));
    }
}
