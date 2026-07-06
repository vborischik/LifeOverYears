using System.Text.Json.Serialization;

namespace LifeOverYears.Models;

public record Camera(string Height, string Direction, int Fov);

<<<<<<< HEAD
public record Road(string Type, int Lanes, IReadOnlyList<string> Markings, string Surface);

public record Building(string Type, string Position, int Stories,
    IReadOnlyList<string> Materials, string Roof, string Setback);
=======
public record Road(
    string Type,
    int Lanes,
    IReadOnlyList<string> Markings,
    string Surface);

public record Building(
    string Type,
    string Position,
    int Stories,
    IReadOnlyList<string> Materials,
    string Roof,
    string Setback);
>>>>>>> 3f9e103 (changed whole logoc)

public record Tree(string Position, string Size, string Type);

public record Geometry(
    IReadOnlyList<Road> Roads,
    bool Sidewalks,
    bool Curbs,
<<<<<<< HEAD
    IReadOnlyList<string> Driveways,
    string Parking,
    IReadOnlyList<Building> Buildings);

public record Environment(
    string Terrain,
    IReadOnlyList<Tree> Trees,
    IReadOnlyList<string> Utilities,
=======
    IReadOnlyList<Building> Buildings,
    IReadOnlyList<string> Driveways,
    string Parking);

public record Environment(
    string Terrain,
    IReadOnlyList<string> Utilities,
    IReadOnlyList<Tree> Trees,
>>>>>>> 3f9e103 (changed whole logoc)
    IReadOnlyList<string> Landscape);

public record SceneDna(
    string Id,
    string CreatedAt,
    string SceneType,
    Camera Camera,
    Geometry Geometry,
    Environment Environment,
    IReadOnlyList<string> ImmutableElements,
    string? SceneType = null);
