using Cadenza.Server.Extensions;
using Cadenza.Server.Interfaces;
using Cadenza.Server.Models;
using Microsoft.Extensions.Options;

namespace Cadenza.Server;

/// <summary>
/// Builds the Cadenza Friends server: a small JSON API plus one WebSocket per device for live events.
/// Kestrel listens on localhost only; Cloudflare Tunnel (or a reverse proxy) provides HTTPS.
/// </summary>
public static partial class ServerApp
{
    [LoggerMessage(1000, LogLevel.Warning, "No accounts yet. Join from Cadenza with invite code {Code}")]
    private static partial void LogBootstrapCode(ILogger logger, string code);

    /// <summary>Builds the configured server. <paramref name="configure"/> lets tests adjust the builder.</summary>
    /// <example>
    /// <code>
    /// var app = ServerApp.Build(args);
    /// await app.RunAsync();
    /// </code>
    /// </example>
    public static WebApplication Build(string[] args, Action<WebApplicationBuilder>? configure = null)
    {
        var builder = WebApplication.CreateSlimBuilder(args);
        configure?.Invoke(builder);
        builder.Services.AddCadenzaServer(builder.Configuration);

        var app = builder.Build();
        app.UseCadenzaServer();
        app.MapCadenzaEndpoints();
        LogBootstrapInvite(app);

        return app;
    }

    /// <summary>When nobody has joined yet, logs the invite code the owner uses for the first account.</summary>
    private static void LogBootstrapInvite(WebApplication app)
    {
        var options = app.Services.GetRequiredService<IOptions<CadenzaServerOptions>>().Value;
        var now = app.Services.GetRequiredService<TimeProvider>().GetUtcNow();
        var code = app.Services.GetRequiredService<ISocialStore>().EnsureBootstrapInvite(options.BootstrapInvite, now);
        if (code is null)
            return;

        LogBootstrapCode(app.Logger, code);
    }
}
