namespace LifeOverYears.Models;

// One era of a brand series. The three logo fields are nullable together: an
// era after the sign came down has no logo to reproduce and no way to get one
// wrong, and says so through SignRemoved/Redeveloped instead.
public record BrandEra
{
    // Path to the era's logo image, relative to data/. Null once there is no
    // sign on the building.
    public string? LogoRef { get; init; }

    // The letterforms in words. The reference image is the primary source, but
    // an image model reproduces a logo far more reliably when the same shapes
    // are also stated, and a missing reference file leaves the spec standing.
    public IReadOnlyList<string>? LogoSpec { get; init; }

    // The wrong logos — most of them this brand's own, from a decade the frame
    // is not set in. Written as a checklist of what would make the sign wrong,
    // never as an instruction, because a bare negation is the one thing an
    // image model reliably ignores.
    public IReadOnlyList<string>? LogoFail { get; init; }

    // The sign has come down. The prompt has to say so explicitly and describe
    // what is left in its place: under era chaining the uploaded frame still
    // carries the lettering, and a block that merely stops mentioning the logo
    // cannot remove it.
    public bool SignRemoved { get; init; }

    // The building is still there and still the same footprint, but the
    // frontage has been carved into separate tenants and carries none of the
    // original signage or its hardware.
    public bool Redeveloped { get; init; }

    public required string Condition { get; init; }

    // Density is words only, never a count: the image model does not count, and
    // the digits cost prompt budget that is already tight.
    public required string LotOccupancy { get; init; }
    public required string CrowdDensity { get; init; }

    public required string ColorMode { get; init; }
    public required string FilmStock { get; init; }

    // "young" / "medium" / "mature" — mapped onto the recorded-size vocabulary
    // PromptService.DescribeTreeSize expects, so canopy growth between two eras
    // is phrased the same way it is in a photo-driven run.
    public required string TreeStage { get; init; }

    public required IReadOnlyList<string> VehicleClasses { get; init; }
    public required IReadOnlyList<string> Fashion { get; init; }
    public required IReadOnlyList<string> Signage { get; init; }
    public required IReadOnlyList<string> Advertising { get; init; }
    public required IReadOnlyList<string> Neighbors { get; init; }
    public required string Tone { get; init; }
}
