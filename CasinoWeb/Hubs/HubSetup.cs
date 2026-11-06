using CasinoWeb.Auth;
using CasinoWeb.Configuration;
using CasinoWeb.Games;
using CasinoWeb.Tables;
using DiscordBot.Domain;
using DiscordBot.Services;
using DiscordBot.Services.Fun.Casino;
using DiscordBot.Services.Logging;
using DiscordBot.Settings;
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
        // A fresh shuffled game per hand; the tests replace it to deal fixed cards.
        services.AddSingleton<Func<Blackjack>>(_ => () => new Blackjack());
        services.AddSingleton<WebBlackjackService>();
        services.AddHostedService<BlackjackSweeper>();
        services.AddCasinoTables();
        services.AddSlotsAnnouncements();
        services.AddSignalR(options => options.AddFilter<CasinoHubFilter>());
        return services;
    }

    /// <summary>The poker tables; the hub replaces <see cref="ITableNotifier"/> with the one that pushes to clients.</summary>
    private static IServiceCollection AddCasinoTables(this IServiceCollection services)
    {
        services.AddSingleton<PresenceTracker>();
        services.AddSingleton<TableLedger>();
        services.AddSingleton<ITableNotifier, NullTableNotifier>();
        // A fresh game per table; the tests replace it to deal fixed cards.
        services.AddSingleton<Func<TableStakes, ITableGame>>(_ => stakes => new PokerTableGame(stakes));
        services.AddSingleton<TableService>();
        services.AddHostedService<TablesSweeper>();
        return services;
    }

    /// <summary>
    /// Big wins go to a Discord webhook with a picture drawn here. The pieces stay idle when no webhook is configured
    /// (Development only): the renderer loads its art on first use and the warm-up does nothing.
    /// </summary>
    private static IServiceCollection AddSlotsAnnouncements(this IServiceCollection services)
    {
        // The bot's settings copy only moves the art folder: the renderer reads nothing else, and the bot's own ./Assets stays the bot's.
        services.AddSingleton(sp => new SlotsRenderer(new BotSettings { AssetsRootPath = sp.GetRequiredService<IOptions<CasinoHostOptions>>().Value.AssetsPath }));
        services.AddHostedService<SlotsPictureWarmUp>();
        services.AddSingleton<SlotsWinAnnouncer>();
        // RemoveAllLoggers: the factory logs every request address at Information level, and a webhook address carries its token.
        services.AddHttpClient(SlotsWinAnnouncer.HttpClientName, client =>
            {
                client.Timeout = TimeSpan.FromSeconds(10);
                client.DefaultRequestHeaders.UserAgent.ParseAdd("UDC-Casino (https://github.com/Unity-Developer-Community/UDC-Bot)");
            })
            .RemoveAllLoggers();
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
