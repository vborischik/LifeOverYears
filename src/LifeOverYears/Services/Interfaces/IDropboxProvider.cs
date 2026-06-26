namespace LifeOverYears.Services.Interfaces;

public interface IDropboxProvider
{
    Task<string> UploadAsync(string filePath);
    Task<string> DownloadAsync(string remotePath);
}
