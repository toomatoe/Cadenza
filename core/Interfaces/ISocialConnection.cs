using Cadenza.Core.Models.Social;

namespace Cadenza.Core.Interfaces;

/// <summary>The live Friends event socket. Reconnects on its own; events arrive on the creating thread's context.</summary>
public interface ISocialConnection : IAsyncDisposable
{
    event EventHandler<SocialEvent>? EventReceived;

    /// <summary>Raised with true when connected and false when the connection drops.</summary>
    event EventHandler<bool>? ConnectionChanged;

    void Start();
}
