namespace Cadenza.Core.Services.Playback;

/// <summary>
/// Maps native playback failure codes to user-facing messages. Codes 6–8 are engine statuses and
/// 9–14 are diagnostic categories from native/diagnostics.rs.
/// </summary>
public static class PlaybackErrors
{
    /// <summary>Whether the playback session must be re-created before trying again.</summary>
    public static bool RequiresReconnect(int code) => code is 0 or 6 or 7 or 8 or 12 or 14;

    public static string Describe(int code) => code switch
    {
        6 => "The Spotify playback connection closed. Play the track again to reconnect.",
        7 => "Spotify rejected playback sign-in. Reconnect your Premium account in Settings.",
        8 => "Windows audio output failed. Check your playback device.",
        9 => "Spotify couldn't load this track. Try another track; if every track fails, check playback in the Spotify app.",
        10 => "Spotify refused access to this track's audio key. This native player cannot stream it. Try the Spotify app.",
        11 => "Spotify denied access to the track data. Check your developer app's access and allowed users.",
        12 => "Couldn't reach Spotify's playback servers. Check your connection and try again.",
        13 => "The track's audio could not be decoded. Try another track or use the Spotify app.",
        14 => "Spotify rejected the playback client. Reconnect in Settings; if it persists, try the Spotify app.",
        _ => "Playback failed. Try another track or reconnect Spotify in Settings."
    };
}
