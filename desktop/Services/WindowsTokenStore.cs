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

    public Task<Tokens?> LoadAsync(string clientId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!NativeMethods.CredRead(Target(clientId), CredTypeGeneric, 0, out var pointer))
        {
            return Marshal.GetLastWin32Error() == ErrorNotFound
                ? Task.FromResult<Tokens?>(null)
                : throw new InvalidOperationException("Windows could not read your saved Spotify sign-in.");
        }

        byte[]? bytes = null;
        try
        {
            var credential = Marshal.PtrToStructure<NativeMethods.Credential>(pointer);
            bytes = new byte[checked((int)credential.CredentialBlobSize)];
            Marshal.Copy(credential.CredentialBlob, bytes, 0, bytes.Length);
            return Task.FromResult(JsonSerializer.Deserialize<Tokens>(bytes));
        }
        finally
        {
            if (bytes is not null)
            {
                Array.Clear(bytes);
            }

            NativeMethods.CredFree(pointer);
        }
    }

    public Task SaveAsync(Tokens tokens, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var bytes = JsonSerializer.SerializeToUtf8Bytes(tokens);
        if (bytes.Length > MaxBlobBytes)
        {
            throw new InvalidOperationException("Spotify sign-in is too large for Windows credential storage.");
        }

        var buffer = Marshal.AllocHGlobal(bytes.Length);
        try
        {
            Marshal.Copy(bytes, 0, buffer, bytes.Length);
            var credential = new NativeMethods.Credential
            {
                Type = CredTypeGeneric,
                TargetName = Target(tokens.ClientId),
                CredentialBlobSize = (uint)bytes.Length,
                CredentialBlob = buffer,
                Persist = CredPersistLocalMachine,
                UserName = "Spotify OAuth"
            };
            if (!NativeMethods.CredWrite(ref credential, 0))
            {
                throw new InvalidOperationException("Windows could not save your Spotify sign-in.");
            }
        }
        finally
        {
            // Overwrite the managed and unmanaged copies of the tokens before releasing them.
            Array.Clear(bytes);
            Marshal.Copy(bytes, 0, buffer, bytes.Length);
            Marshal.FreeHGlobal(buffer);
        }

        return Task.CompletedTask;
    }

    public Task DeleteAsync(string clientId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!NativeMethods.CredDelete(Target(clientId), CredTypeGeneric, 0) && Marshal.GetLastWin32Error() != ErrorNotFound)
        {
            throw new InvalidOperationException("Windows could not remove your saved Spotify sign-in.");
        }

        return Task.CompletedTask;
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
