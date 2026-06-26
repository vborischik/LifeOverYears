namespace LifeOverYears.Models;

public record EraProfile(
    int Year,
    IReadOnlyList<string> Vehicles,
    IReadOnlyList<string> ArchitectureStyles,
    IReadOnlyList<string> Brands,
    IReadOnlyList<string> SignageStyles,
    IReadOnlyList<string> Fashion,
    IReadOnlyList<string> Technology);
