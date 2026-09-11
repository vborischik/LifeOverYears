namespace LifeOverYears.Models;

// Everything a platform needs to put one finished run in front of people.
// One record for all five targets, so the orchestrator that eventually runs
// them builds it once from the run folder and hands the same thing to each.
public record PublishRequest(
    Video Video,
    Caption Caption,

    // The 2025 frame, usually — the only frame a viewer recognises, and the one
    // the video opens on. Null means the platform picks its own cover.
    string? ThumbnailPath,

    // YouTube's vocabulary — "private", "unlisted", "public" — and every other
    // target maps it onto whatever it has. Never defaulted: the one publish
    // that cannot be undone through an upload-only scope is the accidental
    // public one, so the caller says the word every time.
    string Privacy,

    // Scheduled release. YouTube and Facebook honour it natively; the others
    // publish now and ignore it.
    DateTimeOffset? PublishAt = null,

    // A URL a third party can fetch the video from without credentials.
    // Instagram and Facebook do not take bytes — they pull from a URL — so
    // this is filled by a storage step (Dropbox) before those two run, and
    // they refuse to start without it rather than fail three calls in.
    string? PublicVideoUrl = null);
