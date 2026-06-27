namespace LifeOverYears.Services.Interfaces;

public interface INvidiaProvider
{
    Task<string> PostAsync(string url, object body);
    Task<string> PollAsync(string url, int timeoutSeconds = 120);
}
