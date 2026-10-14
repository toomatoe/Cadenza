namespace Cadenza.Server.Models;

/// <summary>The "Cadenza" configuration section (appsettings.json or Cadenza__* environment variables).</summary>
public sealed class CadenzaServerOptions
{
    /// <summary>The configuration section name.</summary>
    public const string SectionName = "Cadenza";

    /// <summary>The SQLite database file.</summary>
    public string Database { get; set; } = "cadenza.db";

    /// <summary>The folder uploaded songs are stored in.</summary>
    public string MusicFolder { get; set; } = "music";

    /// <summary>The invite code for the first account; generated when empty.</summary>
    public string BootstrapInvite { get; set; } = "";
}
