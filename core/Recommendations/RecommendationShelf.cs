namespace Cadenza.Core.Recommendations;

/// <summary>The row a recommendation appears in.</summary>
public enum RecommendationShelf
{
    /// <summary>Artists you don't play yet, similar to your favourites.</summary>
    MoreLikeYourFavourites,

    /// <summary>Unsaved tracks by artists you already love.</summary>
    DeeperCuts,

    /// <summary>Exploration picks: artists new to you.</summary>
    SomethingNew
}
