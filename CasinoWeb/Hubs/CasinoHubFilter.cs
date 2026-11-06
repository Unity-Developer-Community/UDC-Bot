using System.Reflection;
using CasinoWeb.Auth;
using DiscordBot.Settings;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.SignalR;

namespace CasinoWeb.Hubs;

/// <summary>
/// What every hub call goes through. The cookie is checked when a connection opens, but a socket stays open for hours, so
/// the guild membership is rechecked per call (at most every 10 minutes, as for the cookie), then the casino switch and the rate limit apply.
/// </summary>
public sealed class CasinoHubFilter(ActionRateLimiter limiter, BotSettings settings) : IHubFilter
{
    private const string SessionKey = "casino.session";

    public async Task OnConnectedAsync(HubLifetimeContext context, Func<HubLifetimeContext, Task> next)
    {
        // The properties are the ones the cookie validation just refreshed; keeping this very object is what lets a call
        // later see how old the last membership check is.
        var result = await context.Context.GetHttpContext()!.AuthenticateAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        if (result.Properties is null)
        {
            context.Context.Abort();
            return;
        }

        context.Context.Items[SessionKey] = result.Properties;
        await next(context);
    }

    public async ValueTask<object?> InvokeMethodAsync(HubInvocationContext invocation, Func<HubInvocationContext, ValueTask<object?>> next)
    {
        var connection = invocation.Context;
        var isGameAction = invocation.HubMethod.GetCustomAttribute<GameActionAttribute>() is not null;

        if (isGameAction && !limiter.TryAcquire(connection.UserIdentifier ?? string.Empty))
            throw new HubException(HubErrors.SlowDown);

        if (connection.Items[SessionKey] is not AuthenticationProperties properties)
            throw new HubException(HubErrors.SessionExpired);

        var revalidator = invocation.ServiceProvider.GetRequiredService<SessionRevalidator>();
        if (await revalidator.CheckAsync(properties, connection.ConnectionAborted) == SessionCheck.Revoked)
        {
            connection.Abort();
            throw new HubException(HubErrors.SessionExpired);
        }

        if (isGameAction && !settings.Casino.Enabled)
            throw new HubException(HubErrors.CasinoClosed);

        return await next(invocation);
    }
}
