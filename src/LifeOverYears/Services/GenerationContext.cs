namespace LifeOverYears.Services;

public sealed class GenerationContext
{
    public required Random Random { get; init; }
    public HashSet<string> UsedCarModels { get; } = new(StringComparer.OrdinalIgnoreCase);
}
