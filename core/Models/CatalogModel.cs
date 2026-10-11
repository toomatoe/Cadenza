using System.Collections.ObjectModel;

namespace Cadenza.Core.Models;

/// <summary>Bounded catalog state shared by the controller and native view.</summary>
public sealed class CatalogModel
{
    public const int ItemLimit = 500;
    public ObservableCollection<Track> Tracks { get; } = [];
    public ObservableCollection<Playlist> Playlists { get; } = [];
    public int TrackOffset { get; internal set; }
    public int PlaylistOffset { get; internal set; }
    public bool HasMoreTracks { get; internal set; }
    public bool HasMorePlaylists { get; internal set; }
    public string LastSearchQuery { get; internal set; } = "";
}
