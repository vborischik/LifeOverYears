namespace LifeOverYears.Services.Interfaces;

// Pure HTTP connector for OpenAI's image edits endpoint. Knows nothing about
// domain objects or the run folder — mirrors INvidiaProvider's role.
public interface IOpenAiProvider
{
    // POST /v1/images/edits (multipart). Sends referenceImage as the image to
    // edit plus the prompt; returns the decoded PNG bytes of the result.
    Task<byte[]> EditImageAsync(
        byte[] referenceImage, string prompt, string size, string quality,
        CancellationToken ct = default);
}
