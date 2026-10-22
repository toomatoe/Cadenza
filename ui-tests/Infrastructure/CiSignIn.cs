using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace Cadenza.UI.Tests.Infrastructure;

/// <summary>
/// On CI runners only, gives the app a Spotify sign-in before any test starts: the client ID goes into
/// preferences and the refresh token into Windows Credential Manager, in the format the app's token
/// store reads. The app refreshes the access token itself on launch. Runs only when
/// <c>CADENZA_UI_TEST_SEED=1</c>, so it can never overwrite a developer's own settings.
/// </summary>
[TestClass]
public static class CiSignIn
{
    private const string SeedVariable = "CADENZA_UI_TEST_SEED";
    private const string ClientIdVariable = "CADENZA_SPOTIFY_CLIENT_ID";
    private const string RefreshTokenVariable = "CADENZA_SPOTIFY_REFRESH_TOKEN";
    private const string CredentialPrefix = "Cadenza/";
    private const uint GenericCredential = 1;
    private const uint PersistLocalMachine = 2;

    [AssemblyInitialize]
    public static void SeedSignIn(TestContext context)
    {
        var clientId = Environment.GetEnvironmentVariable(ClientIdVariable) ?? "";
        var refreshToken = Environment.GetEnvironmentVariable(RefreshTokenVariable) ?? "";
        if (Environment.GetEnvironmentVariable(SeedVariable) != "1" || clientId.Length == 0 || refreshToken.Length == 0)
            return;

        var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Cadenza");
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "preferences.json"), JsonSerializer.Serialize(new { ClientId = clientId }));

        // An expired, empty access token makes the app refresh with the refresh token on its first request.
        var tokens = JsonSerializer.SerializeToUtf8Bytes(new
        {
            AccessToken = "",
            RefreshToken = refreshToken,
            ExpiresAt = DateTimeOffset.UnixEpoch,
            ClientId = clientId,
        });
        WriteCredential(CredentialPrefix + clientId, tokens);
        context.WriteLine("Seeded a Spotify sign-in for the UI tests.");
    }

    private static void WriteCredential(string target, byte[] secret)
    {
        var buffer = Marshal.AllocHGlobal(secret.Length);
        try
        {
            Marshal.Copy(secret, 0, buffer, secret.Length);
            var credential = new Credential
            {
                Type = GenericCredential,
                TargetName = target,
                CredentialBlobSize = (uint)secret.Length,
                CredentialBlob = buffer,
                Persist = PersistLocalMachine,
                UserName = "Spotify OAuth",
            };
            if (!CredWrite(ref credential, 0))
                throw new InvalidOperationException($"Couldn't save the test sign-in (Win32 error {Marshal.GetLastWin32Error().ToString(System.Globalization.CultureInfo.InvariantCulture)}).");
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    // CREDENTIALW contains strings, so this uses runtime marshalling, as the app's own token store does.
    [DllImport("advapi32.dll", EntryPoint = "CredWriteW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredWrite(ref Credential credential, uint flags);

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
}
