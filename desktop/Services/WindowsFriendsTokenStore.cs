using Cadenza.Core.Interfaces;

namespace Cadenza.Desktop.Services;

/// <summary>The Friends device token in Windows Credential Manager (through <see cref="WindowsTokenStore"/>).</summary>
public sealed class WindowsFriendsTokenStore : IFriendsTokenStore
{
    public string? Load() => WindowsTokenStore.LoadFriendsToken();

    public void Save(string token) => WindowsTokenStore.SaveFriendsToken(token);

    public void Delete() => WindowsTokenStore.DeleteFriendsToken();
}
