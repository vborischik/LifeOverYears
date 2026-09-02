using LifeOverYears.Models;

namespace LifeOverYears.Services.Interfaces;

public interface IRunService
{
    Task<RunFolder> CreateRunAsync(SceneDna sceneDna, string sourcePhotoPath, IReadOnlyList<int> years);

    // A brand series has no source photograph to copy in, so it cannot go
    // through CreateRunAsync. Everything else about the folder is identical —
    // same subfolders, same run.json, same scene.json — because every step
    // after the prompt is the ordinary path and must not be able to tell the
    // two apart. SourcePath on the returned folder points at the series file.
    Task<RunFolder> CreateBrandRunAsync(BrandSeries series, IReadOnlyList<int> years);
}
