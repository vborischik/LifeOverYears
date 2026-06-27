using LifeOverYears.Models;

namespace LifeOverYears.Services.Interfaces;

public interface IImageProvider
{
    Task<HistoricalImage> GenerateImageAsync(Prompt prompt);
}
