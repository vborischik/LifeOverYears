using LifeOverYears.Models;

namespace LifeOverYears.Services.Interfaces;

public interface IVisionProvider
{
    Task<SceneDna> AnalyzeImageAsync(string photoPath, string prompt);
    Task<SceneDna> EnrichAsync(string photoPath, SceneDna current, IReadOnlyList<string> missingFields);

    // A second look at the handful of fields that are cheap to get wrong and
    // expensive to render wrong. Enrich fills in what is missing; this one
    // re-examines what is already there, because the damaging failure is a
    // confident wrong answer, not a blank.
    Task<SceneDna> VerifyAsync(string photoPath, SceneDna current);
}
