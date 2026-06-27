namespace LifeOverYears.Services.Interfaces;

public interface IFileSystemProvider
{
    Task<string> ReadAllTextAsync(string path);
    Task WriteAllTextAsync(string path, string content);
    Task DeleteAsync(string path);
    Task<bool> ExistsAsync(string path);
    Task<IEnumerable<string>> ListFilesAsync(string directory, string pattern = "*");
    Task EnsureDirectoryExistsAsync(string path);
}
