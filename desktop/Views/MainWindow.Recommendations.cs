using System.Collections.ObjectModel;
using Cadenza.Core.Models;
using Cadenza.Core.Recommendations;
using Cadenza.Desktop.Models;
using Cadenza.Desktop.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Cadenza.Desktop.Views;

public sealed partial class MainWindow
{
    private const double SkipWindowMs = 30_000;

    // Created on first use so the feature costs nothing until the "For you" page is opened.
    private ListeningHistoryStore? _history;
    private ListenBrainzSimilarArtists? _similarArtists;
    private RecommendationEngine? _recommendationEngine;
    private bool _recommendationsLoaded;

    private List<RecommendationCard> _recommendations = [];

    public ObservableCollection<RecommendationShelfView> RecommendationShelves { get; } = [];

    private ListeningHistoryStore History => _history ??= new ListeningHistoryStore(DataPath("listening-history.json"));

    private static string DataPath(string fileName) =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Cadenza", fileName);

    private async Task ShowRecommendationsAsync(bool refresh)
    {
        if (!_auth.IsConnected)
        {
            RecommendationsEmpty.Text = "Connect Spotify in Settings to get suggestions.";
            UpdateRecommendationSurfaces();
            return;
        }

        if (_recommendationsLoaded && !refresh)
        {
            UpdateRecommendationSurfaces();
            return;
        }

        await RunAsync(LoadRecommendationsAsync);
    }

    private async Task LoadRecommendationsAsync(CancellationToken ct)
    {
        _similarArtists ??= new ListenBrainzSimilarArtists(_http, DataPath("recommendation-cache.json"));
        _recommendationEngine ??= new RecommendationEngine(_similarArtists, new SpotifyTrackResolver(_spotifyApi));
        RecommendationsStatus.Text = "Finding music for you…";
        var (inputs, missingScopes) = await new TasteInputsLoader(_spotifyApi, History).LoadAsync(ct);
        var discovery = DiscoverySlider.Value / 100;
        if (Math.Abs(_preferences.Discovery - discovery) > 0.001)
        {
            _preferences = _preferences with { Discovery = discovery };
            PreferencesStore.Save(_preferences);
        }

        var profile = TasteProfile.Build(inputs, DateTimeOffset.UtcNow);
        var result = await _recommendationEngine.RecommendAsync(profile, discovery, History.RecentlySuggested, ct);
        History.RememberSuggestions(result.Items.Select(item => item.Track.Id));

        _recommendations = result.Items.Select(item => new RecommendationCard(item)).ToList();
        _recommendationsLoaded = true;
        ReconnectForRecommendations.IsOpen = missingScopes;
        RecommendationsEmpty.Text = "Listen to a few tracks and save some favourites, and suggestions will appear here.";
        RecommendationsStatus.Text = result.Warnings.Count > 0
            ? result.Warnings[0] + " Showing what we could find."
            : "Based on what you save and play. Nothing about you leaves this device except artist names.";
        UpdateRecommendationSurfaces();
    }

    private static readonly (RecommendationShelf Shelf, string Title)[] s_shelves =
    [
        (RecommendationShelf.MoreLikeYourFavourites, "More like your favourites"),
        (RecommendationShelf.DeeperCuts, "Deeper cuts from artists you love"),
        (RecommendationShelf.SomethingNew, "Something new"),
    ];

    private void UpdateRecommendationSurfaces()
    {
        RecommendationShelves.Clear();
        foreach (var (shelf, title) in s_shelves)
        {
            var cards = _recommendations.Where(card => card.Shelf == shelf).ToArray();
            if (cards.Length > 0)
            {
                RecommendationShelves.Add(new RecommendationShelfView(title, cards));
            }
        }

        RecommendationsEmpty.Visibility = ToVisibility(_recommendations.Count == 0);
    }

    private async void RefreshRecommendations_Click(object sender, RoutedEventArgs e) => await ShowRecommendationsAsync(refresh: true);

    private void NotInterested_Click(object sender, RoutedEventArgs e)
    {
        if (((Button)sender).Tag is not RecommendationCard card)
        {
            return;
        }

        History.Record(new ListeningEvent(ListeningEventKind.NotInterested, card.Track.Id, card.Track.LeadArtist, DateTimeOffset.UtcNow));
        _recommendations.Remove(card);
        UpdateRecommendationSurfaces();
        Message($"Got it. Fewer suggestions like {card.Track.LeadArtist} from now on.");
    }

    private async void PlayRecommendation_Click(object sender, RoutedEventArgs e)
    {
        if (((Button)sender).Tag is RecommendationCard card)
        {
            await PlaybackWorkAsync(ct => PlayTrackAsync(card.Track, ct));
        }
    }

    private void AddRecommendation_Click(object sender, RoutedEventArgs e)
    {
        if (((Button)sender).Tag is RecommendationCard card)
        {
            AddToQueue(card.Track);
        }
    }

    /// <summary>Records a listening signal for the playing track; a no-op until history exists or matters.</summary>
    private void RecordListening(ListeningEventKind kind, Track? track)
    {
        if (track is not null)
        {
            History.Record(new ListeningEvent(kind, track.Id, track.LeadArtist, DateTimeOffset.UtcNow));
        }
    }

    /// <summary>"Next" within the first 30 seconds counts as a skip.</summary>
    private void RecordSkipIfEarly()
    {
        if (_playingTrack is not null && _playbackState == PlaybackState.Playing && PositionSlider.Value < SkipWindowMs)
        {
            RecordListening(ListeningEventKind.Skipped, _playingTrack);
        }
    }

    private void ForgetRecommendations()
    {
        History.Clear();
        _recommendations.Clear();
        RecommendationShelves.Clear();
        _recommendationsLoaded = false;
    }
}
