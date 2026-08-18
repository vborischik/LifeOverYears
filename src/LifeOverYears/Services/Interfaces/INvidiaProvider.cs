namespace LifeOverYears.Services.Interfaces;

public interface INvidiaProvider
{
    Task<string> PostAsync(string url, object body);

    // Server-sent events. Returns each `data:` payload in order with the SSE
    // framing removed and the terminating [DONE] sentinel dropped; knows nothing
    // about what the payloads contain — assembling them back into an answer is
    // the calling domain provider's job.
    Task<IReadOnlyList<string>> PostStreamAsync(string url, object body);

    Task<string> PollAsync(string url, int timeoutSeconds = 120);
}
