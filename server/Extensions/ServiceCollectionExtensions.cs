using System.Threading.RateLimiting;
using Cadenza.Core.Models.Social;
using Cadenza.Core.Services.Social;
using Cadenza.Server.Endpoints;
using Cadenza.Server.Interfaces;
using Cadenza.Server.Models;
using Cadenza.Server.Services;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.AspNetCore.HttpOverrides;

namespace Cadenza.Server.Extensions;

/// <summary>Registers the Cadenza Friends server's services.</summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds the Friends server's services, options, JSON settings, forwarded headers and rate limits.
    /// </summary>
    /// <example>
    /// <code>
    /// var builder = WebApplication.CreateSlimBuilder(args);
    /// builder.Services.AddCadenzaServer(builder.Configuration);
    /// </code>
    /// </example>
    public static IServiceCollection AddCadenzaServer(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<CadenzaServerOptions>(configuration.GetSection(CadenzaServerOptions.SectionName));
        services.Configure<JsonOptions>(options =>
        {
            options.SerializerOptions.PropertyNamingPolicy = SocialRules.Json.PropertyNamingPolicy;
            foreach (var converter in SocialRules.Json.Converters)
                options.SerializerOptions.Converters.Add(converter);
        });
        services.Configure<ForwardedHeadersOptions>(options =>
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto);

        // Keep in alphabetical order.
        services.AddSingleton<AuthenticationFilter>();
        services.AddSingleton<IConnectionHub, ConnectionHub>();
        services.AddSingleton<IMusicLibrary, MusicLibrary>();
        services.AddSingleton<ISocialStore, SocialStore>();
        services.AddSingleton(TimeProvider.System);

        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.OnRejected = (context, cancellationToken) => new ValueTask(
                context.HttpContext.Response.WriteAsJsonAsync(new SocialError("Slow down a little and try again soon."), SocialRules.Json, cancellationToken));
            options.AddPolicy(RateLimitPolicies.Send, context => FixedWindow(context, RateLimitPolicies.MessagesPerMinute, TimeSpan.FromMinutes(1)));
            options.AddPolicy(RateLimitPolicies.Slow, context => FixedWindow(context, RateLimitPolicies.SlowActionsPerHour, TimeSpan.FromHours(1)));
            options.AddPolicy(RateLimitPolicies.Upload, context => FixedWindow(context, RateLimitPolicies.UploadsPerHour, TimeSpan.FromHours(1)));
        });

        return services;
    }

    private static RateLimitPartition<string> FixedWindow(HttpContext context, int permits, TimeSpan window) =>
        RateLimitPartition.GetFixedWindowLimiter(PartitionKey(context), _ => new FixedWindowRateLimiterOptions { PermitLimit = permits, Window = window });

    // Per device token when signed in, otherwise per client address.
    private static string PartitionKey(HttpContext context) =>
        context.Request.Headers.Authorization.ToString() is { Length: > 0 } authorization
            ? TokenHasher.Hash(authorization)
            : context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
}
