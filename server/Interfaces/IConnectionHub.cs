using System.Net.WebSockets;
using Cadenza.Core.Social;

namespace Cadenza.Server.Interfaces;

/// <summary>Open WebSockets by user, for pushing live events. A user may be connected from several devices.</summary>
public interface IConnectionHub
{
    /// <summary>Holds the socket open until the client closes it or the server stops.</summary>
    Task ServeAsync(string userId, WebSocket socket, CancellationToken cancellationToken);

    /// <summary>Whether the user has at least one device connected.</summary>
    bool IsOnline(string userId);

    /// <summary>Sends an event to every device the user has connected.</summary>
    Task SendAsync(string userId, SocialEvent socialEvent, CancellationToken cancellationToken);
}
