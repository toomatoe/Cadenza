using Cadenza.Core.Models.Social;
using Cadenza.Server.Extensions;
using Cadenza.Server.Interfaces;
using Cadenza.Server.Models;

namespace Cadenza.Server.Endpoints;

/// <summary>Joining with an invite, the signed-in profile, invites and account deletion.</summary>
public static class AccountEndpoints
{
    /// <summary>Maps <c>POST /api/accounts</c> (anonymous) on the app.</summary>
    public static IEndpointRouteBuilder MapAccountCreation(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/accounts", CreateAccount).RequireRateLimiting(RateLimitPolicies.Slow);

        return app;
    }

    /// <summary>Maps <c>/me</c> and <c>/invites</c> on the authenticated API group.</summary>
    public static RouteGroupBuilder MapAccountEndpoints(this RouteGroupBuilder api)
    {
        api.MapGet("/me", (HttpContext context) => context.GetUser());
        api.MapPut("/me", UpdateProfile);
        api.MapDelete("/me", DeleteAccount);
        api.MapPost("/invites", CreateInvite).RequireRateLimiting(RateLimitPolicies.Slow);

        return api;
    }

    private static AccountCreated CreateAccount(CreateAccountRequest request, ISocialStore store, TimeProvider clock) =>
        store.CreateAccount(request, clock.GetUtcNow());

    private static SocialUser UpdateProfile(HttpContext context, UpdateProfileRequest request, ISocialStore store) =>
        store.UpdateProfile(context.GetUser().Id, request);

    private static IResult DeleteAccount(HttpContext context, ISocialStore store)
    {
        store.DeleteAccount(context.GetUser().Id);

        return Results.NoContent();
    }

    private static InviteCreated CreateInvite(HttpContext context, ISocialStore store, TimeProvider clock) =>
        store.CreateInvite(context.GetUser().Id, clock.GetUtcNow());
}
