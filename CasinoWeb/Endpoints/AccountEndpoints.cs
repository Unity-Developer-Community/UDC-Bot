using System.Security.Claims;
using AspNet.Security.OAuth.Discord;
using CasinoWeb.Auth;
using CasinoWeb.Configuration;
using CasinoWeb.Hubs;
using DiscordBot.Services.Fun.Casino;
using DiscordBot.Settings;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Extensions.Options;

namespace CasinoWeb.Endpoints;

public sealed record MeResponse(string Id, string Name, string AvatarUrl, long? Balance, bool CasinoEnabled, DailyInfo? Daily);

public static class AccountEndpoints
{
    public static void MapAccountEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/login", (string? returnUrl) => Results.Challenge(
            new AuthenticationProperties { RedirectUri = LocalPathOrRoot(returnUrl) },
            [DiscordAuthenticationDefaults.AuthenticationScheme]));

        app.MapGet("/access-denied", (IWebHostEnvironment env) => Page(env, StatusCodes.Status403Forbidden, "Access denied",
            "Only members of the Unity Developer Community Discord server can play."));
        app.MapGet("/login-failed", (IWebHostEnvironment env) => Page(env, StatusCodes.Status401Unauthorized, "Login failed",
            "Discord login did not complete. Try again."));

        var api = app.MapGroup("/api");

        api.MapGet("/me", async (ClaimsPrincipal user, CasinoService casino, BotSettings settings) =>
        {
            var id = user.FindFirstValue(ClaimTypes.NameIdentifier)!;
            var enabled = settings.Casino.Enabled;
            long? balance = enabled ? (await casino.GetOrCreateCasinoUser(id)).Tokens : null;
            var daily = enabled ? DailyInfo.Create(settings.Casino.DailyRewardTokens, await casino.GetNextDailyRewardTime(id)) : null;
            return Results.Ok(new MeResponse(
                id,
                user.FindFirstValue(AuthenticationSetup.DisplayNameClaim) ?? user.FindFirstValue(ClaimTypes.Name) ?? id,
                DiscordAvatar.Url(id, user.FindFirstValue(DiscordAuthenticationConstants.Claims.AvatarHash)),
                balance,
                enabled,
                daily));
        }).RequireAuthorization();

        // Without a session there is nothing to end, so it succeeds either way. SameSite=Lax already keeps other sites from
        // triggering it; the Origin check is a second guard.
        api.MapPost("/logout", async (HttpContext context, IOptions<CasinoHostOptions> host) =>
        {
            var origin = context.Request.Headers.Origin.ToString();
            if (origin.Length > 0 && !OriginPolicy.IsOwnOrigin(origin, host.Value.PublicOriginUri))
                return Results.StatusCode(StatusCodes.Status403Forbidden);

            await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return Results.NoContent();
        });
    }

    /// <summary>
    /// Only a path on this site, so the login cannot be used to bounce players to another site. Control characters are
    /// refused because browsers drop tabs and line breaks from URLs, which turns "/&lt;tab&gt;/evil.example" into "//evil.example".
    /// </summary>
    private static string LocalPathOrRoot(string? returnUrl) =>
        returnUrl is { Length: > 0 } && returnUrl[0] == '/'
        && (returnUrl.Length == 1 || (returnUrl[1] != '/' && returnUrl[1] != '\\'))
        && !returnUrl.Any(char.IsControl)
            ? returnUrl
            : "/";

    /// <summary>
    /// The client's page (it draws the message itself) with the status the failure deserves. Without a built client,
    /// such as in tests or before the first <c>npm run build</c>, a plain page carries the message.
    /// </summary>
    private static async Task<IResult> Page(IWebHostEnvironment env, int status, string title, string message)
    {
        var index = env.WebRootFileProvider.GetFileInfo("index.html");
        if (index.Exists)
        {
            await using var stream = index.CreateReadStream();
            using var reader = new StreamReader(stream);
            return Results.Content(await reader.ReadToEndAsync(), "text/html; charset=utf-8", statusCode: status);
        }

        return Results.Content(
            $"<!doctype html><html lang=\"en\"><meta charset=\"utf-8\"><title>{title}</title><h1>{title}</h1><p>{message}</p>",
            "text/html; charset=utf-8", statusCode: status);
    }
}
