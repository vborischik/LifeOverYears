using System.Text.RegularExpressions;

namespace LifeOverYears.Services;

public sealed class GenerationContext
{
    public required Random Random { get; init; }

    // The full list of years in this run, ascending or not. Used to plan the
    // gas-station brand timeline across the whole run (a station holds one brand
    // for decades). Optional: when empty (e.g. smoke tests) brand resolution
    // degenerates to a single cached brand chosen at the first era it is asked for.
    public IReadOnlyList<int> Years { get; init; } = Array.Empty<int>();

    // Stores normalized base model names, so variants of the same vehicle
    // ("Ford F-150" vs "Ford F-150 Lightning") cannot co-exist within a run.
    public HashSet<string> UsedCarModels { get; } = new(StringComparer.OrdinalIgnoreCase);

    public bool IsCarModelUsed(string model) => UsedCarModels.Contains(BaseModelName(model));

    public bool TryUseCarModel(string model) => UsedCarModels.Add(BaseModelName(model));

    // Same shape as UsedCarModels, but for people-activity/people-mix bullet
    // lines: without this, people picks had no cross-era memory the way cars do.
    public HashSet<string> UsedPeopleLines { get; } = new(StringComparer.OrdinalIgnoreCase);

    public bool TryUsePeopleLine(string line) => UsedPeopleLines.Add(line);

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

    // ── Scene condition trajectory ────────────────────────────────────────
    // A run tells one story across its eras. Condition rank is monotonic: once
    // a place starts declining it never quietly recovers in a later era. The
    // only exception is the final era of a gas station, which resolves the arc.
    public int TotalEras { get; init; } = 6;

    private int _eraIndex = -1;
    public int EraIndex => _eraIndex;
    public bool IsFirstEra => _eraIndex == 0;
    public bool IsLastEra => _eraIndex == TotalEras - 1;

    // Called once per era at the top of PromptService.BuildAsync, before any
    // sampling, so the index advances for every scene type — not only the ones
    // that sample a condition.
    public int BeginEra() => ++_eraIndex;

    public string SceneCondition { get; private set; } = "thriving";

    private int _conditionRank;   // 0 healthy, 1 declining, 2 derelict
    private bool _everDecayed;    // true once any era reached rank >= 1 this run

    private static int RankOf(string condition) => condition switch
    {
        "declining"               => 1,
        "abandoned" or "squatted" => 2,
        _                         => 0
    };

    public string PickSceneCondition(IReadOnlyList<string>? allowed, string sceneType)
    {
        // Gas station finale: if the place ever fell apart, the last era
        // resolves it — rebuilt, renovated, or taken over by squatters.
        // Deliberately bypasses the era's allowed list, which has no "squatted".
        if (sceneType == "gas_station" && IsLastEra && _conditionRank > 0)
        {
            SceneCondition = Random.Next(3) switch
            {
                0 => "new",
                1 => "restored",
                _ => "squatted"
            };
            _conditionRank = RankOf(SceneCondition);
            return SceneCondition;
        }

        var pool = (allowed ?? Array.Empty<string>())
            .Where(c => RankOf(c) >= _conditionRank)
            .ToList();

        // "new" = newly built — only credible in the run's first era. A rebuilt
        // finale sets "new" directly (before this pool), so it stays unaffected.
        if (!IsFirstEra)
            pool = pool.Where(c => !string.Equals(c, "new", StringComparison.OrdinalIgnoreCase)).ToList();

        if (pool.Count == 0)
        {
            // Era offers nothing at or above the reached rank — hold the arc.
            SceneCondition = _conditionRank >= 2 ? "abandoned" : "declining";
            _everDecayed = true;
            return SceneCondition;
        }

        // Once decline has started, prefer it deepening over flattening out:
        // a place that was declining in 2005 should look worse in 2015.
        if (_conditionRank > 0)
        {
            var worse = pool.Where(c => RankOf(c) > _conditionRank).ToList();
            if (worse.Count > 0)
                pool = worse;
        }

        // Decline is the story these runs exist for, and an even draw across the
        // era's pool starts it too late to show. When a worse state is on offer,
        // take it more often than chance alone would, with the bias ramping up
        // across the run so a place can stay healthy into the 2000s and only
        // fall apart near the end. Gas stations are excluded: they already
        // resolve their arc through the new/squatted finale.
        if (sceneType is "downtown_street" or "strip_mall" or "auto_repair" && pool.Count > 1)
        {
            var worstRank = pool.Max(RankOf);
            var worst = pool.Where(c => RankOf(c) == worstRank).ToList();
            if (worst.Count < pool.Count && Random.NextDouble() < DeclineBias())
                pool = worst;
        }

        SceneCondition = pool[Random.Next(pool.Count)];
        _conditionRank = Math.Max(_conditionRank, RankOf(SceneCondition));
        if (_conditionRank >= 1) _everDecayed = true;
        return SceneCondition;
    }

    private double DeclineBias() =>
        TotalEras <= 1 ? 0.80 : 0.15 + 0.65 * ((double)EraIndex / (TotalEras - 1));

    // ── Gas-station brand timeline ────────────────────────────────────────
    // A real station keeps one brand for decades. Across a run we hold a single
    // brand X; only if the run spans >= 25 years is there a ~50% chance of ONE
    // rebrand X -> Y near the 20-30 year mark. Decay overrides everything: an
    // abandoned/squatted era shows a dead, stripped board (no brand, no price),
    // and a decayed station never executes its planned rebrand. The finale that
    // resolves a decayed run's arc goes one of three ways: rebuilt under a NEW
    // brand Z different from whatever it last wore ("new"), renovated under its
    // OWN last live brand ("restored"), or abandoned to squatters ("squatted")
    // with no sign at all.
    public enum GasSignKind { Branded, DeadBoard }
    public readonly record struct GasSign(GasSignKind Kind, string? Brand);

    private bool _gasPlanBuilt;
    private string? _brandX;            // initial brand
    private string? _brandY;            // post-rebrand brand (null if no rebrand)
    private int _rebrandYear = int.MaxValue;
    private string? _lastLiveBrand;     // last brand actually shown lit

    // Public read-only summary of the brand arc, for callers (e.g. caption
    // generation) that need to describe the scene without touching sign-timeline
    // internals.
    public string? FirstBrand => _brandX;
    public string? LastBrand => _lastLiveBrand ?? _brandX;
    public bool RebrandOccurred => _rebrandYear != int.MaxValue && _brandY is not null;

    private string? PickEligibleAcross(
        IReadOnlyList<(string Name, int From, int To)> brands, int fromYear, int toYear, string? exclude)
    {
        bool NotExcluded((string Name, int From, int To) b) =>
            exclude is null || !string.Equals(b.Name, exclude, StringComparison.OrdinalIgnoreCase);

        var eligible = brands.Where(b => b.From <= fromYear && toYear <= b.To).Where(NotExcluded).ToList();
        if (eligible.Count == 0)  // relax to eligibility at the start year only
            eligible = brands.Where(b => b.From <= fromYear && fromYear <= b.To).Where(NotExcluded).ToList();
        return eligible.Count > 0 ? eligible[Random.Next(eligible.Count)].Name : null;
    }

    private void BuildGasPlan(IReadOnlyList<(string Name, int From, int To)> brands)
    {
        _gasPlanBuilt = true;
        if (Years.Count == 0) return; // no span — fall back to single cached brand in ResolveGasSign

        var ordered = Years.OrderBy(y => y).ToList();
        var first = ordered[0];
        var last  = ordered[^1];

        var doRebrand = (last - first) >= 25 && Random.Next(2) == 0;
        if (doRebrand)
        {
            var target = first + 20 + Random.Next(11);           // 20..30 years in
            var switchYear = ordered.FirstOrDefault(y => y >= target);
            if (switchYear > first) _rebrandYear = switchYear;
        }

        if (_rebrandYear == int.MaxValue)
        {
            _brandX = PickEligibleAcross(brands, first, last, exclude: null);
        }
        else
        {
            _brandX = PickEligibleAcross(brands, first, _rebrandYear - 1, exclude: null);
            _brandY = PickEligibleAcross(brands, _rebrandYear, last, exclude: _brandX);
            if (_brandY is null) _rebrandYear = int.MaxValue; // no distinct successor — hold X
        }
    }

    // Resolves the sign to render for a gas station this era, given its condition.
    public GasSign ResolveGasSign(
        IReadOnlyList<(string Name, int From, int To)> brands, int year, string condition)
    {
        if (!_gasPlanBuilt) BuildGasPlan(brands);

        // Dead station: bare stripped board regardless of brand plan.
        if (condition is "abandoned" or "squatted")
            return new GasSign(GasSignKind.DeadBoard, null);

        // Rebuilt finale: reopens under a NEW identity, different from the last worn brand.
        if (condition == "new" && IsLastEra && _everDecayed)
        {
            var z = PickEligibleAcross(brands, year, year, exclude: _lastLiveBrand);
            if (z is not null) _lastLiveBrand = z;
            return new GasSign(GasSignKind.Branded, z);
        }

        // Restored finale: the SAME business renovated, so it re-hangs its own
        // last live brand rather than adopting a new identity. If the run never
        // showed a live brand, fall through to the normal brand-plan path below
        // instead of inventing one here.
        if (condition == "restored" && IsLastEra && _everDecayed && _lastLiveBrand is not null)
            return new GasSign(GasSignKind.Branded, _lastLiveBrand);

        string? brand;
        if (Years.Count > 0)
            brand = (year >= _rebrandYear && _brandY is not null) ? _brandY : _brandX;
        else
            brand = _brandX ??= PickEligibleAcross(brands, year, year, exclude: null);

        if (brand is not null) _lastLiveBrand = brand;
        return new GasSign(GasSignKind.Branded, brand);
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
