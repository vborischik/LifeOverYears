using LifeOverYears.Models;

namespace LifeOverYears.Services;

// The caption's parts, assembled the way each platform wants them. Kept out
// of the providers so that "body then a line of hashtags" is one definition
// and a change to it lands on every platform at once.
public static class PublishText
{
    // Body text followed by the hashtags on their own line — the post form
    // Instagram, Facebook and YouTube descriptions all take. Hashtags come
    // from data/captions/hashtags.txt already carrying their '#'.
    public static string BodyWithTags(Caption caption) =>
        caption.Hashtags.Count == 0
            ? caption.Description
            : $"{caption.Description}\n\n{string.Join(" ", caption.Hashtags)}";

    // Title, body, hashtags — for a platform with no separate title field.
    public static string TitledBodyWithTags(Caption caption) =>
        $"{caption.Title}\n\n{BodyWithTags(caption)}";

    // Hashtags without the '#', for platforms whose tag field is bare words.
    public static IReadOnlyList<string> BareTags(Caption caption) =>
        caption.Hashtags
            .Select(t => t.TrimStart('#').Trim())
            .Where(t => t.Length > 0)
            .ToList();
}
