using Cadenza.Core.Models;
using Cadenza.Core.Models.Recommendations;
using Cadenza.Desktop.Models;
using Cadenza.Desktop.Services;
using Cadenza.Desktop.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Cadenza.Desktop.Views;

/// <summary>The "For you" page: thin glue between the controls and <see cref="RecommendationsViewModel"/>.</summary>
public sealed partial class MainWindow
{
    private const double SkipWindowMs = 30_000;

    public RecommendationsViewModel Recommendations { get; }

    private async Task ShowRecommendationsAsync(bool refresh)
    {
        var needsRefresh = Recommendations.Open(_auth.IsConnected);
        if (_auth.IsConnected && (refresh || needsRefresh))
            await RunAsync(LoadRecommendationsAsync);
    }

    private async Task LoadRecommendationsAsync(CancellationToken cancellationToken)
    {
        var discovery = DiscoverySlider.Value / 100;
        if (Math.Abs(_preferences.Discovery - discovery) > 0.001)
        {
            _preferences = _preferences with { Discovery = discovery };
            PreferencesStore.Save(_preferences);
        }

        await Recommendations.RefreshAsync(discovery, cancellationToken);
    }

    private async void RefreshRecommendations_Click(object sender, RoutedEventArgs e) => await ShowRecommendationsAsync(refresh: true);

    private void NotInterested_Click(object sender, RoutedEventArgs e)
    {
        if (((Button)sender).Tag is not RecommendationCard card)
            return;

        Recommendations.NotInterested(card);
        Message($"Got it. Fewer suggestions like {card.Track.LeadArtist} from now on.");
    }

    private async void PlayRecommendation_Click(object sender, RoutedEventArgs e)
    {
        if (((Button)sender).Tag is RecommendationCard card)
            await PlaybackWorkAsync(ct => PlayTrackAsync(card.Track, ct));
    }

    private void AddRecommendation_Click(object sender, RoutedEventArgs e)
    {
        if (((Button)sender).Tag is RecommendationCard card)
            AddToQueue(card.Track);
    }

    /// <summary>Records a listening signal for the playing track.</summary>
    private void RecordListening(ListeningEventKind kind, Track? track)
    {
        if (track is not null)
            Recommendations.RecordListening(kind, track);
    }

    /// <summary>"Next" within the first 30 seconds counts as a skip.</summary>
    private void RecordSkipIfEarly()
    {
        if (_playingTrack is not null && _playbackState == PlaybackState.Playing && PositionSlider.Value < SkipWindowMs)
            RecordListening(ListeningEventKind.Skipped, _playingTrack);
    }

    private void ForgetRecommendations() => Recommendations.Forget();
}
