using LifeOverYears.Models;

namespace LifeOverYears.Services;

public static class SceneDnaValidator
{
    // Values that mean "Vision did not place this photo" rather than naming a
    // kind of place. "unknown" is the one the vision prompt asks for; the others
    // are what a model reaches for when it is unsure, and each of them lands on
    // the generic content pool if it is let through.
    private static readonly string[] UndeterminedSceneTypes =
        { "unknown", "default", "other", "none", "unclear", "n/a" };

    public static IReadOnlyList<string> Validate(SceneDna s)
    {
        var missing = new List<string>();

        // Was an exact match on "unknown", so a null or empty scene_type — which
        // is the same failure, just spelled differently — never triggered the
        // enrichment retry at all.
        if (IsUndetermined(s.SceneType))     missing.Add("scene_type");
        if (s.Geometry.Roads.Count == 0)     missing.Add("geometry.roads");
        if (s.Geometry.Buildings.Count == 0) missing.Add("geometry.buildings");

        return missing;
    }

    public static bool IsUndetermined(string? sceneType) =>
        string.IsNullOrWhiteSpace(sceneType)
        || UndeterminedSceneTypes.Contains(sceneType.Trim(), StringComparer.OrdinalIgnoreCase);

    // Can this scene type actually be rendered, or would it fall through to the
    // generic pool? Unrecognised types are the dangerous case: they are not
    // "unknown", so nothing flags them, and every data lookup quietly falls back
    // to "default" — six images of nowhere in particular, at full price.
    //
    // knownTypes is data/prompts/scene-types.txt, the file the synthetic base
    // needs a phrase in, so a type absent from it cannot produce a base image
    // even if the era data happens to cover it. Resolved through
    // SceneContentKey first, because "highway" is renderable while the literal
    // string "highway" is not a key in that file — its two flavors are.
    public static bool IsRenderableSceneType(
        string? sceneType, string? terrain, IReadOnlyDictionary<string, string> knownTypes)
    {
        if (IsUndetermined(sceneType))
            return false;

        var key = SceneContentKey.Resolve(sceneType!.Trim(), terrain);
        return knownTypes.ContainsKey(key) && !IsUndetermined(key);
    }
}
