using System.Net.Http.Headers;
using System.Text.Json;
using LifeOverYears.Services.Interfaces;
using Microsoft.Extensions.Logging;

namespace LifeOverYears.Providers;

public sealed class DropboxProvider : IDropboxProvider
{
    private readonly HttpClient _http;
    private readonly ILogger<DropboxProvider> _logger;

    private const string UploadUrl   = "https://content.dropboxapi.com/2/files/upload";
    private const string DownloadUrl = "https://content.dropboxapi.com/2/files/download";

    public DropboxProvider(HttpClient http, ILogger<DropboxProvider> logger)
    {
        _http = http;
        _logger = logger;
    }

    public async Task<string> UploadAsync(string filePath)
    {
        var remotePath = $"/LifeOverYears/{Path.GetFileName(filePath)}";
        _logger.LogInformation("Uploading {Local} → Dropbox:{Remote}", filePath, remotePath);

        var bytes = await File.ReadAllBytesAsync(filePath);
        using var content = new ByteArrayContent(bytes);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");

        var request = new HttpRequestMessage(HttpMethod.Post, UploadUrl) { Content = content };
        request.Headers.Add("Dropbox-API-Arg", JsonSerializer.Serialize(new
        {
            path      = remotePath,
            mode      = "overwrite",
            autorename = false,
            mute      = false
        }));

        var response = await _http.SendAsync(request);
        response.EnsureSuccessStatusCode();

        _logger.LogInformation("Uploaded to Dropbox: {Remote}", remotePath);
        return $"dropbox://{remotePath}";
    }

    public async Task<string> DownloadAsync(string remotePath)
    {
        _logger.LogInformation("Downloading Dropbox:{Remote}", remotePath);

        var request = new HttpRequestMessage(HttpMethod.Post, DownloadUrl);
        request.Headers.Add("Dropbox-API-Arg", JsonSerializer.Serialize(new { path = remotePath }));
        request.Content = new StringContent(string.Empty);

        var response = await _http.SendAsync(request);
        response.EnsureSuccessStatusCode();

        var localPath = Path.Combine(Path.GetTempPath(), Path.GetFileName(remotePath));
        Directory.CreateDirectory(Path.GetDirectoryName(localPath)!);
        await using var fs = File.Create(localPath);
        await response.Content.CopyToAsync(fs);

        _logger.LogInformation("Downloaded to {Local}", localPath);
        return localPath;
    }
}
