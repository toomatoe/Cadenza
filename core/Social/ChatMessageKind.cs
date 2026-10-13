namespace Cadenza.Core.Social;

public enum ChatMessageKind
{
    /// <summary>Plain text.</summary>
    Text,

    /// <summary>A track shared as a card the friend can play or queue.</summary>
    Share,

    /// <summary>A track offered for the friend's Up next.</summary>
    Gift
}
