using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using ApurvaSpotify.Core;

namespace ApurvaSpotify.Desktop;

/// <summary>Generic credentials protected by Windows Credential Manager. No token files.</summary>
internal sealed class WindowsTokenStore : ITokenStore
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct Credential
    {
        public uint Flags;
        public uint Type;
        public string TargetName;
        public string? Comment;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
        public uint CredentialBlobSize;
        public IntPtr CredentialBlob;
        public uint Persist;
        public uint AttributeCount;
        public IntPtr Attributes;
        public string? TargetAlias;
        public string? UserName;
    }
    [DllImport("advapi32.dll", EntryPoint = "CredWriteW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool CredWrite(ref Credential credential, uint flags);
    [DllImport("advapi32.dll", EntryPoint = "CredReadW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool CredRead(string target, uint type, uint flags, out IntPtr credential);
    [DllImport("advapi32.dll", EntryPoint = "CredDeleteW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool CredDelete(string target, uint type, uint flags);
    [DllImport("advapi32.dll")] private static extern void CredFree(IntPtr credential);
    private static string Target(string clientId) => "ApurvaSpotify/" + SpotifyAuth.ValidateClientId(clientId);

    public Task<Tokens?> LoadAsync(string clientId, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (!CredRead(Target(clientId), 1, 0, out var pointer))
        {
            if (Marshal.GetLastWin32Error() == 1168) return Task.FromResult<Tokens?>(null);
            throw new InvalidOperationException("Windows could not read your saved Spotify sign-in.");
        }
        byte[]? bytes = null;
        try
        {
            var credential = Marshal.PtrToStructure<Credential>(pointer);
            bytes = new byte[checked((int)credential.CredentialBlobSize)];
            Marshal.Copy(credential.CredentialBlob, bytes, 0, bytes.Length);
            return Task.FromResult(JsonSerializer.Deserialize<Tokens>(bytes));
        }
        finally { if (bytes is not null) Array.Clear(bytes); CredFree(pointer); }
    }
    public Task SaveAsync(Tokens tokens, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var bytes = JsonSerializer.SerializeToUtf8Bytes(tokens);
        if (bytes.Length > 2560) throw new InvalidOperationException("Spotify sign-in is too large for Windows credential storage.");
        var buffer = Marshal.AllocHGlobal(bytes.Length);
        try
        {
            Marshal.Copy(bytes, 0, buffer, bytes.Length);
            var credential = new Credential
            {
                Type = 1, TargetName = Target(tokens.ClientId), CredentialBlobSize = (uint)bytes.Length,
                CredentialBlob = buffer, Persist = 2, UserName = "Spotify OAuth"
            };
            if (!CredWrite(ref credential, 0)) throw new InvalidOperationException("Windows could not save your Spotify sign-in.");
        }
        finally
        {
            Array.Clear(bytes); Marshal.Copy(bytes, 0, buffer, bytes.Length); Marshal.FreeHGlobal(buffer);
        }
        return Task.CompletedTask;
    }
    public Task DeleteAsync(string clientId, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (!CredDelete(Target(clientId), 1, 0) && Marshal.GetLastWin32Error() != 1168)
            throw new InvalidOperationException("Windows could not remove your saved Spotify sign-in.");
        return Task.CompletedTask;
    }
}

internal sealed record Preferences(string ClientId = "", int ArtistSpacing = 2)
{
    private static string Path => System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ApurvaSpotify", "preferences.json");
    public static Preferences Load()
    {
        try { return File.Exists(Path) ? JsonSerializer.Deserialize<Preferences>(File.ReadAllText(Path)) ?? new() : new(); }
        catch (Exception e) when (e is IOException or JsonException) { return new(); }
    }
    public void Save()
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
        File.WriteAllText(Path + ".tmp", JsonSerializer.Serialize(this), Encoding.UTF8);
        File.Move(Path + ".tmp", Path, overwrite: true);
    }
}
