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

    // True when each era is generated on top of the previous era's image rather
    // than on the shared empty base. The uploaded photo is then already
    // populated, so the era prompt has to clear the previous era's people and
    // vehicles before placing its own — otherwise they accumulate down the chain.
    public bool ChainedFromPreviousEra { get; init; }

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
    // a place starts declining it never quietly recovers in a later era, and it
    // worsens one step at a time — healthy never jumps straight to derelict.
    // The only exception is the final era, which resolves the arc for every
    // scene type that supports conditions.
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

    public string PickSceneCondition(IReadOnlyList<string>? allowed, string sceneType, int year)
    {
        // Finale: if the place ever fell apart, the last era resolves the arc.
        // A gas station may be rebuilt, renovated, or taken over by squatters.
        // Everything else resolves by how far it fell: a row that only ever
        // declined can come back renovated or keep limping, but one that reached
        // rank 2 stays down — no resurrection, because a place does not go from
        // derelict to trading in a single era and showing it that way is the one
        // beat that reads as false. "squatted" is no longer gas-station-only:
        // for downtown and strip malls it now means a half-dead row — some units
        // boarded, the cheap survivors still lit — not a sealed ruin.
        // Deliberately bypasses the era's allowed list, which has no "squatted".
        // A corner shop has no comeback in it: the trade that ends up in the
        // building is the last one it gets, so the finale resolves down rather
        // than either way. Renovating it would undo the arc the scene type
        // exists to show.
        if (IsLastEra && _conditionRank > 0)
        {
            SceneCondition = sceneType switch
            {
                "gas_station" => Random.Next(3) switch { 0 => "new", 1 => "restored", _ => "squatted" },
                // The turnover has already been shown by the finale, so the last
                // era is free to close the shop for good — one run in three does,
                // which is the difference between "this is what it is now" and
                // "this is where it ended".
                "corner_shop" => _conditionRank >= 2 || Random.Next(3) == 0 ? "squatted" : "declining",
                _             => _conditionRank >= 2
                    ? "squatted"
                    : Random.Next(3) switch { 0 => "restored", 1 => "restored", _ => "declining" }
            };
            _conditionRank = RankOf(SceneCondition);
            return SceneCondition;
        }

        var pool = (allowed ?? Array.Empty<string>())
            .Where(c => RankOf(c) >= _conditionRank)
            .ToList();

        // "abandoned" is never selected any more — a sealed, empty ruin is dead
        // air on screen, so the era JSONs' abandoned entries are read as the
        // half-dead "squatted" state instead. Mapped here rather than filtered
        // out so an era offering only "abandoned" still yields a rank-2 option
        // instead of falling through to the empty-pool branch. Distinct() keeps
        // an era that lists both from weighting squatted twice.
        pool = pool.Select(c => c == "abandoned" ? "squatted" : c).Distinct().ToList();

        // The corner shop is the one scene type whose arc is scripted rather
        // than sampled, and it needs a floor and a ceiling to stay legible.
        //
        // Floor: from DeclineFromYear it is never in good repair again. The era
        // pools still offer "thriving" that late, and taking it left runs where
        // the shop was spotless in 2005 and a liquor store in 2015 with nothing
        // in between — the decline has to be visible before the turnover, or the
        // turnover reads as a cut rather than an ending.
        //
        // Ceiling: it must not board up before the last era. The shop becoming a
        // liquor store is the beat the scene type exists for, and a derelict
        // 2015 shows a stripped sign instead of ever putting that store on
        // screen. It can still be squatted at the end, once the turnover has
        // been seen.
        if (sceneType == "corner_shop")
        {
            if (year >= DeclineFromYear)
            {
                var worn = pool.Where(c => RankOf(c) >= 1).ToList();
                if (worn.Count > 0) pool = worn;
            }
            if (!IsLastEra)
            {
                var survivable = pool.Where(c => RankOf(c) < 2).ToList();
                if (survivable.Count > 0) pool = survivable;
            }
        }

        // Condition worsens one step at a time: a healthy scene can start
        // declining, but it cannot skip straight to derelict.
        if (_conditionRank == 0)
        {
            var oneStep = pool.Where(c => RankOf(c) <= 1).ToList();
            if (oneStep.Count > 0)
                pool = oneStep;
        }

        // "new" = newly built — only credible in the run's first era. A rebuilt
        // finale sets "new" directly (before this pool), so it stays unaffected.
        if (!IsFirstEra)
            pool = pool.Where(c => !string.Equals(c, "new", StringComparison.OrdinalIgnoreCase)).ToList();

        if (pool.Count == 0)
        {
            // Era offers nothing at or above the reached rank — hold the arc.
            SceneCondition = _conditionRank >= 2 ? "squatted" : "declining";
            _everDecayed = true;
            return SceneCondition;
        }

        // Abandonment ends the story: once a place is derelict, the rank filter
        // pins every later era there, so a run that abandons early spends the
        // rest of itself showing the same boarded-up frame. Before the final era
        // it stays possible but uncommon — this is the main thing keeping runs
        // from all looking alike.
        if (!IsLastEra && pool.Count > 1 && Random.NextDouble() >= EarlyAbandonChance)
        {
            var survivable = pool.Where(c => RankOf(c) < 2).ToList();
            if (survivable.Count > 0)
                pool = survivable;
        }

        // Once decline has started, prefer it deepening over flattening out — but
        // as a tendency, not a certainty. Forcing it turned "declining" into a
        // one-way gate: the very next era that offered "abandoned" took it, which
        // is how 2015 ended up derelict in ~86% of runs.
        if (_conditionRank > 0)
        {
            var worse = pool.Where(c => RankOf(c) > _conditionRank).ToList();
            if (worse.Count > 0 && Random.NextDouble() < DeclineBias())
                pool = worse;
        }

        // Decline is the story these runs exist for, and an even draw across the
        // era's pool starts it too late to show. When a worse state is on offer,
        // take it more often than chance alone would, with the bias ramping up
        // quadratically across the run — low and flat through the early eras so
        // a place can plausibly stay healthy into the 2000s, then climbing hard
        // toward the end so the fall still lands. Gas stations are excluded:
        // they already resolve their arc through the new/squatted finale.
        if (sceneType is "downtown_street" or "strip_mall" or "auto_repair" or "corner_shop" && pool.Count > 1)
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

    // How often a pre-finale era is allowed to reach for "abandoned" at all.
    // Deliberately low: it is a terminal state, and reaching it early costs the
    // run every era after it.
    // Abandoned is disabled entirely — a bare empty ruin reads as dead air on
    // screen. Kept at 0.0 rather than deleted so the behaviour can be restored
    // by changing one number.
    private const double EarlyAbandonChance = 0.0;

    private double DeclineBias()
    {
        if (TotalEras <= 1) return 0.80;
        var t = (double)EraIndex / (TotalEras - 1);
        return 0.05 + 0.75 * t * t;
    }

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

    public enum ChainSignKind { Generic, Named, Ghost }
    public readonly record struct ChainTenant(string Name, ChainSignKind Kind, string Text);

    // ── Corner shop trajectory ────────────────────────────────────────────────
    // A corner shop tells one story and only one: the neighbourhood shop that
    // ends up a liquor store. It opens the run as a grocery or a pharmacy —
    // decided once per run — keeps that name and that trade through 2005, and
    // from LiquorFromYear the same building trades as a liquor store under a new
    // name, with the old lettering still ghosting above it. The building never
    // changes; only what it sells does, which is the point of the scene type.
    public enum CornerShopKind { Grocery, Pharmacy, Liquor }

    // PriorName is the previous business's name once the shop has turned over —
    // null while the original trade is still in the window.
    public readonly record struct CornerShopSign(CornerShopKind Kind, string? Name, string? PriorName);

    public const int LiquorFromYear = 2015;

    // The era the shop stops being in good repair — one era before the trade
    // turns over, so the decline is already on screen when the bottles arrive.
    public const int DeclineFromYear = 2005;

    private bool _cornerShopPlanBuilt;
    private CornerShopKind _originKind;
    private string? _originName;
    private string? _liquorName;

    public CornerShopKind CornerShopOriginKind
    {
        get
        {
            if (!_cornerShopPlanBuilt)
                throw new InvalidOperationException(
                    "Corner shop plan is not built yet — call ResolveCornerShop first");
            return _originKind;
        }
    }

    // Resolves what this era's corner shop sells and what its sign says. A
    // derelict era has no live sign at all: the scene block's derelict branch
    // describes a stripped shell, so returning a name here would put lettering
    // on a shop the prompt has just closed down.
    public CornerShopSign ResolveCornerShop(
        IReadOnlyDictionary<string, IReadOnlyList<string>> names, int year, string condition)
    {
        if (!_cornerShopPlanBuilt)
        {
            _cornerShopPlanBuilt = true;
            _originKind = Random.Next(2) == 0 ? CornerShopKind.Grocery : CornerShopKind.Pharmacy;
            _originName = PickName(names, _originKind);
            _liquorName = PickName(names, CornerShopKind.Liquor);
        }

        var turnedOver = year >= LiquorFromYear;
        var kind = turnedOver ? CornerShopKind.Liquor : _originKind;

        if (condition is "abandoned" or "squatted")
            return new CornerShopSign(kind, null, null);

        return turnedOver
            ? new CornerShopSign(kind, _liquorName, _originName)
            : new CornerShopSign(kind, _originName, null);
    }

    private string? PickName(
        IReadOnlyDictionary<string, IReadOnlyList<string>> names, CornerShopKind kind)
    {
        var key = kind.ToString().ToLowerInvariant();
        return names.TryGetValue(key, out var pool) && pool.Count > 0
            ? pool[Random.Next(pool.Count)]
            : null;
    }

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

    // ── Chain tenant plan ──────────────────────────────────────────────────
    // Whether Blockbuster and RadioShack ever set up shop in this run is decided
    // once, lazily, on first use — same idiom as the business-name pools above.
    // Independent rolls: both, one, or neither chain may be present. Once decided,
    // every era resolves the same chain's presence purely from year + condition —
    // no re-rolling, so a chain that showed up doesn't flicker in and out.
    private bool? _blockbusterPresent;
    public bool BlockbusterPresent => _blockbusterPresent ??= Random.NextDouble() < 0.5;

    private bool? _radioShackPresent;
    public bool RadioShackPresent => _radioShackPresent ??= Random.NextDouble() < 0.4;

    // Blockbuster only ever leased strip-mall end caps, never a main-street
    // storefront. RadioShack fit either. Any other scene type gets nothing.
    public IReadOnlyList<ChainTenant> ResolveChainTenants(int year, string sceneType, string condition)
    {
        if (sceneType is not ("strip_mall" or "downtown_street"))
            return Array.Empty<ChainTenant>();

        var tenants = new List<ChainTenant>();

        if (sceneType == "strip_mall" && BlockbusterPresent)
        {
            var bb = ResolveBlockbuster(year, condition);
            if (bb is not null) tenants.Add(bb.Value);
        }

        if (RadioShackPresent)
        {
            var rs = ResolveRadioShack(year, condition);
            if (rs is not null) tenants.Add(rs.Value);
        }

        return tenants;
    }

    // The chain opened in late 1985 and had no small-town presence until the late
    // eighties, and its stores were gone by 2014 — before 1990 it simply isn't
    // there yet, at all.
    private static ChainTenant? ResolveBlockbuster(int year, string condition)
    {
        if (year < 1990) return null;

        var kind = year <= 2009 ? ChainSignKind.Named : ChainSignKind.Ghost;

        // Ghost is driven by year alone; a live Named tenant must never appear
        // in a derelict era, so decay demotes Named down to Ghost.
        if (condition is "abandoned" or "squatted" && kind == ChainSignKind.Named)
            kind = ChainSignKind.Ghost;

        var text = kind switch
        {
            ChainSignKind.Named => year < 1996
                ? "a Blockbuster Video store in the row, blue storefront sign with the yellow torn-ticket logo, the ticket larger on this earlier wordmark"
                : "a Blockbuster store in the row, blue storefront sign with the yellow torn-ticket logo",
            ChainSignKind.Ghost => "the unit where the store was, a rectangular patch of less-faded wall above the door where the sign board was removed, anchor holes still visible, a lighter band along the top where the blue fascia ran, no lettering at all",
            _ => throw new InvalidOperationException($"Unexpected Blockbuster sign kind: {kind}")
        };

        return new ChainTenant("Blockbuster", kind, text);
    }

    // RadioShack ran under its own name for decades before the image-model-known
    // one-word logo existed, so pre-1990 it must never be named — only the
    // generic radio-and-electronics shop it actually was.
    private static ChainTenant? ResolveRadioShack(int year, string condition)
    {
        var kind = year < 1990 ? ChainSignKind.Generic
                 : year <= 2014 ? ChainSignKind.Named
                 : ChainSignKind.Ghost;

        if (condition is "abandoned" or "squatted")
        {
            if (kind == ChainSignKind.Named) kind = ChainSignKind.Ghost;
            else if (kind == ChainSignKind.Generic) return null; // no chain sign ever went up to strip
        }

        var text = kind switch
        {
            ChainSignKind.Generic => year <= 1980
                ? "a small radio and electronics shop, hand-lettered window sign reading CB RADIOS - ANTENNAS - SCANNERS, no chain name anywhere"
                : "a small radio and electronics shop, hand-lettered window sign reading STEREO - SPEAKERS - TAPES, no chain name anywhere",
            ChainSignKind.Named => "a RadioShack store, red sign board with white lettering",
            ChainSignKind.Ghost => "the unit where the store was, a rectangular patch of less-faded wall above the door where the sign board was removed, anchor holes still visible, no lettering at all",
            _ => throw new InvalidOperationException($"Unexpected RadioShack sign kind: {kind}")
        };

        return new ChainTenant("RadioShack", kind, text);
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

    // Off-street counterparts: a forecourt, apron or lot has no "side of the
    // street" to speak of, so these describe stalls instead of curb sides.
    // Wording stays generic so gas_station, strip_mall and auto_repair can all
    // draw from the same pools.
    public static readonly IReadOnlyList<string> PlacementPatternsLot34 = new[]
    {
        "all parked in the lot, nose-in to marked stalls, with an empty stall between them",
        "two nose-in near the building; the rest angled into stalls at the far edge of the lot",
        "one nose-in close to the entrance; the rest spread across stalls toward the lot's far edge",
        "all angled into stalls along the far edge of the lot, the near stalls left empty"
    };

    public static readonly IReadOnlyList<string> PlacementPatternsLot56 = new[]
    {
        "all parked in the lot, nose-in to marked stalls, with gaps between them",
        "three nose-in near the building; the rest in stalls at the far edge of the lot",
        "two in stalls near the entrance; the rest spread across the lot with gaps"
    };

    // Pool selected by parking type first, then vehicle count: 3-4 vehicles →
    // the smaller-arrangement pool.
    public static IReadOnlyList<string> PlacementPoolFor(int vehicleCount, bool onStreet) =>
        onStreet
            ? (vehicleCount <= 4 ? PlacementPatterns34    : PlacementPatterns56)
            : (vehicleCount <= 4 ? PlacementPatternsLot34 : PlacementPatternsLot56);

    // Union of both parking types' pools for a given vehicle count — for the
    // smoke suite, which validates a PLACEMENT line against whichever pool it
    // could plausibly have come from without needing to know onStreet itself.
    public static IReadOnlyList<string> AllPlacementPatternsFor(int vehicleCount) =>
        PlacementPoolFor(vehicleCount, true).Concat(PlacementPoolFor(vehicleCount, false)).ToList();

    private readonly HashSet<string> _usedPlacements = new();

    public string NextPlacement(int vehicleCount, bool onStreet)
    {
        var pool      = PlacementPoolFor(vehicleCount, onStreet);
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
