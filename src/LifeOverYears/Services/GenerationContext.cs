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
    // content references each recurring business by a proper name token instead.
    // One name per pool is drawn per context (per scene run) and reused across
    // all six eras.
    public static readonly IReadOnlyList<string> DinerNames = new[]
    {
        "MARY'S DINER", "MAIN STREET DINER", "STAR DINER", "TOWN DINER",
        "RED ROBIN DINER", "BLUE PLATE DINER"
    };

    public static readonly IReadOnlyList<string> DrugStoreNames = new[]
    {
        "CORNER DRUGS", "CITY DRUG", "MAIN STREET PHARMACY", "PARK DRUGS"
    };

    public static readonly IReadOnlyList<string> HardwareNames = new[]
    {
        "MILLER HARDWARE", "ACE HARDWARE", "TOWN HARDWARE", "BENSON'S HARDWARE"
    };

    public static readonly IReadOnlyList<string> FiveAndDimeNames = new[]
    {
        "BEN FRANKLIN", "WOOLWORTH", "NEWBERRY'S"
    };

    public static readonly IReadOnlyList<string> BarberNames = new[]
    {
        "SAM'S BARBER SHOP", "MAIN ST BARBERS", "TONY'S BARBER SHOP", "CLASSIC CUTS"
    };

    public static readonly IReadOnlyList<string> ShoeRepairNames = new[]
    {
        "JOE'S SHOE REPAIR", "COBBLER'S CORNER", "CITY SHOE REPAIR"
    };

    public static readonly IReadOnlyList<string> ApplianceNames = new[]
    {
        "WESTSIDE TV & APPLIANCE", "HOME APPLIANCE CO", "CENTRAL TV"
    };

    public static readonly IReadOnlyList<string> DressShopNames = new[]
    {
        "THE FASHION SHOP", "ELAINE'S", "MODE APPAREL"
    };

    private string? _dinerName;
    public string DinerName => _dinerName ??= DinerNames[Random.Next(DinerNames.Count)];

    private string? _drugStoreName;
    public string DrugStoreName => _drugStoreName ??= DrugStoreNames[Random.Next(DrugStoreNames.Count)];

    private string? _hardwareName;
    public string HardwareName => _hardwareName ??= HardwareNames[Random.Next(HardwareNames.Count)];

    private string? _fiveAndDimeName;
    public string FiveAndDimeName => _fiveAndDimeName ??= FiveAndDimeNames[Random.Next(FiveAndDimeNames.Count)];

    private string? _barberName;
    public string BarberName => _barberName ??= BarberNames[Random.Next(BarberNames.Count)];

    private string? _shoeRepairName;
    public string ShoeRepairName => _shoeRepairName ??= ShoeRepairNames[Random.Next(ShoeRepairNames.Count)];

    private string? _applianceName;
    public string ApplianceName => _applianceName ??= ApplianceNames[Random.Next(ApplianceNames.Count)];

    private string? _dressShopName;
    public string DressShopName => _dressShopName ??= DressShopNames[Random.Next(DressShopNames.Count)];

    // Full token map for callers that substitute business names into an assembled
    // prompt without needing to know each individual property.
    public IReadOnlyDictionary<string, string> BusinessNameTokens() => new Dictionary<string, string>
    {
        ["{DINER_NAME}"]         = DinerName,
        ["{DRUGSTORE_NAME}"]     = DrugStoreName,
        ["{HARDWARE_NAME}"]      = HardwareName,
        ["{FIVE_AND_DIME_NAME}"] = FiveAndDimeName,
        ["{BARBER_NAME}"]        = BarberName,
        ["{SHOE_REPAIR_NAME}"]   = ShoeRepairName,
        ["{APPLIANCE_NAME}"]     = ApplianceName,
        ["{DRESS_SHOP_NAME}"]    = DressShopName,
    };

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
    // Parked vehicles only: driving cars and per-vehicle orientation were removed
    // after testing — the image model handles a single global traffic rule better
    // than choreography, and moving cars were the main source of wrong-way
    // vehicles. One pattern is sampled per era; a per-run HashSet keeps eras from
    // repeating a pattern until the relevant pool is exhausted.
    public static readonly IReadOnlyList<string> PlacementPatterns34 = new[]
    {
        "all parked on the RIGHT side of the street with gaps between them",
        "all parked on the LEFT side of the street with gaps between them",
        "the first two parked on the RIGHT side; the rest parked on the LEFT side",
        "the first one parked on the LEFT side near the corner; the rest parked on the RIGHT side"
    };

    public static readonly IReadOnlyList<string> PlacementPatterns56 = new[]
    {
        "all parked on the RIGHT side of the street with gaps between them",
        "three parked on the RIGHT side; the rest parked on the LEFT side",
        "two parked on the LEFT side; the rest parked on the RIGHT side with gaps"
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
