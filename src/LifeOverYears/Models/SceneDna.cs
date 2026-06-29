namespace LifeOverYears.Models;

public record Camera(string Height, string Direction, int Fov);

public record Building(string Type, string Position);

public record Tree(string Position, string Size, string Type);

public record Geometry(IReadOnlyList<string> Roads, bool Sidewalks, IReadOnlyList<Building> Buildings);

public record Environment(string Terrain, IReadOnlyList<Tree>? Trees, IReadOnlyList<string> Utilities);

public record SceneDna(
    string Id,
    string CreatedAt,
    Camera Camera,
    Geometry Geometry,
    Environment Environment,
    IReadOnlyList<string> ImmutableElements,
    string SceneType = "unknown");
