using LifeOverYears.Models;

namespace LifeOverYears.Services.Interfaces;

public interface ITelegramProvider
{
    Task<Publication> SendVideoAsync(Video video, Caption caption);
}
