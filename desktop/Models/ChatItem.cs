using System.ComponentModel;
using System.Globalization;
using Cadenza.Core.Models;
using Cadenza.Core.Models.Social;
using Microsoft.UI.Xaml;

namespace Cadenza.Desktop.Models;

/// <summary>A chat bubble. Shared tracks arrive as IDs; <see cref="Track"/> fills in once fetched from Spotify.</summary>
public sealed partial class ChatItem(ChatMessage message, bool mine) : INotifyPropertyChanged
{
    private Track? _track;

    public event PropertyChangedEventHandler? PropertyChanged;

    public ChatMessage Message { get; } = message;

    public bool Mine { get; } = mine;

    public HorizontalAlignment Alignment => Mine ? HorizontalAlignment.Right : HorizontalAlignment.Left;

    public string Text => Message.Kind switch
    {
        ChatMessageKind.Gift when Mine => "You sent this to their queue",
        ChatMessageKind.Gift => "A track for your queue",
        ChatMessageKind.Share when Message.Text.Length == 0 => Mine ? "You shared a track" : "Shared a track",
        _ => Message.Text,
    };

    public string Time => Message.SentAt.ToLocalTime().ToString("t", CultureInfo.CurrentCulture);

    public Visibility TrackVisibility => Message.TrackId is null ? Visibility.Collapsed : Visibility.Visible;

    public Track? Track
    {
        get => _track;
        set
        {
            _track = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Track)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(TrackTitle)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(TrackArtist)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ArtworkUrl)));
        }
    }

    public string TrackTitle => _track?.Name ?? "Loading track…";

    public string TrackArtist => _track?.Artist ?? "";

    public string ArtworkUrl => _track?.ArtworkUrl ?? "";
}
