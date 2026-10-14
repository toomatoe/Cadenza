using Cadenza.Core.Models.Social;
using Cadenza.Server.Extensions;
using Cadenza.Server.Interfaces;
using Cadenza.Server.Models;

namespace Cadenza.Server.Endpoints;

/// <summary>Friend lists, requests, unfriending and blocking. Changes are pushed live to the other person.</summary>
public static class FriendEndpoints
{
    /// <summary>Maps <c>/friends</c> and <c>/blocks</c> on the authenticated API group.</summary>
    public static RouteGroupBuilder MapFriendEndpoints(this RouteGroupBuilder api)
    {
        api.MapGet("/friends", (HttpContext context, ISocialStore store) => store.GetFriends(context.GetUser().Id));
        api.MapPost("/friends/requests", RequestFriendAsync).RequireRateLimiting(RateLimitPolicies.Slow);
        api.MapPost("/friends/{id}/accept", AcceptAsync);
        api.MapDelete("/friends/{id}", RemoveAsync);

        // Blocking is silent: the other person sees an ordinary unfriend at most.
        api.MapPost("/blocks/{id}", BlockAsync);
        api.MapDelete("/blocks/{id}", Unblock);

        return api;
    }

    private static async Task<FriendList> RequestFriendAsync(HttpContext context, FriendRequest request, ISocialStore store, IConnectionHub hub, TimeProvider clock)
    {
        var me = context.GetUser();
        var (other, accepted) = store.RequestFriend(me.Id, request.Username, clock.GetUtcNow());
        var type = accepted ? SocialEvent.FriendAcceptedType : SocialEvent.FriendRequestType;
        await hub.SendAsync(other.Id, new SocialEvent(type, user: me), context.RequestAborted);

        return store.GetFriends(me.Id);
    }

    private static async Task<FriendList> AcceptAsync(HttpContext context, string id, ISocialStore store, IConnectionHub hub)
    {
        var me = context.GetUser();
        store.Accept(me.Id, id);
        await hub.SendAsync(id, new SocialEvent(SocialEvent.FriendAcceptedType, user: me), context.RequestAborted);

        return store.GetFriends(me.Id);
    }

    private static async Task<FriendList> RemoveAsync(HttpContext context, string id, ISocialStore store, IConnectionHub hub)
    {
        var me = context.GetUser();
        store.Remove(me.Id, id);
        await hub.SendAsync(id, new SocialEvent(SocialEvent.FriendRemovedType, user: me), context.RequestAborted);

        return store.GetFriends(me.Id);
    }

    private static async Task<FriendList> BlockAsync(HttpContext context, string id, ISocialStore store, IConnectionHub hub)
    {
        var me = context.GetUser();
        store.Block(me.Id, id);
        await hub.SendAsync(id, new SocialEvent(SocialEvent.FriendRemovedType, user: me), context.RequestAborted);

        return store.GetFriends(me.Id);
    }

    private static IResult Unblock(HttpContext context, string id, ISocialStore store)
    {
        store.Unblock(context.GetUser().Id, id);

        return Results.NoContent();
    }
}
