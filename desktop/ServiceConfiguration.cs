using Cadenza.Core.Interfaces;
using Cadenza.Core.Models;
using Cadenza.Core.Services.Catalog;
using Cadenza.Core.Services.Recommendations;
using Cadenza.Core.Services.Social;
using Cadenza.Core.Services.Spotify;
using Cadenza.Desktop.Interfaces;
using Cadenza.Desktop.Services;

namespace Cadenza.Desktop;

/// <summary>
/// The app's composition root. Builds the object graph by hand (no DI container, to keep the app light).
/// Core services are created up front; feature services are created the first time they're used.
/// </summary>
public sealed class ServiceConfiguration : IDisposable
{
    private const string PreferencesFile = "preferences.json";
    private static readonly TimeSpan _httpTimeout = TimeSpan.FromSeconds(30);
    private readonly Lazy<RecommendationService> _recommendations;
    private readonly Lazy<SharedLibraryService> _sharedLibrary;

    public ServiceConfiguration()
    {
        DataFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Cadenza");
        Preferences = new PreferencesStore(DataPath(PreferencesFile));
        Http = new HttpClient { Timeout = _httpTimeout };
        Auth = new SpotifyAuth(Http, new WindowsTokenStore());
        PlaybackAuth = new SpotifyAuth(Http, new WindowsTokenStore(), SpotifyAuth.PlaybackScope);
        SpotifyApi = new SpotifyApi(Http, Auth);
        Catalog = new CatalogModel();
        CatalogController = new CatalogController(SpotifyApi, Catalog);
        Queue = new ListeningQueue();
        _recommendations = new Lazy<RecommendationService>(() => new RecommendationService(Http, SpotifyApi, DataFolder, TimeProvider.System));
        _sharedLibrary = new Lazy<SharedLibraryService>(() => new SharedLibraryService(DataPath("library-cache")));
    }

    /// <summary><c>%LOCALAPPDATA%\Cadenza</c>: preferences, caches and local history.</summary>
    public string DataFolder { get; }

    /// <summary>The user's settings file.</summary>
    public IPreferencesStore Preferences { get; }

    /// <summary>The one HTTP client every service shares.</summary>
    public HttpClient Http { get; }

    /// <summary>Spotify sign-in for the library and Web API (the user's own client ID).</summary>
    public ISpotifyAuth Auth { get; }

    /// <summary>Spotify sign-in for playback (librespot's desktop client ID).</summary>
    public ISpotifyAuth PlaybackAuth { get; }

    public ISpotifyApi SpotifyApi { get; }

    public CatalogModel Catalog { get; }

    public CatalogController CatalogController { get; }

    public ListeningQueue Queue { get; }

    /// <summary>The "For you" service, created on first use.</summary>
    public IRecommendationService Recommendations => _recommendations.Value;

    /// <summary>The shared Cadenza library (uploads, search, playable files), created on first use.</summary>
    public ISharedLibraryService SharedLibrary => _sharedLibrary.Value;

    /// <summary>A file path inside <see cref="DataFolder"/>.</summary>
    public string DataPath(string fileName) => Path.Combine(DataFolder, fileName);

    public void Dispose()
    {
        if (_recommendations.IsValueCreated)
            _recommendations.Value.Dispose();
        SpotifyApi.Dispose();
        Auth.Dispose();
        PlaybackAuth.Dispose();
        Http.Dispose();
    }
}
