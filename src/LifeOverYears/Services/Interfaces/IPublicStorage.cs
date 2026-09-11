namespace LifeOverYears.Services.Interfaces;

// A place to put a file where a platform can fetch it by URL with no
// credentials. Exists because Instagram and Facebook do not accept an upload
// — they take a link and pull the bytes themselves — so a publish to either
// is really two steps, and this is the first.
public interface IPublicStorage
{
    // Uploads and returns a direct-download URL. Uploading the same remote
    // name again overwrites; the URL stays stable.
    Task<string> UploadPublicAsync(string localPath, string remoteName, CancellationToken ct = default);
}
