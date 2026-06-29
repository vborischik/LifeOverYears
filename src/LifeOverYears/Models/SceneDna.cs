namespace LifeOverYears.Models;

public record Camera(string Height, string Direction, int Fov);

public record Building(string Type, string Position);

public record Geometry(IReadOnlyList<string> Roads, bool Sidewalks, IReadOnlyList<Building> Buildings);

public record Environment(string Terrain, IReadOnlyList<string> Utilities);

public record SceneDna(
    string Id,
    string CreatedAt,
    string SceneType,
    Camera Camera,
    Geometry Geometry,
    Environment Environment,
    IReadOnlyList<string> ImmutableElements);
