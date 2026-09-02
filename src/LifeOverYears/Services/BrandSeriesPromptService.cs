using System.Text;
using LifeOverYears.Models;
using LifeOverYears.Services.Interfaces;
using Microsoft.Extensions.Logging;

namespace LifeOverYears.Services;

// Builds one era prompt from a BrandSeries file. Programmatic assembly, the
// same shape PromptService uses, with no template and no model call — the
// series file already says what the scene is, so there is nothing to infer.
//
// The brand name reaches the prompt only through the LOGO block. Once the sign
// is down (2015, 2025 in the Kmart series) the name appears nowhere: naming a
// brand inside a removal instruction is the surest way to put it back on the
// building, and under era chaining the frame being edited still has the
// lettering in it.
public sealed class BrandSeriesPromptService : IBrandSeriesPromptService
{
    private readonly ILogger<BrandSeriesPromptService> _logger;

    public BrandSeriesPromptService(ILogger<BrandSeriesPromptService> logger)
    {
        _logger = logger;
    }

    // The pools are sampled rather than listed in full for the same reason the
    // era pools are: a list of everything reads as mandatory, and a frontage
    // cannot hold five neighbours and five ad boards at once.
    private const int NeighborsMin = 3;
    private const int NeighborsMax = 4;
    private const int VehicleClassCount = 3;
    private const int FashionCount = 3;
    private const int SignageCount = 3;
    private const int AdvertisingCount = 3;

    public Prompt Build(
        BrandSeries series, int year, GenerationContext context,
        IReadOnlyList<(string Name, int From, int To, string Category)> centerReplacements)
    {
        if (!series.Eras.TryGetValue(year.ToString(), out var era))
            throw new InvalidOperationException(
                $"Brand series '{series.Brand}' has no era {year} — years present: {string.Join(", ", series.Eras.Keys)}");

        var rng      = context.Random;
        var eraIndex = context.BeginEra();

        // The era before this one in the run's year list — one fact serving two
        // purposes. It is the gap CONTINUITY states, and, when eras are chained,
        // it is also the frame this generation actually edits, so canopy growth
        // is stated against that image rather than against the base.
        var previousYear = PreviousYear(series, context, year);
        var chainedFrom  = context.ChainedFromPreviousEra ? previousYear : null;

        var vehicleClasses = PickVehicleClasses(era, context, rng);

        var sb = new StringBuilder();

        sb.AppendLine("One single continuous photograph — no panels, grids, or split frames.");
        sb.AppendLine($"A photorealistic {year} photograph of {series.StoreDescription}, shot from the " +
                       "parking lot towards the entrance.");

        // The first era is drawn from text with nothing uploaded, so it is the
        // one frame that has to state the canvas; every era after it inherits
        // that canvas by editing the frame before it, and repeating the aspect
        // there would invite a re-crop of a shot already in the right shape.
        //
        // Those later eras edit the previous year's finished frame, which still
        // has that year's shoppers and traffic in it. Nothing further down the
        // prompt removes them — a block describing this year's people only adds
        // to what is already there — so they accumulate down the run unless the
        // clearing is asked for here, before anything is placed.
        if (chainedFrom is null)
        {
            sb.AppendLine("OUTPUT FORMAT: a TRUE 9:16 vertical portrait frame.");
        }
        else
        {
            sb.AppendLine("Use the uploaded photo as the exact base composition. It shows this same place " +
                           "in an earlier year: first remove EVERY person, vehicle and bicycle already in " +
                           "it, so the lot and the frontage are bare, then populate them as specified " +
                           "below. Keep none of the earlier year's figures or traffic.");
        }

        AppendContinuity(sb, year, previousYear);
        AppendLogo(sb, series, era, PreviousEra(series, previousYear), year, centerReplacements, rng, context);
        AppendLogoFail(sb, era);
        AppendScene(sb, era, rng);
        AppendVehicles(sb, vehicleClasses);
        AppendPeople(sb, era, rng);
        AppendDetails(sb, era, rng);
        AppendTrees(sb, era, year, chainedFrom, previousYear, context);
        AppendStyle(sb, era);

        sb.AppendLine();
        sb.AppendLine("PRIVACY");
        sb.Append("Blur every face and make every licence plate unreadable.");

        var text = sb.ToString();
        _logger.LogInformation(
            "Brand prompt built: brand={Brand} year={Year} era={Index} logoRef={LogoRef} length={Length}",
            series.Brand, year, eraIndex, era.LogoRef ?? "none", text.Length);

        return new Prompt(
            Id:               Guid.NewGuid().ToString(),
            SceneDnaId:       SeriesId(series),
            Year:             year,
            Text:             text,
            SelectedVehicles: vehicleClasses,
            CreatedAt:        DateTimeOffset.UtcNow.ToString("o"),
            SceneCondition:   era.Condition);
    }

    // The run's own id, used for the run folder name and the prompt records.
    public static string SeriesId(BrandSeries series) =>
        series.Brand.ToLowerInvariant().Replace(' ', '-');

    // ── Blocks ────────────────────────────────────────────────────────────────

    // One block, stated once. Saying "same building, same angle, same framing"
    // three different ways across three sections is how a prompt ends up
    // arguing with itself about which of them the model should weigh.
    private static void AppendContinuity(StringBuilder sb, int year, int? previousYear)
    {
        if (previousYear is not { } previous)
            return;

        sb.AppendLine();
        sb.AppendLine("CONTINUITY");
        sb.AppendLine($"The same location {year - previous} years later: same camera position, framing, " +
                       "building footprint, parking geometry, landscaping islands and neighbouring " +
                       "storefronts. Only historical content changes.");
    }

    // The era before this one, or null for the first frame. What was on the
    // facade in the uploaded photo is the whole question the LOGO block has to
    // answer, and only the previous era knows it.
    private static BrandEra? PreviousEra(BrandSeries series, int? previousYear) =>
        previousYear is { } y && series.Eras.TryGetValue(y.ToString(), out var era) ? era : null;

    private static void AppendLogo(
        StringBuilder sb, BrandSeries series, BrandEra era, BrandEra? previous, int year,
        IReadOnlyList<(string Name, int From, int To, string Category)> centerReplacements,
        Random rng, GenerationContext context)
    {
        // The sign is gone. Said as what is physically there now, not as an
        // absence: the uploaded frame still carries the lettering, and an
        // instruction that only stops mentioning it leaves it standing.
        if (era.SignRemoved)
        {
            sb.AppendLine();
            sb.AppendLine("SIGN REMOVED");
            sb.AppendLine("The store lettering that ran across the facade has been taken down and no sign " +
                           "hangs there now. What is there instead: bare mounting points and open " +
                           "attachment holes in the fascia, a faded outline where the letters sat, and a " +
                           "rectangle of cleaner, less weathered paint around that outline. The pylon sign " +
                           "at the road is an empty steel frame with no panel in it.");
            return;
        }

        // The building outlives the business. Footprint and roofline carry over;
        // the frontage does not, and the fascia is resurfaced rather than merely
        // unmentioned, so no ghost of the old sign survives the chain.
        if (era.Redeveloped)
        {
            sb.AppendLine();
            sb.AppendLine("REDEVELOPED");
            sb.AppendLine("The original building footprint and roofline are unchanged. The frontage is now " +
                           "subdivided into several separate tenant units, each with its own entrance, " +
                           "glazing and awning. The fascia has been resurfaced in fresh uniform cladding " +
                           "across the whole building and carries current tenant signage only — no earlier " +
                           "store name, outline, ghost lettering or mounting hardware survives anywhere on " +
                           "it. The pylon sign at the road carries a stack of current tenant panels.");

            // Named, real trades rather than category words. "FITNESS" over a
            // door is a label for a shop, not a shop; these are the businesses
            // that actually took these buildings, and each one is filtered to
            // the years it was trading in, so a dead chain cannot open in 2025.
            var tenants = PickTenants(centerReplacements, year, rng, context);
            if (tenants.Count > 0)
            {
                sb.AppendLine($"The units now trading are {Join(tenants)} — one per bay, each with its own " +
                               "sign across its own unit, spelled exactly as given and no other wording.");
            }
            return;
        }

        if (era.LogoSpec is not { Count: > 0 } spec)
            return;

        sb.AppendLine();
        sb.AppendLine("LOGO");

        // The uploaded frame already has a sign on the building. Describing this
        // era's logo without saying what happens to that one is the same mistake
        // as describing a scene without clearing its people: the block adds, it
        // does not remove, so the model keeps the old lettering or blends the
        // two into a logo that never existed. Which sentence opens the block is
        // therefore decided by what the previous era actually put there.
        var previousSpec = previous?.LogoSpec;
        if (previous is null)
        {
            sb.AppendLine($"The store name across the facade reads \"{series.Brand}\", built exactly like this:");
        }
        else if (previousSpec is null)
        {
            // Nothing was on the building last era — a bare or resurfaced fascia.
            sb.AppendLine($"The facade in the uploaded photo carries no sign. Mount the store name " +
                           $"\"{series.Brand}\" across it, built exactly like this:");
        }
        else if (previousSpec.SequenceEqual(spec, StringComparer.Ordinal))
        {
            // Same logo as last era. Saying "take it down and put it back" would
            // invite a redraw of a sign that is already right, so this asks for
            // the opposite — and still states the letterforms, because the model
            // repaints the fascia either way and needs to know what it is copying.
            sb.AppendLine($"The store name across the facade is unchanged from the uploaded photo: same " +
                           $"sign, same place, same size. It reads \"{series.Brand}\" and is built like this:");
        }
        else
        {
            sb.AppendLine($"The lettering across the facade in the uploaded photo is the OLD sign and is " +
                           $"being replaced. Take it down completely — no ghost, no outline, no leftover " +
                           $"letter — and mount the new store name \"{series.Brand}\" in its place, built " +
                           $"exactly like this:");
        }

        foreach (var line in spec)
            sb.AppendLine($"- {line}");

        if (era.LogoRef is null)
            return;

        // The reference is a logo sheet, not a photograph of a store: without
        // this the model borrows its flat background and centred composition and
        // returns the logo rather than the building wearing it.
        sb.AppendLine("LOGO REFERENCE");
        sb.AppendLine("Reproduce the letterforms, proportions, spacing and slant from the attached " +
                       "reference image. Apply them as channel letters mounted on the facade at the size " +
                       "and position the building already gives the sign. Use the reference for the logo " +
                       "only — never its background, lighting, framing or composition.");
    }

    private static void AppendLogoFail(StringBuilder sb, BrandEra era)
    {
        if (era.LogoFail is not { Count: > 0 } fails)
            return;

        sb.AppendLine();
        sb.AppendLine("LOGO FAIL");
        sb.AppendLine("The sign is wrong if any of these describes it:");
        foreach (var line in fails)
            sb.AppendLine($"- {line}");
    }

    private static void AppendScene(StringBuilder sb, BrandEra era, Random rng)
    {
        sb.AppendLine();
        sb.AppendLine("SCENE");
        sb.AppendLine($"The place reads {era.Condition} — {era.Tone}.");
        sb.AppendLine($"The parking lot is {LotPhrase(era.LotOccupancy)}.");

        var neighbors = Sample(era.Neighbors, rng.Next(NeighborsMin, NeighborsMax + 1), rng);
        if (neighbors.Count > 0)
            sb.AppendLine($"Neighbouring units along the same frontage: {string.Join(", ", neighbors)}.");
    }

    // The series says how full the lot is in one word; this is what that word
    // looks like. The crowd is left to the PEOPLE block rather than stated here
    // too — the same fact in two places is two instructions to reconcile.
    private static string LotPhrase(string lotOccupancy) =>
        lotOccupancy.ToLowerInvariant() switch
        {
            "packed"   => "full — nearly every bay taken, cars queuing for the ones that are not",
            "busy"     => "busy — most bays taken, gaps scattered through it",
            "moderate" => "half full — bays taken in loose clusters, whole rows empty",
            "sparse"   => "nearly empty — a few vehicles scattered across a wide expanse of asphalt",
            "empty"    => "bare — not a vehicle on it, the bay striping running unbroken across it",
            _          => $"{lotOccupancy}"
        };

    // The classes are examples, not an inventory. "Every vehicle in the lot is
    // of the period: X, Y, Z" reads as a specification of the whole lot, and a
    // lot built from three named classes is three shapes repeated down the rows.
    // What actually has to hold is the date — nothing newer than the year — so
    // that is stated flatly and the models are left as a sample the rest of the
    // lot is drawn in keeping with.
    private static void AppendVehicles(StringBuilder sb, IReadOnlyList<string> classes)
    {
        if (classes.Count == 0)
            return;

        sb.AppendLine();
        sb.AppendLine("VEHICLES");
        sb.AppendLine($"The vehicles are of the period — for example {string.Join(", ", classes)}, " +
                       "or anything else in keeping with the year. Nothing newer than the year of the " +
                       "photograph appears anywhere in frame.");
    }

    // One uniqueness clause. The people pools are small and the same sentence
    // written three ways stops reading as emphasis and starts reading as three
    // separate demands the model has to reconcile.
    //
    // "empty" changes the block's shape rather than one adjective in it. A
    // deserted lot described as holding shoppers walking to the entrance is a
    // contradiction, and the model settles it by drawing the shoppers — so the
    // empty case states a place with nobody in it and stops, clothing included:
    // there is nobody left for it to dress.
    private static void AppendPeople(StringBuilder sb, BrandEra era, Random rng)
    {
        sb.AppendLine();
        sb.AppendLine("PEOPLE");

        if (IsDeserted(era.CrowdDensity))
        {
            sb.AppendLine("Nobody is in frame. No shoppers, no staff, no passers-by at the entrance, along " +
                           "the frontage or anywhere on the lot — bare paving, bare walkway, an unattended " +
                           "building.");
            return;
        }

        sb.AppendLine($"{CrowdPhrase(era.CrowdDensity)}, walking to and from the entrance and across the " +
                       "lot, each one doing something plainly ordinary.");

        var fashion = Sample(era.Fashion, FashionCount, rng);
        if (fashion.Count > 0)
            sb.AppendLine($"Period clothing and grooming: {string.Join(", ", fashion)}.");

        sb.AppendLine("Every person is a different individual — no repeated face, build, pose or outfit.");
    }

    private static bool IsDeserted(string crowdDensity) =>
        crowdDensity.Equals("empty", StringComparison.OrdinalIgnoreCase)
        || crowdDensity.Equals("deserted", StringComparison.OrdinalIgnoreCase);

    // Density as a group the model can draw, never as a count — it cannot
    // count, and the digits would cost budget for an instruction it ignores.
    private static string CrowdPhrase(string crowdDensity) =>
        crowdDensity.ToLowerInvariant() switch
        {
            "busy"     => "A steady stream of shoppers crosses the frontage",
            "packed"   => "A dense, uncountable crowd fills the frontage",
            "steady"   => "A regular flow of shoppers crosses the frontage",
            "moderate" => "A scattering of shoppers is spread across the frontage",
            "sparse"   => "A few people are spread thinly across the frontage",
            _          => $"The frontage is {crowdDensity}, with shoppers across it"
        };

    private static void AppendDetails(StringBuilder sb, BrandEra era, Random rng)
    {
        sb.AppendLine();
        sb.AppendLine("PERIOD DETAILS");

        var signage = Sample(era.Signage, SignageCount, rng);
        if (signage.Count > 0)
            sb.AppendLine("Store signage reading " +
                           string.Join(", ", signage.Select(s => $"\"{s}\"")) + ".");

        var advertising = Sample(era.Advertising, AdvertisingCount, rng);
        if (advertising.Count > 0)
            sb.AppendLine($"Period advertising and fittings: {string.Join(", ", advertising)}.");

        sb.AppendLine(PromptService.PlacementRule);

        // The quoted strings above are the whole whitelist. Without this the
        // model fills every other blank surface with invented lettering.
        sb.AppendLine("No other readable text appears anywhere in the image.");
    }

    // Reuses the retention arithmetic the photo path uses, so a decade of canopy
    // growth is the same amount of growth in both. TreeStage is the series' own
    // vocabulary and maps onto the recorded sizes that arithmetic expects.
    //
    // Only ever stated against the previous era. The first frame is drawn from
    // text with no image behind it, so there is nothing for a percentage to be a
    // percentage of — unlike the photo path, whose base came from a real scene
    // with real trees already in it.
    private static void AppendTrees(
        StringBuilder sb, BrandEra era, int year, int? chainedFrom, int? previousYear,
        GenerationContext context)
    {
        if (chainedFrom is null)
            return;

        // Measured against the last era that actually stated a size, not the era
        // before. A decade on a mature tree is about 5%, which no image model
        // draws; those eras stay silent and the growth accrues here instead of
        // being asked for five times and rendered none.
        const string key = "brand-series-trees";
        var anchor = context.TreeGrowthAnchor(key, previousYear ?? chainedFrom.Value);
        var description = PromptService.DescribeTreeSize(
            TreeSizeFor(era.TreeStage), position: null, year, anchor);

        // Empty when the uploaded image already shows the trees at the right
        // size — the same case that makes PromptService drop the whole section.
        if (description.Length == 0)
            return;

        context.RecordTreeGrowthStated(key, year);
        sb.AppendLine();
        sb.AppendLine("TREES");
        sb.AppendLine($"The trees in the landscaping islands and along the lot edge: {description}.");
        sb.AppendLine("Tree sizes MUST follow this specification even where they differ from the uploaded photo.");
    }

    // "young"/"medium"/"mature" onto the "small"/"medium"/"large" vocabulary the
    // retention rates are keyed by.
    private static string TreeSizeFor(string treeStage) =>
        treeStage.ToLowerInvariant() switch
        {
            "young"  => "small",
            "medium" => "medium",
            "mature" => "large",
            _        => "medium"
        };

    private static void AppendStyle(StringBuilder sb, BrandEra era)
    {
        sb.AppendLine();
        sb.AppendLine("PHOTOGRAPHIC STYLE");

        if (string.Equals(era.ColorMode, "black_and_white", StringComparison.OrdinalIgnoreCase))
        {
            sb.AppendLine("STRICTLY BLACK AND WHITE — true monochrome archival photograph, zero colour anywhere.");
            sb.AppendLine($"Look: {era.FilmStock}.");
            sb.AppendLine("Photorealistic, with slight period-lens softness rather than digital sharpness.");
            return;
        }

        sb.AppendLine($"COLOUR photograph — {era.FilmStock}.");
        sb.AppendLine("Photorealistic — NOT black-and-white, no HDR.");
    }

    // ── Sampling ──────────────────────────────────────────────────────────────

    // Vehicle classes go through the run's UsedCarModels set, the same memory
    // that stops a car model recurring in a photo-driven run: a class that has
    // already carried one era is not what the next decade should look like.
    // Falls back to already-used classes only if the era's pool is exhausted, so
    // an era is never left with no vehicles at all.
    private static List<string> PickVehicleClasses(BrandEra era, GenerationContext context, Random rng)
    {
        var shuffled = era.VehicleClasses.Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(_ => rng.Next()).ToList();
        var picks    = new List<string>();
        var leftover = new List<string>();

        foreach (var item in shuffled)
        {
            if (picks.Count >= VehicleClassCount) break;
            if (context.TryUseCarModel(item))
                picks.Add(item);
            else
                leftover.Add(item);
        }

        foreach (var item in leftover)
        {
            if (picks.Count >= VehicleClassCount) break;
            picks.Add(item);
        }

        return picks;
    }

    // Eligible in this exact year, one per category, and remembered across the
    // run in the same set the vehicle classes use — a chain that opened in one
    // era must not reappear as a new arrival in another.
    //
    // The category is the load-bearing part. The pool is heavily weighted (nine
    // of thirty-nine entries are gyms, five are thrift stores), so an unfiltered
    // draw of three put two competing gyms in adjacent bays of the same building
    // in more than half of runs. An empty category is treated as its own kind
    // and never blocks anything, which is what lets the gas and motel files
    // share this pool's parser without carrying the field.
    private const int TenantMin = 3;
    private const int TenantMax = 4;

    private static List<string> PickTenants(
        IReadOnlyList<(string Name, int From, int To, string Category)> pool, int year, Random rng, GenerationContext context)
    {
        var eligible = pool
            .Where(t => year >= t.From && year <= t.To)
            .DistinctBy(t => t.Name, StringComparer.OrdinalIgnoreCase)
            .OrderBy(_ => rng.Next())
            .ToList();

        var want       = rng.Next(TenantMin, TenantMax + 1);
        var picks      = new List<string>();
        var categories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var tenant in eligible)
        {
            if (picks.Count >= want) break;
            if (tenant.Category.Length > 0 && !categories.Add(tenant.Category)) continue;
            if (context.TryUseCarModel("tenant:" + tenant.Name))
                picks.Add(tenant.Name);
        }
        return picks;
    }

    // "A, B and C" — a list of shopfronts reads as a sentence, not as bullets.
    private static string Join(IReadOnlyList<string> names) =>
        names.Count <= 1
            ? string.Concat(names)
            : string.Join(", ", names.Take(names.Count - 1)) + " and " + names[^1];

    private static List<string> Sample(IEnumerable<string> pool, int count, Random rng) =>
        pool.Distinct().OrderBy(_ => rng.Next()).Take(count).ToList();

    // The era before this one in the series' own year list — the frame the
    // chained generation actually edits, and the gap CONTINUITY states.
    private static int? PreviousYear(BrandSeries series, GenerationContext context, int year)
    {
        var years = context.Years.Count > 0 ? context.Years : series.Years;
        var index = years.ToList().IndexOf(year);
        return index > 0 ? years[index - 1] : null;
    }
}
