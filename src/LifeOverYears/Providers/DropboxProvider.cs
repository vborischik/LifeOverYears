using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using LifeOverYears.Services.Interfaces;
using Microsoft.Extensions.Logging;

namespace LifeOverYears.Providers;

// The storage step behind Instagram and Facebook: upload, share, hand back a
// link the platform can pull from. The flow — upload, create_shared_link,
// rewrite the share page URL into a direct-download one — is the one
// HouseTimelineApp debugged against the live API. The June 2026 sketch in
// this repo sent no Authorization header at all.
//
// Auth is a refresh token, not an access token. Dropbox access tokens live
// four hours; the reference app was carrying hard-coded ones and every run
// after lunch needed a new paste. With an app key, secret and refresh token
// this mints its own access token on first use and again when it expires,
// which is what lets a scheduled publisher run unattended. A plain access
// token still works for a one-off — it is just never refreshed.
public sealed class DropboxProvider : IPublicStorage
{
    private const string ApiBase     = "https://api.dropboxapi.com/2";
    private const string ContentBase = "https://content.dropboxapi.com/2";
    private const string TokenUrl    = "https://api.dropboxapi.com/oauth2/token";

    // Under files/upload's single-request limit (150 MB) by two orders of
    // magnitude — a 16-second 1080x1920 clip is about a megabyte — so the
    // session-based upload is not needed.
    public const long MaxSingleUploadBytes = 150L * 1024 * 1024;

    private readonly HttpClient _http;
    private readonly ILogger<DropboxProvider> _logger;
    private readonly DropboxAuth _auth;
    private readonly string _folder;

    private string? _accessToken;
    private DateTimeOffset _accessTokenExpiry = DateTimeOffset.MinValue;

    // folder is the Dropbox path everything lands under, "/LifeOverYears".
    public DropboxProvider(HttpClient http, DropboxAuth auth, string folder, ILogger<DropboxProvider> logger)
    {
        _http   = http;
        _logger = logger;
        _auth   = auth;
        _folder = "/" + folder.Trim('/');
    }

    public async Task<string> UploadPublicAsync(string localPath, string remoteName, CancellationToken ct = default)
    {
        if (!File.Exists(localPath))
            throw new FileNotFoundException("Nothing to upload", localPath);

        var size = new FileInfo(localPath).Length;
        if (size > MaxSingleUploadBytes)
            throw new InvalidOperationException(
                $"{localPath} is {size} bytes, over Dropbox's {MaxSingleUploadBytes} single-request limit — a session upload is needed");

        var remotePath = $"{_folder}/{remoteName.TrimStart('/')}";
        _logger.LogInformation("Uploading {Local} → Dropbox:{Remote}", localPath, remotePath);

        await UploadAsync(localPath, remotePath, ct);
        var url = await SharedLinkAsync(remotePath, ct);

        _logger.LogInformation("Dropbox public link {Url}", url);
        return url;
    }

    private async Task UploadAsync(string localPath, string remotePath, CancellationToken ct)
    {
        await using var stream = File.OpenRead(localPath);
        var content = new StreamContent(stream);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");

        var request = new HttpRequestMessage(HttpMethod.Post, $"{ContentBase}/files/upload") { Content = content };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await AccessTokenAsync(ct));
        // Overwrite, not autorename: the same run re-published lands on the
        // same path, and the shared link created below keeps pointing at it.
        request.Headers.Add("Dropbox-API-Arg", JsonSerializer.Serialize(new
        {
            path       = remotePath,
            mode       = "overwrite",
            autorename = false,
            mute       = true,
        }));

        var response = await _http.SendAsync(request, ct);
        await EnsureSuccessAsync(response, "files/upload", ct);
    }

    // create_shared_link_with_settings answers 409 shared_link_already_exists
    // the second time for the same path, and the existing link is inside that
    // error body — so a 409 here is the success path for a re-publish.
    private async Task<string> SharedLinkAsync(string remotePath, CancellationToken ct)
    {
        var response = await PostJsonAsync($"{ApiBase}/sharing/create_shared_link_with_settings", new
        {
            path     = remotePath,
            settings = new
            {
                requested_visibility = new Dictionary<string, string> { [".tag"] = "public" },
                audience             = new Dictionary<string, string> { [".tag"] = "public" },
                access               = new Dictionary<string, string> { [".tag"] = "viewer" },
            },
        }, ct);

        var body = await response.Content.ReadAsStringAsync(ct);

        if (response.StatusCode == HttpStatusCode.Conflict)
        {
            using var err = JsonDocument.Parse(body);
            if (err.RootElement.TryGetProperty("error", out var error)
                && error.TryGetProperty("shared_link_already_exists", out var existing)
                && existing.TryGetProperty("metadata", out var metadata)
                && metadata.TryGetProperty("url", out var url))
                return ToDirectLink(url.GetString()!);

            throw new InvalidOperationException($"Dropbox sharing conflict without a link in it: {body}");
        }

        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"Dropbox create_shared_link failed {(int)response.StatusCode}: {body}");

        using var doc = JsonDocument.Parse(body);
        return ToDirectLink(doc.RootElement.GetProperty("url").GetString()
            ?? throw new InvalidOperationException("Dropbox returned a shared link with no url"));
    }

    // A shared link is a preview page. Both rewrites turn it into the file
    // itself, which is what a platform fetching by URL needs; either alone
    // is not enough on the newer /scl/fi/ link shape.
    public static string ToDirectLink(string sharedUrl) =>
        sharedUrl
            .Replace("www.dropbox.com", "dl.dropboxusercontent.com", StringComparison.Ordinal)
            .Replace("dl=0", "dl=1", StringComparison.Ordinal);

    // ── Auth ─────────────────────────────────────────────────────────────────

    private async Task<string> AccessTokenAsync(CancellationToken ct)
    {
        if (_accessToken is not null && DateTimeOffset.UtcNow < _accessTokenExpiry)
            return _accessToken;

        if (_auth.RefreshToken is null)
        {
            _accessToken       = _auth.AccessToken
                ?? throw new InvalidOperationException("Dropbox: neither a refresh token nor an access token is configured");
            _accessTokenExpiry = DateTimeOffset.MaxValue;
            return _accessToken;
        }

        _logger.LogInformation("Refreshing Dropbox access token");
        using var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"]    = "refresh_token",
            ["refresh_token"] = _auth.RefreshToken,
            ["client_id"]     = _auth.AppKey     ?? throw new InvalidOperationException("Dropbox: AppKey is required with a refresh token"),
            ["client_secret"] = _auth.AppSecret  ?? throw new InvalidOperationException("Dropbox: AppSecret is required with a refresh token"),
        });

        var response = await _http.PostAsync(TokenUrl, form, ct);
        var body     = await EnsureSuccessAsync(response, "oauth2/token", ct);

        using var doc = JsonDocument.Parse(body);
        _accessToken = doc.RootElement.GetProperty("access_token").GetString()
            ?? throw new InvalidOperationException("Dropbox token response carried no access_token");

        // Renew a minute early rather than on the second: an upload that
        // starts at expiry minus one second fails with a token that was valid
        // when it was checked.
        var seconds = doc.RootElement.TryGetProperty("expires_in", out var exp) ? exp.GetInt32() : 14400;
        _accessTokenExpiry = DateTimeOffset.UtcNow.AddSeconds(seconds - 60);
        return _accessToken;
    }

    // ── HTTP ─────────────────────────────────────────────────────────────────

    private async Task<HttpResponseMessage> PostJsonAsync(string url, object body, CancellationToken ct)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json"),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await AccessTokenAsync(ct));
        return await _http.SendAsync(request, ct);
    }

    private static async Task<string> EnsureSuccessAsync(HttpResponseMessage response, string op, CancellationToken ct)
    {
        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"Dropbox {op} failed {(int)response.StatusCode}: {body}");
        return body;
    }
}

// Either a refresh-token triple (unattended, renews itself) or a bare access
// token (four hours, then a new paste). Both nullable so the config can carry
// whichever the operator has.
public sealed record DropboxAuth(
    string? AppKey,
    string? AppSecret,
    string? RefreshToken,
    string? AccessToken);
