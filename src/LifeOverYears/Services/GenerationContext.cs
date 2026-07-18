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

    // ── Business names ────────────────────────────────────────────────────────
    // Descriptive words next to a business type leak onto signs ("the same local
    // diner, sign aging" rendered a sign reading "Aging Local DINER"), so scene
    // content references the diner by a proper {DINER_NAME} token instead. One name
    // is drawn per context (per scene run) and reused across all six eras.
    public static readonly IReadOnlyList<string> DinerNames = new[]
    {
        "MARY'S DINER", "MAIN STREET DINER", "STAR DINER", "TOWN DINER",
        "RED ROBIN DINER", "BLUE PLATE DINER"
    };

    private string? _dinerName;
    public string DinerName => _dinerName ??= DinerNames[Random.Next(DinerNames.Count)];

    // ── Scene condition ───────────────────────────────────────────────────────
    // One condition is sampled per era from that era's allowed list (a shared
    // context spans all six eras, so this is re-sampled on each BuildAsync call
    // and reflects the most recently built era). Falls back to "thriving" when an
    // era declares no allowed conditions.
    public string SceneCondition { get; private set; } = "thriving";

    public string PickSceneCondition(IReadOnlyList<string>? allowed)
    {
        SceneCondition = allowed is { Count: > 0 }
            ? allowed[Random.Next(allowed.Count)]
            : "thriving";
        return SceneCondition;
    }

    // ── Vehicle placement patterns ────────────────────────────────────────────
    // Simple patterns only: manual tests showed the model reliably follows
    // side-of-street and drive direction, but ignores complex choreography, so
    // nothing here references parking manoeuvres or wheel angles. One pattern is
    // sampled per era; a per-run HashSet keeps eras from repeating a pattern until
    // the relevant pool is exhausted.
    public static readonly IReadOnlyList<string> PlacementPatterns34 = new[]
    {
        "the first two parked on the LEFT side; the rest parked on the RIGHT side",
        "the first two parked on the RIGHT side; one parked on the left near the corner; the last one driving toward the camera in the far lane",
        "all parked on the RIGHT side with gaps; the first one driving away from the camera in the near lane",
        "the first two parked on the LEFT side; the last one driving away from the camera"
    };

    public static readonly IReadOnlyList<string> PlacementPatterns56 = new[]
    {
        "three parked on the LEFT side; the rest parked on the RIGHT side; the last one driving toward the camera in the far lane",
        "three parked on the RIGHT side; one parked on the left near the corner; the last one driving away from the camera in the near lane",
        "two parked on each side with gaps; the last one driving toward the camera"
    };

    // Pool selected by vehicle count: 3-4 vehicles → the smaller-arrangement pool.
    public static IReadOnlyList<string> PlacementPoolFor(int vehicleCount) =>
        vehicleCount <= 4 ? PlacementPatterns34 : PlacementPatterns56;

    private readonly HashSet<string> _usedPlacements = new();

    public string NextPlacement(int vehicleCount)
    {
        var pool      = PlacementPoolFor(vehicleCount);
        var available = pool.Where(p => !_usedPlacements.Contains(p)).ToList();
        if (available.Count == 0)          // pool exhausted this run — reuse is allowed
            available = pool.ToList();
        var pick = available[Random.Next(available.Count)];
        _usedPlacements.Add(pick);
        return pick;
    }

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
