namespace Cadenza.Core.Models;

/// <summary>A Windows installer and checksum from a published Cadenza release.</summary>
public sealed class AppUpdate
{
    public AppUpdate(string version, Uri installerUrl, Uri checksumUrl, long size)
    {
        Version = version;
        InstallerUrl = installerUrl;
        ChecksumUrl = checksumUrl;
        Size = size;
    }

    public string Version { get; }
    public Uri InstallerUrl { get; }
    public Uri ChecksumUrl { get; }
    public long Size { get; }
}
