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

    // POST /v1/images/generations (JSON). No reference image — the prompt is
    // the only input. Returns the decoded PNG bytes.
    Task<byte[]> GenerateImageAsync(string prompt, string size, string quality,
        CancellationToken ct = default);

    // Files API: upload bytes, return the file id.
    Task<string> UploadFileAsync(byte[] content, string fileName, string purpose,
        CancellationToken ct = default);

    // Batch API: create a batch over an already-uploaded .jsonl input file.
    Task<string> CreateBatchAsync(string inputFileId, string endpoint,
        CancellationToken ct = default);

    // Batch API: current status plus result/error file ids once terminal.
    Task<(string Status, string? OutputFileId, string? ErrorFileId)> GetBatchAsync(
        string batchId, CancellationToken ct = default);

    // Files API: an uploaded file reports "uploaded", "processed" or "error".
    // Referencing one before it is processed is what the Batch API appears to
    // choke on, so the batch provider waits on this before creating a batch.
    Task<string> GetFileStatusAsync(string fileId, CancellationToken ct = default);

    // Files API: download file content as raw text.
    Task<string> DownloadFileContentAsync(string fileId, CancellationToken ct = default);
}
