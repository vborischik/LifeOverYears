namespace LifeOverYears.Services.Interfaces;

public interface IXaiProvider
{
    Task<string> CompleteAsync(string prompt);
}
