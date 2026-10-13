using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text.Json;
using Cadenza.Core.Models;
using Cadenza.Core.Services.Contracts;
using Cadenza.Core.Services.Spotify;

namespace Cadenza.Desktop.Services;

/// <summary>Stores Spotify tokens as generic credentials in Windows Credential Manager. No token files.</summary>
public sealed class WindowsTokenStore : ITokenStore
{
    private const uint CredTypeGeneric = 1;
    private const uint CredPersistLocalMachine = 2;
    private const int ErrorNotFound = 1168;

    // CRED_MAX_CREDENTIAL_BLOB_SIZE is 5 * 512 bytes.
    private const int MaxBlobBytes = 2560;
    private const string TargetPrefix = "Cadenza/";
    private const string FriendsTarget = TargetPrefix + "Friends";

    public Task<Tokens?> LoadAsync(string clientId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var bytes = Read(Target(clientId), "Spotify sign-in");
        try
        {
            return Task.FromResult(bytes is null ? null : JsonSerializer.Deserialize<Tokens>(bytes));
        }
        finally
        {
            if (bytes is not null)
            {
                Array.Clear(bytes);
            }
        }
    }

    /// <summary>The Cadenza Friends device token, or null when you haven't joined on this PC.</summary>
    public static string? LoadFriendsToken()
    {
        var bytes = Read(FriendsTarget, "Cadenza Friends sign-in");
        return bytes is null ? null : System.Text.Encoding.UTF8.GetString(bytes);
    }

    public static void SaveFriendsToken(string token) =>
        Write(FriendsTarget, System.Text.Encoding.UTF8.GetBytes(token), "Cadenza Friends", "Cadenza Friends sign-in");

    public static void DeleteFriendsToken() => Delete(FriendsTarget, "Cadenza Friends sign-in");

    public Task SaveAsync(Tokens tokens, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(tokens);
        Write(Target(tokens.ClientId), JsonSerializer.SerializeToUtf8Bytes(tokens), "Spotify OAuth", "Spotify sign-in");
        return Task.CompletedTask;
    }

    public Task DeleteAsync(string clientId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Delete(Target(clientId), "Spotify sign-in");
        return Task.CompletedTask;
    }

    private static byte[]? Read(string target, string what)
    {
        if (!NativeMethods.CredRead(target, CredTypeGeneric, 0, out var pointer))
        {
            return Marshal.GetLastWin32Error() == ErrorNotFound
                ? null
                : throw new InvalidOperationException($"Windows could not read your saved {what}.");
        }

        try
        {
            var credential = Marshal.PtrToStructure<NativeMethods.Credential>(pointer);
            var bytes = new byte[checked((int)credential.CredentialBlobSize)];
            Marshal.Copy(credential.CredentialBlob, bytes, 0, bytes.Length);
            return bytes;
        }
        finally
        {
            NativeMethods.CredFree(pointer);
        }
    }

    /// <summary>Writes a secret, then overwrites the managed and unmanaged copies before releasing them.</summary>
    private static void Write(string target, byte[] bytes, string userName, string what)
    {
        if (bytes.Length > MaxBlobBytes)
        {
            throw new InvalidOperationException($"Your {what} is too large for Windows credential storage.");
        }

        var buffer = Marshal.AllocHGlobal(bytes.Length);
        try
        {
            Marshal.Copy(bytes, 0, buffer, bytes.Length);
            var credential = new NativeMethods.Credential
            {
                Type = CredTypeGeneric,
                TargetName = target,
                CredentialBlobSize = (uint)bytes.Length,
                CredentialBlob = buffer,
                Persist = CredPersistLocalMachine,
                UserName = userName
            };
            if (!NativeMethods.CredWrite(ref credential, 0))
            {
                throw new InvalidOperationException($"Windows could not save your {what}.");
            }
        }
        finally
        {
            Array.Clear(bytes);
            Marshal.Copy(bytes, 0, buffer, bytes.Length);
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static void Delete(string target, string what)
    {
        if (!NativeMethods.CredDelete(target, CredTypeGeneric, 0) && Marshal.GetLastWin32Error() != ErrorNotFound)
        {
            throw new InvalidOperationException($"Windows could not remove your saved {what}.");
        }
    }

    private static string Target(string clientId) => TargetPrefix + SpotifyAuth.ValidateClientId(clientId);

    // CREDENTIALW contains strings, so these use runtime marshalling rather than LibraryImport.
    private static class NativeMethods
    {
        [DllImport("advapi32.dll", EntryPoint = "CredWriteW", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool CredWrite(ref Credential credential, uint flags);

        [DllImport("advapi32.dll", EntryPoint = "CredReadW", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool CredRead(string target, uint type, uint flags, out IntPtr credential);

        [DllImport("advapi32.dll", EntryPoint = "CredDeleteW", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool CredDelete(string target, uint type, uint flags);

        [DllImport("advapi32.dll")]
        public static extern void CredFree(IntPtr credential);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct Credential
        {
            public uint Flags;
            public uint Type;
            public string TargetName;
            public string? Comment;
            public FILETIME LastWritten;
            public uint CredentialBlobSize;
            public IntPtr CredentialBlob;
            public uint Persist;
            public uint AttributeCount;
            public IntPtr Attributes;
            public string? TargetAlias;
            public string? UserName;
        }
    }
}
