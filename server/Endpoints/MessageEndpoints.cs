using Cadenza.Core.Social;
using Cadenza.Server.Exceptions;
using Cadenza.Server.Extensions;
using Cadenza.Server.Interfaces;
using Cadenza.Server.Models;

namespace Cadenza.Server.Endpoints;

/// <summary>One-to-one messages, reports, and the live WebSocket.</summary>
public static class MessageEndpoints
{
    /// <summary>Maps <c>/messages</c>, <c>/reports</c> and <c>/live</c> on the authenticated API group.</summary>
    public static RouteGroupBuilder MapMessageEndpoints(this RouteGroupBuilder api)
    {
        api.MapGet("/messages/{friendId}", GetMessages);
        api.MapPost("/messages/{friendId}", SendAsync).RequireRateLimiting(RateLimitPolicies.Send);
        api.MapPost("/reports", Report).RequireRateLimiting(RateLimitPolicies.Slow);
        api.MapGet("/live", LiveAsync);

        return api;
    }

    private static IReadOnlyList<ChatMessage> GetMessages(HttpContext context, string friendId, long? before, ISocialStore store)
    {
        var me = context.GetUser();
        if (!store.AreFriends(me.Id, friendId))
            throw new SocialRuleException(StatusCodes.Status403Forbidden, "You can only read conversations with friends.");

        return store.Messages(me.Id, friendId, before, SocialRules.PageSize);
    }

    private static async Task<ChatMessage> SendAsync(HttpContext context, string friendId, SendMessageRequest request, ISocialStore store, IConnectionHub hub, TimeProvider clock)
    {
        var me = context.GetUser();
        var message = store.SendMessage(me.Id, friendId, request, clock.GetUtcNow());
        var pushed = new SocialEvent(SocialEvent.MessageType, message);
        await hub.SendAsync(friendId, pushed, context.RequestAborted);
        await hub.SendAsync(me.Id, pushed, context.RequestAborted);

        return message;
    }

    private static IResult Report(HttpContext context, ReportRequest request, ISocialStore store, TimeProvider clock)
    {
        store.Report(context.GetUser().Id, request, clock.GetUtcNow());

        return Results.NoContent();
    }

    private static async Task<IResult> LiveAsync(HttpContext context, IConnectionHub hub)
    {
        if (!context.WebSockets.IsWebSocketRequest)
            return Results.BadRequest(new SocialError("Expected a WebSocket."));

        using var socket = await context.WebSockets.AcceptWebSocketAsync();
        await hub.ServeAsync(context.GetUser().Id, socket, context.RequestAborted);

        return Results.Empty;
    }
}
