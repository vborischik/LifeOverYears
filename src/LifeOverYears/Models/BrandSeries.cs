namespace LifeOverYears.Models;

// A brand series replaces the photo → Vision → SceneDna path: the scene is
// described by a file in data/brands/series/ instead of being read off a
// photograph, and each era carries a logo reference image the generator matches
// letterforms against. Everything downstream of the prompt — era chaining, the
// year overlay, video assembly, the caption — is the ordinary run path.
//
// No JsonPropertyName attributes: the files are camelCase and JsonProvider
// deserializes case-insensitively, so the property names match as written.
public record BrandSeries
{
    public required string Brand { get; init; }

    // Kept as data rather than derived from the folder name so a series can say
    // what kind of place it is; nothing in CaptionService knows this value, so
    // captions fall back to base.txt exactly as any unmapped type does.
    public required string SceneType { get; init; }

    // What the building and lot are, in one phrase. Every era prompt opens with
    // it, so the first frame and the chained frames after it describe the same
    // premises without the brand name doing that work.
    public required string StoreDescription { get; init; }

    public required IReadOnlyList<int> Years { get; init; }

    // Keyed by year as a string, because that is what a JSON object gives us.
    public required IReadOnlyDictionary<string, BrandEra> Eras { get; init; }
}
