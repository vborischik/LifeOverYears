using LifeOverYears.Models;

namespace LifeOverYears.Services.Interfaces;

public interface IRunService
{
    Task<RunFolder> CreateRunAsync(SceneDna sceneDna, string sourcePhotoPath, IReadOnlyList<int> years);
}
