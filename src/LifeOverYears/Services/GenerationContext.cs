using System.Text.RegularExpressions;

namespace LifeOverYears.Services;

public sealed class GenerationContext
{
    public required Random Random { get; init; }

    // Stores normalized base model names, so variants of the same vehicle
    // ("Ford F-150" vs "Ford F-150 Lightning") cannot co-exist within a run.
    public HashSet<string> UsedCarModels { get; } = new(StringComparer.OrdinalIgnoreCase);

    public bool IsCarModelUsed(string model) => UsedCarModels.Contains(BaseModelName(model));

    public bool TryUseCarModel(string model) => UsedCarModels.Add(BaseModelName(model));

    // "2022-2025 Ford F-150 Lightning — electric pickup" -> "ford f-150"
    // (drop the descriptor, the year range, and trim/variant words after the
    // first model token, keeping make + first model token)
    public static string BaseModelName(string model)
    {
        var name   = model.Split(" — ")[0];
        var tokens = name.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(t => !Regex.IsMatch(t, @"^\d{4}(-\d{4})?$"))
            .Take(2);
        return string.Join(' ', tokens).ToLowerInvariant();
    }
}
