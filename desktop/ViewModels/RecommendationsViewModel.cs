using System.Collections.ObjectModel;
using System.Globalization;
using Cadenza.Core.Interfaces;
using Cadenza.Core.Models;
using Cadenza.Core.Models.Recommendations;
using Cadenza.Desktop.Models;

namespace Cadenza.Desktop.ViewModels;

/// <summary>State of the "For you" page: the shelves, status line, empty hint and reconnect banner.</summary>
public sealed class RecommendationsViewModel : ObservableObject
{
    private const string ConnectFirst = "Connect Spotify in Settings to get suggestions.";
    private const string EmptyHint = "Listen to a few tracks and save some favourites, and suggestions will appear here.";
    private const string Finding = "Finding music for you…";
    private const string PrivacyNote = "Based on what you save and play. Nothing about you leaves this device except artist names.";

    private static readonly (RecommendationShelf Shelf, string Title)[] _shelves =
    [
        (RecommendationShelf.MoreLikeYourFavourites, "More like your favourites"),
        (RecommendationShelf.DeeperCuts, "Deeper cuts from artists you love"),
        (RecommendationShelf.SomethingNew, "Something new"),
    ];

    private readonly Func<IRecommendationService> _service;
    private List<RecommendationCard> _cards = [];
    private bool _loaded;
    private string _status = "";
    private string _emptyText = EmptyHint;
    private bool _isEmpty = true;
    private bool _missingScopes;

    /// <param name="service">Creates the recommendation service on first use.</param>
    public RecommendationsViewModel(Func<IRecommendationService> service)
    {
        _service = service;
    }

    public ObservableCollection<RecommendationShelfView> Shelves { get; } = [];

    public string Status
    {
        get => _status;
        private set => SetProperty(ref _status, value);
    }

    public string EmptyText
    {
        get => _emptyText;
        private set => SetProperty(ref _emptyText, value);
    }

    public bool IsEmpty
    {
        get => _isEmpty;
        private set => SetProperty(ref _isEmpty, value);
    }

    /// <summary>The newer Spotify scopes are missing, so the page suggests reconnecting.</summary>
    public bool MissingScopes
    {
        get => _missingScopes;
        set => SetProperty(ref _missingScopes, value);
    }

    /// <summary>
    /// Prepares the page when it's opened. Returns true when it still needs a refresh (nothing loaded and
    /// nothing saved); the saved suggestions are shown instantly otherwise.
    /// </summary>
    public bool Open(bool connected)
    {
        if (!connected)
        {
            EmptyText = ConnectFirst;
            _cards.Clear();
            RebuildShelves();

            return false;
        }

        if (_loaded)
            return false;

        if (_service().LoadSaved() is not { } saved)
            return true;

        Show(saved.Result.Items);
        Status = string.Format(CultureInfo.CurrentCulture, "Suggested {0:t}. Refresh for new ones.", saved.MadeAt.ToLocalTime());

        return false;
    }

    /// <summary>Builds fresh suggestions.</summary>
    /// <param name="discovery">0 = familiar artists, 1 = mostly new ones.</param>
    public async Task RefreshAsync(double discovery, CancellationToken cancellationToken)
    {
        Status = Finding;
        var (result, missingScopes) = await _service().RefreshAsync(discovery, cancellationToken);
        Show(result.Items);
        MissingScopes = missingScopes;
        Status = result.Warnings.Count > 0 ? result.Warnings[0] + " Showing what we could find." : PrivacyNote;
    }

    /// <summary>Removes a suggestion and records "not interested" for its artist.</summary>
    public void NotInterested(RecommendationCard card)
    {
        _service().RecordListening(ListeningEventKind.NotInterested, card.Track);
        _cards.Remove(card);
        RebuildShelves();
    }

    /// <summary>Records a listening signal for the recommendation history.</summary>
    public void RecordListening(ListeningEventKind kind, Track track) => _service().RecordListening(kind, track);

    /// <summary>Clears everything (on disconnect).</summary>
    public void Forget()
    {
        _service().Forget();
        _cards.Clear();
        _loaded = false;
        RebuildShelves();
    }

    private void Show(IEnumerable<Recommendation> items)
    {
        _cards = items.Select(item => new RecommendationCard(item)).ToList();
        _loaded = true;
        EmptyText = EmptyHint;
        RebuildShelves();
    }

    private void RebuildShelves()
    {
        Shelves.Clear();
        foreach (var (shelf, title) in _shelves)
        {
            var cards = _cards.Where(card => card.Shelf == shelf).ToArray();
            if (cards.Length > 0)
                Shelves.Add(new RecommendationShelfView(title, cards));
        }

        IsEmpty = _cards.Count == 0;
    }
}
