namespace LifeOverYears.Models;

public record Camera(string Height, string Direction, int Fov);

public record Road(string Type, int Lanes, IReadOnlyList<string> Markings, string Surface);

public record Building(string Type, string Position, int Stories,
    IReadOnlyList<string> Materials, string Roof, string Setback);

public record Tree(string Position, string Size, string Type);

public record Geometry(
    IReadOnlyList<Road> Roads,
    bool Sidewalks,
    bool Curbs,
    IReadOnlyList<string> Driveways,
    string Parking,
    IReadOnlyList<Building> Buildings);

public record Environment(
    string Terrain,
    IReadOnlyList<Tree> Trees,
    IReadOnlyList<string> Utilities,
    IReadOnlyList<string> Landscape);

public record SceneDna(
    string Id,
    string CreatedAt,
    string SceneType,
    Camera Camera,
    Geometry Geometry,
    Environment Environment,
    IReadOnlyList<string> ImmutableElements);
