using CasinoWeb.Auth;
using CasinoWeb.Configuration;
using DiscordBot.Services;
using DiscordBot.Services.Fun.Casino;
using DiscordBot.Services.Logging;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Options;

namespace CasinoWeb.Hubs;

public static class HubSetup
{
    public const string HubPath = "/hub/casino";

    public static IServiceCollection AddCasinoHub(this IServiceCollection services)
    {
        services.AddSingleton<ActionRateLimiter>();
        // No renderer: the browser animates the reels, so a spin returns no GIF.
        services.AddSingleton(sp => new SlotsService(
            sp.GetRequiredService<CasinoService>(),
            sp.GetRequiredService<ICasinoDatabase>(),
            renderer: null,
            sp.GetRequiredService<ILoggingService>()));
        services.AddSignalR(options => options.AddFilter<CasinoHubFilter>());
        return services;
    }

    /// <summary>
    /// Refuses a hub request whose <c>Origin</c> is another site, and a WebSocket upgrade without one. Browsers always send
    /// it on a WebSocket, so only a non-browser client lacks it, and that one has no cookie to ride on.
    /// </summary>
    public static IApplicationBuilder UseHubOriginCheck(this IApplicationBuilder app) => app.UseWhen(
        context => context.Request.Path.StartsWithSegments("/hub"),
        branch => branch.Use(async (context, next) =>
        {
            var origin = context.Request.Headers.Origin.ToString();
            var publicOrigin = context.RequestServices.GetRequiredService<IOptions<CasinoHostOptions>>().Value.PublicOriginUri;
            var allowed = origin.Length > 0
                ? OriginPolicy.IsOwnOrigin(origin, publicOrigin)
                : !context.WebSockets.IsWebSocketRequest;
            if (!allowed)
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                return;
            }

            await next(context);
        }));
}
