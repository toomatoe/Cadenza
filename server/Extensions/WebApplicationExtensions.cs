using Cadenza.Core.Models.Social;
using Cadenza.Core.Services.Social;
using Cadenza.Server.Endpoints;
using Cadenza.Server.Exceptions;

namespace Cadenza.Server.Extensions;

/// <summary>The Friends server's request pipeline and routes.</summary>
public static class WebApplicationExtensions
{
    // Cloudflare closes idle connections after 100 seconds; keep-alives stay well inside that.
    private static readonly TimeSpan _keepAlive = TimeSpan.FromSeconds(30);

    /// <summary>Adds forwarded headers, rule-error responses, rate limiting and WebSockets, in that order.</summary>
    public static WebApplication UseCadenzaServer(this WebApplication app)
    {
        app.UseForwardedHeaders();
        app.Use(ConvertRuleErrorsAsync);
        app.UseRateLimiter();
        app.UseWebSockets(new WebSocketOptions { KeepAliveInterval = _keepAlive });

        return app;
    }

    /// <summary>Maps <c>/health</c>, account creation, and the authenticated <c>/api</c> group.</summary>
    public static WebApplication MapCadenzaEndpoints(this WebApplication app)
    {
        app.MapGet("/health", () => "ok");
        app.MapAccountCreation();
        app.MapGroup("/api")
            .AddEndpointFilter<AuthenticationFilter>()
            .MapAccountEndpoints()
            .MapFriendEndpoints()
            .MapMessageEndpoints()
            .MapMusicEndpoints();

        return app;
    }

    /// <summary>Turns <see cref="SocialRuleException"/> and malformed requests into JSON errors safe to show the user.</summary>
    private static async Task ConvertRuleErrorsAsync(HttpContext context, RequestDelegate next)
    {
        try
        {
            await next(context);
        }
        catch (SocialRuleException error) when (!context.Response.HasStarted)
        {
            context.Response.StatusCode = error.StatusCode;
            await context.Response.WriteAsJsonAsync(new SocialError(error.Message), SocialRules.Json, context.RequestAborted);
        }
        catch (BadHttpRequestException error) when (!context.Response.HasStarted)
        {
            var logger = context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger(nameof(WebApplicationExtensions));
            logger.LogInformation(error, "Rejected a malformed request to {Path}", context.Request.Path);
            if (error.InnerException is { } inner)
                logger.LogInformation(inner, "Inner exception for the malformed request to {Path}", context.Request.Path);

            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            await context.Response.WriteAsJsonAsync(new SocialError("That request wasn't understood."), SocialRules.Json, context.RequestAborted);
        }
    }
}
