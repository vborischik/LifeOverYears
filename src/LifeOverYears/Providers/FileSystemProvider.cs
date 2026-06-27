using LifeOverYears.Services.Interfaces;
using Microsoft.Extensions.Logging;

namespace LifeOverYears.Providers;

public sealed class FileSystemProvider : IFileSystemProvider
{
    private readonly ILogger<FileSystemProvider> _logger;

    public FileSystemProvider(ILogger<FileSystemProvider> logger)
    {
        _logger = logger;
    }

    public async Task<string> ReadAllTextAsync(string path)
    {
        _logger.LogDebug("Reading file: {Path}", path);
        return await File.ReadAllTextAsync(path);
    }

    public async Task WriteAllTextAsync(string path, string content)
    {
        _logger.LogDebug("Writing file: {Path}", path);
        await EnsureDirectoryExistsAsync(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, content);
    }

    public Task DeleteAsync(string path)
    {
        _logger.LogDebug("Deleting file: {Path}", path);
        File.Delete(path);
        return Task.CompletedTask;
    }

    public Task<bool> ExistsAsync(string path)
        => Task.FromResult(File.Exists(path));

    public Task<IEnumerable<string>> ListFilesAsync(string directory, string pattern = "*")
    {
        if (!Directory.Exists(directory))
            return Task.FromResult(Enumerable.Empty<string>());

        return Task.FromResult<IEnumerable<string>>(
            Directory.EnumerateFiles(directory, pattern));
    }

    public Task EnsureDirectoryExistsAsync(string path)
    {
        if (!string.IsNullOrEmpty(path))
            Directory.CreateDirectory(path);
        return Task.CompletedTask;
    }
}
