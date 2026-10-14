namespace Cadenza.Core.Interfaces;

/// <summary>Keeps the Cadenza Friends device token in the OS credential store (never in files).</summary>
public interface IFriendsTokenStore
{
    /// <summary>The saved token, or null when you haven't joined on this device.</summary>
    string? Load();

    void Save(string token);

    void Delete();
}
