using LifeOverYears.Models;

namespace LifeOverYears.Services;

// Builds a plausible SceneDna for a scene type from a seed, with no photograph
// and no Vision call.
//
// Why this can exist at all: under Pipeline:BaseMode=synthetic the photo is
// never sent anywhere. The base frame is drawn from SceneDna *text* — the road,
// the buildings, the trees, the immutable elements — and every era then edits
// that. So Vision is not reading the photo for the model's benefit there; it is
// only turning one particular photo into that text. Generate the text directly
// and the same pipeline runs with no photo and no vision cost at all.
//
// What it is used for today: the smoke suite. Ten hand-written fixtures cover
// ten shapes, and every prompt rule gets exercised against those same ten
// forever. A seeded generator covers a different scene on every seed — a corner
// shop with no trees, a strip mall with one building rather than three, a
// highway with an empty buildings array — which is where the rules that quietly
// assume a shape get caught.
//
// The values are deliberately narrow. This is not trying to invent interesting
// places; it is trying to produce scenes that are *ordinary* for their type, so
// that a prompt built from one is a fair test of the builder. Anything that
// varies here varies because a real photo of that type would vary.
public static class SceneDnaFactory
{
    public static IReadOnlyList<string> SceneTypes { get; } = new[]
    {
        "gas_station", "auto_repair", "strip_mall", "shopping_center", "mall",
        "downtown_street", "corner_shop", "freestanding_shop", "motel", "highway",
    };

    public static SceneDna Create(string sceneType, int seed, string? terrain = null)
    {
        var rng = new Random(seed);

        // Terrain is a real input, not decoration: SceneContentKey splits the
        // highway on it, so a caller that does not care still gets a spread
        // rather than one flavour forever.
        terrain ??= sceneType == "highway"
            ? Pick(rng, "urban", "suburban", "rural", "industrial")
            : Pick(rng, "urban", "suburban", "suburban", "rural");

        var spec = SpecFor(sceneType, terrain, rng);

        return new SceneDna(
            Id:        $"generated-{sceneType}-{seed}",
            CreatedAt: DateTimeOffset.UtcNow.ToString("o"),
            SceneType: sceneType,
            Camera: new Camera(
                Height:    Pick(rng, "eye-level", "eye-level", "low"),
                Direction: spec.CameraDirection,
                Fov:       rng.Next(70, 85)),
            Geometry: new Geometry(
                Roads:
                [
                    new Road(
                        Type:     spec.RoadType,
                        Lanes:    spec.Lanes,
                        Markings: spec.Markings,
                        Surface:  spec.Surface)
                ],
                Sidewalks: spec.Sidewalks,
                Curbs:     spec.Sidewalks,
                Buildings: spec.Buildings,
                Driveways: spec.Driveways,
                Parking:   spec.Parking),
            Environment: new Models.Environment(
                Terrain:   terrain,
                Utilities: spec.Utilities,
                Trees:     spec.Trees,
                Landscape: spec.Landscape),
            ImmutableElements: spec.Immutable,
            Composition: new Composition(
                SubjectDistance: Pick(rng, "mid", "mid", "close", "far"),
                FrameShare:      Pick(rng, "dominant", "large", "moderate"),
                Horizon:         Pick(rng, "middle", "low")),
            Distinctive: spec.Distinctive);
    }

    private sealed record Spec(
        string CameraDirection, string RoadType, int Lanes, IReadOnlyList<string> Markings,
        string Surface, bool Sidewalks, IReadOnlyList<Building> Buildings,
        IReadOnlyList<string> Driveways, string Parking, IReadOnlyList<string> Utilities,
        IReadOnlyList<Tree> Trees, IReadOnlyList<string> Landscape,
        IReadOnlyList<string> Immutable, IReadOnlyList<string> Distinctive);

    private static Spec SpecFor(string sceneType, string terrain, Random rng) => sceneType switch
    {
        "gas_station" => new Spec(
            "street-facing", "arterial", 2, ["center line", "edge line"], "asphalt", Maybe(rng, 0.6),
            [ Building("small attached store", "behind the pump island", 1, ["brick veneer", "plate glass"], "flat", "40 feet from road") ],
            ["corner entrance apron"], "open asphalt apron around the pump islands",
            ["overhead power lines", "utility pole at the lot edge"],
            TreeSet(rng, 0, 2),
            ["gravel edge along the apron", "narrow grass strip at the road"],
            ["freestanding pylon sign at the road", "pump island under a flat canopy", "canopy support columns"],
            ["a canopy wider than the store behind it", "the pylon sign set close to the kerb"]),

        "auto_repair" => new Spec(
            "street-facing", "residential", 2, ["center line"], "asphalt", true,
            [ Building("small office with plate glass", "corner of the lot", 1, ["brick veneer"], "flat parapet", "20 feet from road"),
              Building("service bay row under one roof", "rear of the lot", 1, ["concrete block", "corrugated metal"], "flat", "40 feet from road") ],
            ["concrete apron entrance"], "concrete apron in front of the bays",
            ["overhead power lines", "utility pole at the corner"],
            TreeSet(rng, 1, 2),
            ["gravel edge along the apron"],
            ["painted sign band across the parapet", "roll-up bay doors in a row"],
            ["bay doors set at an angle to the street"]),

        "strip_mall" => new Spec(
            "facade", "commercial arterial", rng.Next(2, 5), ["center line", "edge line", "turn arrow"], "asphalt", Maybe(rng, 0.5),
            [ Building("single-story retail row under one continuous roof", "across the back of the lot", 1, ["concrete block", "plate glass"], "flat", "100 feet from road") ],
            ["main entrance apron"], "surface lot in front of the row",
            ["lot light poles", "overhead power lines along the frontage"],
            TreeSet(rng, 1, 3),
            ["planter islands between parking rows", "grass verge along the road"],
            ["continuous storefront overhang", "freestanding pylon sign at the road", "parking lot islands"],
            ["a walkway canopy running the full length of the row"]),

        "shopping_center" => new Spec(
            "facade", "commercial arterial", 4, ["center line", "edge line", "turn arrow"], "asphalt", true,
            [ Building("anchor block with a raised parapet", "left of the run", 1, ["concrete block", "brick veneer"], "flat, raised parapet", "120 feet from road"),
              Building("inline retail block, lower parapet", "right of the anchor", 1, ["concrete block", "plate glass"], "flat", "120 feet from road") ],
            ["main entrance apron", "service drive at the far end"], "large surface lot in front of the run",
            ["lot light poles", "overhead power lines along the road frontage"],
            TreeSet(rng, 1, 3),
            ["planter islands between parking rows"],
            ["stepped parapet line across the run", "freestanding pylon sign at the road"],
            ["one unit standing a storey taller than the rest of the run"]),

        "mall" => new Spec(
            "facade", "commercial arterial", 4, ["center line", "edge line", "turn arrow"], "asphalt", false,
            [ Building("enclosed mall box", "rear of the lot", 1, ["concrete panels", "brick base course"], "flat", "150 feet from road") ],
            ["main entrance apron", "side entrance apron"], "large surface lot surrounding the building",
            ["tall parking lot light poles"],
            TreeSet(rng, 1, 2),
            ["long planter islands splitting the parking rows"],
            ["windowless end-cap facade", "recessed main entrance with canopy"],
            ["an entrance canopy projecting from an otherwise blank wall"]),

        "downtown_street" => new Spec(
            "street-facing", "main street", 2, ["center line", "crosswalk"], "asphalt", true,
            [ Building("two-story commercial block", "left block face", 2, ["red brick", "plate glass"], "flat parapet", "at-street"),
              Building("two-story commercial block", "right block face", 2, ["red brick"], "flat parapet", "at-street") ],
            [], "parallel street parking both sides",
            ["overhead power lines", "utility poles along the kerb"],
            TreeSet(rng, 0, 2),
            ["concrete sidewalks", "small planted tree pits"],
            ["continuous storefront frontage meeting the sidewalk", "transom windows above the shopfronts"],
            ["an upper-floor cornice running unbroken across several shopfronts"]),

        "corner_shop" => new Spec(
            "facade", "residential", 2, ["center line", "crosswalk at the corner"], "asphalt", true,
            [ Building("corner building with a shop at street level", "on the corner", rng.Next(1, 3), ["red brick", "plate glass"], "flat parapet", "at-street") ],
            [], "parallel street parking along the kerb",
            ["overhead power lines", "utility pole at the corner"],
            TreeSet(rng, 0, 1),
            ["concrete sidewalk squares", "granite kerb at the corner"],
            ["sign band above the storefront", "blank brick side wall along the side street", "entrance on the corner chamfer"],
            ["the entrance cut across the corner rather than facing either street"]),

        "freestanding_shop" => new Spec(
            "facade", "arterial", 2, ["center line", "edge line"], "asphalt", Maybe(rng, 0.5),
            [ Building("small standalone retail unit", "centre of the lot", 1, ["concrete block", "plate glass"], "flat parapet", "60 feet from road") ],
            ["entrance apron from the road"], "striped parking apron facing the entrance",
            ["overhead power lines", "a light pole at the lot edge"],
            TreeSet(rng, 0, 2),
            ["grass verge between the apron and the road"],
            ["freestanding sign near the road", "parking bays facing the shopfront"],
            ["parking bays laid at an angle to the frontage"]),

        "motel" => new Spec(
            "facade", "arterial", 2, ["center line", "edge line"], "asphalt", false,
            [ Building("single-story guest room row", "along the back of the lot", 1, ["concrete block", "painted stucco"], "shallow pitched", "70 feet from road"),
              Building("small office at the end of the row", "left end of the run", 1, ["concrete block"], "flat", "70 feet from road") ],
            ["entrance apron from the road"], "striped bays laid one per guest room door",
            ["overhead power lines", "a light pole at the lot entrance"],
            TreeSet(rng, 1, 2),
            ["grass verge along the road", "concrete walkway in front of the doors"],
            ["low pylon sign near the road", "uniform row of numbered guest doors", "one paired window per door"],
            ["the office roofline stepping down from the guest room row"]),

        "highway" => new Spec(
            "street", terrain == "rural" ? "rural two-lane highway" : "urban interstate",
            terrain == "rural" ? 2 : rng.Next(4, 7),
            terrain == "rural" ? ["center line", "edge line"] : ["edge line", "lane line"],
            terrain == "rural" ? "asphalt" : "concrete", false,
            // Often nothing at all beside the road: this is the shape that made
            // the base prompt invent a strip mall behind the median.
            Maybe(rng, 0.5)
                ? [ Building("low industrial building", "background, beyond the far shoulder", 1, ["concrete panels"], "flat", "200 feet from the roadway") ]
                : [],
            [], "",
            terrain == "rural"
                ? ["wooden utility poles along the shoulder", "transmission towers across the fields"]
                : ["overhead sign gantry", "tall light standards on mast arms"],
            TreeSet(rng, 0, 1, background: true),
            terrain == "rural" ? ["open fields both sides", "gravel shoulder"] : ["mown embankment", "concrete median barrier"],
            ["guardrail along the shoulder", "the road alignment and lane count"],
            ["a long curve opening out beyond the near shoulder"]),

        _ => new Spec(
            "street", "local street", 2, ["center line"], "asphalt", false,
            [ Building("industrial warehouse", "background", 1, ["corrugated metal"], "gabled", "50 feet from road") ],
            ["gravel access road"], "gravel lot",
            ["overhead power lines"], TreeSet(rng, 0, 1), ["gravel and packed dirt lot"],
            ["loading dock on the end wall"], ["a dock door wider than the others in the run"]),
    };

    private static readonly string[] TreeTypes = { "oak", "maple", "elm", "honey locust", "pear", "cottonwood" };
    private static readonly string[] TreeSizes = { "small", "medium", "large" };

    private static readonly string[] TreePositions =
    {
        "left of the frontage", "right of the frontage", "at the kerb", "in a planter island",
        "along the side property line", "beyond the far edge of the lot",
    };

    // A count of zero is as important as any other: several rules quietly assume
    // at least one tree, and only a scene without them shows that up.
    private static IReadOnlyList<Tree> TreeSet(Random rng, int min, int max, bool background = false)
    {
        var n = rng.Next(min, max + 1);
        return Enumerable.Range(0, n)
            .Select(_ => new Tree(
                Position: background ? "background" : Pick(rng, TreePositions),
                Size:     Pick(rng, TreeSizes),
                Type:     Pick(rng, TreeTypes)))
            .ToList();
    }

    private static Building Building(
        string type, string position, int stories, string[] materials, string roof, string setback) =>
        new(type, position, stories, materials, roof, setback);

    private static bool Maybe(Random rng, double p) => rng.NextDouble() < p;

    private static T Pick<T>(Random rng, params T[] options) => options[rng.Next(options.Length)];

    private static T Pick<T>(Random rng, IReadOnlyList<T> options) => options[rng.Next(options.Count)];
}
