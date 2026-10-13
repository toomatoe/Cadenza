using System.Threading.RateLimiting;
using Cadenza.Core.Social;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Http.Json;

namespace Cadenza.Server;

/// <summary>
/// Builds the Cadenza Friends server: a small JSON API plus one WebSocket per device for live events.
/// Kestrel listens on localhost only; Cloudflare Tunnel (or a reverse proxy) provides HTTPS.
/// </summary>
public static class ServerApp
{
    public const string SendPolicy = "send";
    public const string SlowPolicy = "slow";
    public const int MessagesPerMinute = 30;
    public const int SlowActionsPerHour = 20;

    private const string UserItem = "cadenza-user";

    // Cloudflare closes idle connections after 100 seconds; keep-alives stay well inside that.
    private static readonly TimeSpan s_keepAlive = TimeSpan.FromSeconds(30);

    public static WebApplication Build(string[] args, Action<WebApplicationBuilder>? configure = null)
    {
        var builder = WebApplication.CreateSlimBuilder(args);
        configure?.Invoke(builder);
        builder.Services.Configure<JsonOptions>(options =>
        {
            options.SerializerOptions.PropertyNamingPolicy = SocialRules.Json.PropertyNamingPolicy;
            foreach (var converter in SocialRules.Json.Converters)
            {
                options.SerializerOptions.Converters.Add(converter);
            }
        });
        builder.Services.AddSingleton(_ => new SocialStore(builder.Configuration["Cadenza:Database"] ?? "cadenza.db"));
        builder.Services.AddSingleton<ConnectionHub>();
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.Configure<ForwardedHeadersOptions>(options =>
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto);
        builder.Services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.OnRejected = (context, ct) => new ValueTask(
                context.HttpContext.Response.WriteAsJsonAsync(new SocialError("Slow down a little and try again soon."), SocialRules.Json, ct));
            options.AddPolicy(SendPolicy, context => RateLimitPartition.GetFixedWindowLimiter(
                PartitionKey(context), _ => new FixedWindowRateLimiterOptions { PermitLimit = MessagesPerMinute, Window = TimeSpan.FromMinutes(1) }));
            options.AddPolicy(SlowPolicy, context => RateLimitPartition.GetFixedWindowLimiter(
                PartitionKey(context), _ => new FixedWindowRateLimiterOptions { PermitLimit = SlowActionsPerHour, Window = TimeSpan.FromHours(1) }));
        });

        var app = builder.Build();
        app.UseForwardedHeaders();
        app.Use(ConvertRuleErrorsAsync);
        app.UseRateLimiter();
        app.UseWebSockets(new WebSocketOptions { KeepAliveInterval = s_keepAlive });
        MapEndpoints(app);

        var bootstrap = app.Services.GetRequiredService<SocialStore>()
            .EnsureBootstrapInvite(app.Configuration["Cadenza:BootstrapInvite"], app.Services.GetRequiredService<TimeProvider>().GetUtcNow());
        if (bootstrap is not null)
        {
#pragma warning disable CA1848 // Logged once at startup; a source-generated logger would be overkill.
            app.Logger.LogWarning("No accounts yet. Join from Cadenza with invite code {Code}", bootstrap);
#pragma warning restore CA1848
        }

        return app;
    }

    private static void MapEndpoints(WebApplication app)
    {
        app.MapGet("/health", () => "ok");

        app.MapPost("/api/accounts", (CreateAccountRequest request, SocialStore store, TimeProvider clock) =>
            store.CreateAccount(request, clock.GetUtcNow())).RequireRateLimiting(SlowPolicy);

        var api = app.MapGroup("/api").AddEndpointFilter(AuthenticateAsync);

        api.MapGet("/me", (HttpContext context) => Me(context));
        api.MapPut("/me", (HttpContext context, UpdateProfileRequest request, SocialStore store) => store.UpdateProfile(Me(context).Id, request));
        api.MapDelete("/me", (HttpContext context, SocialStore store) =>
        {
            store.DeleteAccount(Me(context).Id);
            return Results.NoContent();
        });

        api.MapPost("/invites", (HttpContext context, SocialStore store, TimeProvider clock) =>
            store.CreateInvite(Me(context).Id, clock.GetUtcNow())).RequireRateLimiting(SlowPolicy);

        api.MapGet("/friends", (HttpContext context, SocialStore store) => store.GetFriends(Me(context).Id));
        api.MapPost("/friends/requests", async (HttpContext context, FriendRequest request, SocialStore store, ConnectionHub hub, TimeProvider clock) =>
        {
            var me = Me(context);
            var (other, accepted) = store.RequestFriend(me.Id, request.Username, clock.GetUtcNow());
            await hub.SendAsync(other.Id, new SocialEvent(accepted ? SocialEvent.FriendAcceptedType : SocialEvent.FriendRequestType, User: me), context.RequestAborted);
            return store.GetFriends(me.Id);
        }).RequireRateLimiting(SlowPolicy);
        api.MapPost("/friends/{id}/accept", async (HttpContext context, string id, SocialStore store, ConnectionHub hub) =>
        {
            var me = Me(context);
            store.Accept(me.Id, id);
            await hub.SendAsync(id, new SocialEvent(SocialEvent.FriendAcceptedType, User: me), context.RequestAborted);
            return store.GetFriends(me.Id);
        });
        api.MapDelete("/friends/{id}", async (HttpContext context, string id, SocialStore store, ConnectionHub hub) =>
        {
            var me = Me(context);
            store.Remove(me.Id, id);
            await hub.SendAsync(id, new SocialEvent(SocialEvent.FriendRemovedType, User: me), context.RequestAborted);
            return store.GetFriends(me.Id);
        });

        // Blocking is silent: the other person sees an ordinary unfriend at most.
        api.MapPost("/blocks/{id}", async (HttpContext context, string id, SocialStore store, ConnectionHub hub) =>
        {
            var me = Me(context);
            store.Block(me.Id, id);
            await hub.SendAsync(id, new SocialEvent(SocialEvent.FriendRemovedType, User: me), context.RequestAborted);
            return store.GetFriends(me.Id);
        });
        api.MapDelete("/blocks/{id}", (HttpContext context, string id, SocialStore store) =>
        {
            store.Unblock(Me(context).Id, id);
            return Results.NoContent();
        });

        api.MapGet("/messages/{friendId}", (HttpContext context, string friendId, long? before, SocialStore store) =>
        {
            var me = Me(context);
            return store.AreFriends(me.Id, friendId)
                ? store.Messages(me.Id, friendId, before, SocialRules.PageSize)
                : throw new SocialRuleException(StatusCodes.Status403Forbidden, "You can only read conversations with friends.");
        });
        api.MapPost("/messages/{friendId}", async (HttpContext context, string friendId, SendMessageRequest request, SocialStore store, ConnectionHub hub, TimeProvider clock) =>
        {
            var me = Me(context);
            var message = store.SendMessage(me.Id, friendId, request, clock.GetUtcNow());
            var pushed = new SocialEvent(SocialEvent.MessageType, message);
            await hub.SendAsync(friendId, pushed, context.RequestAborted);
            await hub.SendAsync(me.Id, pushed, context.RequestAborted);
            return message;
        }).RequireRateLimiting(SendPolicy);

        api.MapPost("/reports", (HttpContext context, ReportRequest request, SocialStore store, TimeProvider clock) =>
        {
            store.Report(Me(context).Id, request, clock.GetUtcNow());
            return Results.NoContent();
        }).RequireRateLimiting(SlowPolicy);

        api.MapGet("/live", async (HttpContext context, ConnectionHub hub) =>
        {
            if (!context.WebSockets.IsWebSocketRequest)
            {
                return Results.BadRequest(new SocialError("Expected a WebSocket."));
            }

            using var socket = await context.WebSockets.AcceptWebSocketAsync();
            await hub.ServeAsync(Me(context).Id, socket, context.RequestAborted);
            return Results.Empty;
        });
    }

    private static SocialUser Me(HttpContext context) => (SocialUser)context.Items[UserItem]!;

    private static async ValueTask<object?> AuthenticateAsync(EndpointFilterInvocationContext invocation, EndpointFilterDelegate next)
    {
        var context = invocation.HttpContext;
        var header = context.Request.Headers.Authorization.ToString();
        var token = header.StartsWith("Bearer ", StringComparison.Ordinal) ? header["Bearer ".Length..].Trim() : "";
        var user = token.Length > 0 ? context.RequestServices.GetRequiredService<SocialStore>().Authenticate(token) : null;
        if (user is null)
        {
            return Results.Json(new SocialError("Sign in to Cadenza Friends again."), SocialRules.Json, statusCode: StatusCodes.Status401Unauthorized);
        }

        context.Items[UserItem] = user;
        return await next(invocation);
    }

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
        catch (BadHttpRequestException) when (!context.Response.HasStarted)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            await context.Response.WriteAsJsonAsync(new SocialError("That request wasn't understood."), SocialRules.Json, context.RequestAborted);
        }
    }

    // Per device token when signed in, otherwise per client address.
    private static string PartitionKey(HttpContext context) =>
        context.Request.Headers.Authorization.ToString() is { Length: > 0 } auth
            ? TokenHasher.Hash(auth)
            : context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
}
