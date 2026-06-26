using LifeOverYears.Models;

namespace LifeOverYears.Services.Interfaces;

public interface IFfmpegProvider
{
    Task<Video> ComposeAsync(IReadOnlyList<HistoricalImage> images);
}
