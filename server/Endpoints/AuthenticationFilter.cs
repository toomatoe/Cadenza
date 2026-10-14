using Cadenza.Core.Models.Social;
using Cadenza.Core.Services.Social;
using Cadenza.Server.Extensions;
using Cadenza.Server.Interfaces;

namespace Cadenza.Server.Endpoints;

/// <summary>Requires a valid device token ("Authorization: Bearer …") and stores the user on the request.</summary>
public sealed class AuthenticationFilter : IEndpointFilter
{
    private const string BearerPrefix = "Bearer ";
    private readonly ISocialStore _store;

    public AuthenticationFilter(ISocialStore store)
    {
        _store = store;
    }

    /// <inheritdoc/>
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var http = context.HttpContext;
        var header = http.Request.Headers.Authorization.ToString();
        var token = header.StartsWith(BearerPrefix, StringComparison.Ordinal) ? header[BearerPrefix.Length..].Trim() : "";
        if (token.Length == 0 || _store.Authenticate(token) is not { } user)
            return Results.Json(new SocialError("Sign in to Cadenza Friends again."), SocialRules.Json, statusCode: StatusCodes.Status401Unauthorized);

        http.SetUser(user);

        return await next(context);
    }
}
