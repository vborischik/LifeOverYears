using LifeOverYears.Models;

namespace LifeOverYears.Services.Interfaces;

public interface IRunService
{
    Task<RunFolder> CreateRunAsync(string sceneId, string sourcePhotoPath);
}
