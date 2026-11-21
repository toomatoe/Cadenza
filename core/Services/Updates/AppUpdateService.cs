using System.Globalization;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Cadenza.Core.Exceptions;
using Cadenza.Core.Interfaces;
using Cadenza.Core.Models;

namespace Cadenza.Core.Services.Updates;

/// <summary>No polling or extra dependencies. Downloads only Cadenza's published GitHub assets.</summary>
public sealed class AppUpdateService : IAppUpdateService
{
    private const string ReleasesUrl = "https://api.github.com/repos/toomatoe/Cadenza/releases?per_page=30";
    private const string AssetPrefix = "/toomatoe/Cadenza/releases/download/";
    private const string InstallerName = "Cadenza-Setup.exe";
    private const long MaxInstallerSize = 300 * 1024 * 1024;
    private const int MaxMetadataSize = 2 * 1024 * 1024;
    private const int MaxChecksumSize = 1024;
    private const int BufferSize = 65536;
    private readonly HttpClient _http;

    public AppUpdateService(HttpClient http) => _http = http;

    public async Task<AppUpdate?> CheckAsync(string currentVersion, bool includePrereleases, CancellationToken cancellationToken = default)
    {
        if (!TryVersion(currentVersion, out _, out _))
            throw new AppUpdateException("This build has no valid version. Install a versioned release first.");

        using var request = new HttpRequestMessage(HttpMethod.Get, ReleasesUrl);
        request.Headers.UserAgent.ParseAdd("Cadenza-Updater/1.0");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var metadata = await ReadLimitedAsync(response.Content, MaxMetadataSize, cancellationToken).ConfigureAwait(false);
        using var document = JsonDocument.Parse(metadata);
        AppUpdate? latest = null;
        foreach (var release in document.RootElement.EnumerateArray())
        {
            if (release.GetProperty("draft").GetBoolean() || (!includePrereleases && release.GetProperty("prerelease").GetBoolean()))
                continue;

            var version = release.GetProperty("tag_name").GetString() ?? "";
            if (!TryVersion(version, out _, out var prerelease) || (!includePrereleases && prerelease.Length > 0)
                || Compare(version, currentVersion) <= 0 || (latest is not null && Compare(version, latest.Version) <= 0))
                continue;

            var assets = release.GetProperty("assets").EnumerateArray().ToArray();
            var installer = assets.FirstOrDefault(asset => asset.GetProperty("name").GetString() == InstallerName);
            var checksum = assets.FirstOrDefault(asset => asset.GetProperty("name").GetString() == InstallerName + ".sha256");
            if (installer.ValueKind == JsonValueKind.Undefined || checksum.ValueKind == JsonValueKind.Undefined)
                continue;

            var size = installer.GetProperty("size").GetInt64();
            if (size <= 0 || size > MaxInstallerSize)
                continue;

            var installerUrl = AssetUri(installer.GetProperty("browser_download_url").GetString() ?? "", version, InstallerName);
            var checksumUrl = AssetUri(checksum.GetProperty("browser_download_url").GetString() ?? "", version, InstallerName + ".sha256");
            latest = new AppUpdate(version, installerUrl, checksumUrl, size);
        }

        return latest;
    }

    public async Task<string> DownloadAsync(AppUpdate update, string directory, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(update);
        AssetUri(update.InstallerUrl.AbsoluteUri, update.Version, InstallerName);
        AssetUri(update.ChecksumUrl.AbsoluteUri, update.Version, InstallerName + ".sha256");
        if (update.Size <= 0 || update.Size > MaxInstallerSize)
            throw new AppUpdateException("The update is larger than the supported download limit.");

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(5));
        var token = timeout.Token;
        using var checksumResponse = await _http.GetAsync(update.ChecksumUrl, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
        checksumResponse.EnsureSuccessStatusCode();
        var checksumText = Encoding.UTF8.GetString(await ReadLimitedAsync(checksumResponse.Content, MaxChecksumSize, token).ConfigureAwait(false));
        var checksumParts = checksumText.Trim().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (checksumParts.Length != 2 || checksumParts[1] != InstallerName || checksumParts[0].Length != 64 || !checksumParts[0].All(Uri.IsHexDigit))
            throw new AppUpdateException("The release checksum is invalid. The update was not installed.");

        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, InstallerName);
        var partial = path + ".part";
        try
        {
            using var response = await _http.GetAsync(update.InstallerUrl, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength is long length && length != update.Size)
                throw new AppUpdateException("The update download size doesn't match the release.");

            await using (var input = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false))
            await using (var output = new FileStream(partial, FileMode.Create, FileAccess.Write, FileShare.None, BufferSize, useAsync: true))
            using (var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
            {
                var buffer = new byte[BufferSize];
                long total = 0;
                int read;
                while ((read = await input.ReadAsync(buffer, token).ConfigureAwait(false)) > 0)
                {
                    total += read;
                    if (total > update.Size)
                        throw new AppUpdateException("The update download exceeded its expected size.");

                    hash.AppendData(buffer, 0, read);
                    await output.WriteAsync(buffer.AsMemory(0, read), token).ConfigureAwait(false);
                }

                if (total != update.Size || !Convert.ToHexString(hash.GetHashAndReset()).Equals(checksumParts[0], StringComparison.OrdinalIgnoreCase))
                    throw new AppUpdateException("The update failed verification. Retry the download.");
            }

            token.ThrowIfCancellationRequested();
            File.Move(partial, path, overwrite: true);
        }
        finally
        {
            File.Delete(partial);
        }

        return path;
    }

    private static Uri AssetUri(string url, string version, string fileName)
    {
        var expectedPath = AssetPrefix + Uri.EscapeDataString(version) + "/" + fileName;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps
            || uri.Host != "github.com" || !uri.IsDefaultPort || uri.UserInfo.Length > 0
            || uri.AbsolutePath != expectedPath || uri.Query.Length > 0 || uri.Fragment.Length > 0)
            throw new AppUpdateException("The release contains an unexpected download address.");

        return uri;
    }

    private static async Task<byte[]> ReadLimitedAsync(HttpContent content, int limit, CancellationToken cancellationToken)
    {
        await using var input = await content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var output = new MemoryStream();
        var buffer = new byte[8192];
        int read;
        while ((read = await input.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
        {
            if (output.Length + read > limit)
                throw new AppUpdateException("The release response exceeded its expected size.");

            output.Write(buffer, 0, read);
        }

        return output.ToArray();
    }

    private static bool TryVersion(string text, out Version number, out string prerelease)
    {
        var parts = text.TrimStart('v').Split('+')[0].Split('-', 2);
        prerelease = parts.Length == 2 ? parts[1] : "";
        number = new Version(0, 0, 0);
        if (parts[0].Split('.').Length != 3 || !Version.TryParse(parts[0], out var parsed)
            || (parts.Length == 2 && (prerelease.Length == 0 || prerelease.Split('.').Any(part => part.Length == 0 || !part.All(c => char.IsAsciiLetterOrDigit(c) || c == '-')))))
            return false;

        number = parsed;

        return true;
    }

    private static int Compare(string left, string right)
    {
        TryVersion(left, out var leftNumber, out var leftPre);
        TryVersion(right, out var rightNumber, out var rightPre);
        var numeric = leftNumber.CompareTo(rightNumber);
        if (numeric != 0)
            return numeric;
        if (leftPre.Length == 0 || rightPre.Length == 0)
            return leftPre.Length == rightPre.Length ? 0 : leftPre.Length == 0 ? 1 : -1;

        var leftParts = leftPre.Split('.');
        var rightParts = rightPre.Split('.');
        for (var index = 0; index < Math.Min(leftParts.Length, rightParts.Length); index++)
        {
            var leftNumeric = long.TryParse(leftParts[index], NumberStyles.None, CultureInfo.InvariantCulture, out var leftValue);
            var rightNumeric = long.TryParse(rightParts[index], NumberStyles.None, CultureInfo.InvariantCulture, out var rightValue);
            var compared = leftNumeric && rightNumeric ? leftValue.CompareTo(rightValue)
                : leftNumeric != rightNumeric ? leftNumeric ? -1 : 1
                : string.CompareOrdinal(leftParts[index], rightParts[index]);
            if (compared != 0)
                return compared;
        }

        return leftParts.Length.CompareTo(rightParts.Length);
    }
}
