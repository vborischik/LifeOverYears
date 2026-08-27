namespace LifeOverYears.Services;

public static class SceneContentKey
{
    // Vision classifies one scene_type, "highway" — the vocabulary (traffic
    // density arc, road furniture) depends on urban vs rural setting, which
    // Vision already captures in environment.terrain, so the content lookup key
    // is split from the structural scene_type. Everything that reads *data*
    // (era scene_content, caption bodies, titles, AnglesByScene) uses the
    // resolved key; everything that reads *structure* (SupportsCondition,
    // isGasStation, isHighway itself) keeps using the raw sceneType, because
    // both flavors share the same code paths (no condition arc, moving traffic,
    // no storefronts).
    // Only literal "rural" terrain — or an unrecognized/missing value — falls to
    // the two-lane countryside flavor. Industrial and suburban terrain both
    // imply road and traffic density closer to an interstate corridor than to
    // open country: an industrial highway corridor is warehouses and multi-lane
    // arterials, not a two-lane road through farmland.
    public static string Resolve(string sceneType, string? terrain) =>
        sceneType == "highway"
            ? (terrain is "urban" or "suburban" or "industrial" ? "highway_urban" : "highway_rural")
            : sceneType;
}
