using LifeOverYears.Models;

namespace LifeOverYears.Services;

// Reads a finished run folder into a PublishRequest. This is the one
// definition of "what a run looks like to a publisher": the video, the
// caption CaptionRunner wrote, the title, and the 2025 frame as the cover.
// Both the one-shot publish and the review queue go through it, so they
// cannot disagree about what gets posted.
public static class RunPublishSource
{
    public const string VideoRelativePath = "video/timeline.mp4";
    public const string CaptionFileName   = "caption.txt";
    public const string TitleFileName     = "title.txt";
    public const string PublishFileName   = "publish.json";

    // The files a review copy needs. Everything else in a run folder —
    // prompts, jobs, the era images — is generation, not publishing.
    public static readonly string[] RequiredRelativePaths =
    {
        VideoRelativePath, CaptionFileName, TitleFileName,
    };

    public static string? CoverRelativePath(string runFolder)
    {
        // The video opens on the newest frame; the cover should be the same
        // one. Newest by year, whatever years the run had.
        var imagesDir = Path.Combine(runFolder, "images");
        if (!Directory.Exists(imagesDir)) return null;
        var newest = Directory.EnumerateFiles(imagesDir, "*.png")
            .Select(Path.GetFileNameWithoutExtension)
            .Where(n => int.TryParse(n, out _))
            .Select(int.Parse!)
            .DefaultIfEmpty(-1)
            .Max();
        return newest < 0 ? null : Path.Combine("images", $"{newest}.png");
    }

    public static bool IsPublishable(string runFolder) =>
        RequiredRelativePaths.All(rel => File.Exists(Path.Combine(runFolder, rel)));

    public static bool HasDecision(string runFolder) =>
        File.Exists(Path.Combine(runFolder, PublishFileName));

    public static async Task<PublishRequest> ReadAsync(string runFolder, string privacy)
    {
        foreach (var rel in RequiredRelativePaths)
            if (!File.Exists(Path.Combine(runFolder, rel)))
                throw new FileNotFoundException($"Run is not publishable — missing {rel}", Path.Combine(runFolder, rel));

        var captionText = await File.ReadAllTextAsync(Path.Combine(runFolder, CaptionFileName));
        var title       = (await File.ReadAllTextAsync(Path.Combine(runFolder, TitleFileName))).Trim();
        var (body, tags) = SplitCaption(captionText);

        var videoPath = Path.Combine(runFolder, VideoRelativePath);
        var cover     = CoverRelativePath(runFolder);
        var id        = Path.GetFileName(runFolder.TrimEnd(Path.DirectorySeparatorChar));

        return new PublishRequest(
            Video: new Video(
                Id:        id,
                ImageIds:  Array.Empty<string>(),
                FilePath:  videoPath,
                CreatedAt: File.GetCreationTimeUtc(videoPath).ToString("o")),
            Caption: new Caption(
                Id:          id,
                Title:       title,
                Description: body,
                Hashtags:    tags),
            ThumbnailPath: cover is null ? null : Path.Combine(runFolder, cover),
            Privacy:       privacy);
    }

    // caption.txt is the body, a blank line, then one hashtag per line — the
    // exact form CaptionRunner writes. Split back so each platform can place
    // the tags where it wants them. A file with no tag block is all body.
    public static (string Body, IReadOnlyList<string> Hashtags) SplitCaption(string captionText)
    {
        var text  = captionText.Replace("\r\n", "\n").TrimEnd();
        var lines = text.Split('\n');

        var tagStart = lines.Length;
        while (tagStart > 0 && lines[tagStart - 1].TrimStart().StartsWith('#'))
            tagStart--;

        var tags = lines[tagStart..]
            .SelectMany(l => l.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            .Where(t => t.StartsWith('#'))
            .ToList();
        var body = string.Join("\n", lines[..tagStart]).TrimEnd();
        return (body, tags);
    }
}
