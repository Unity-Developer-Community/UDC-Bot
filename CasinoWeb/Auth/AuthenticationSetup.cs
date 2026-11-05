using System.Security.Claims;
using System.Text.Json;
using AspNet.Security.OAuth.Discord;
using CasinoWeb.Configuration;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.DataProtection.Repositories;
using Microsoft.Extensions.Options;

namespace CasinoWeb.Auth;

public static class AuthenticationSetup
{
    public const string CookieName = "udc_casino";
    public const string DisplayNameClaim = "urn:casino:display_name";

    public static IServiceCollection AddCasinoAuthentication(this IServiceCollection services)
    {
        services.AddSingleton(TimeProvider.System);
        services.AddTransient<SessionRevalidator>();
        services.AddHttpClient<IGuildMembershipChecker, DiscordGuildMembershipChecker>(client =>
        {
            client.BaseAddress = DiscordGuildMembershipChecker.ApiBaseAddress;
            client.Timeout = TimeSpan.FromSeconds(5);
            // Discord's API asks every client to identify itself and may refuse requests that do not.
            client.DefaultRequestHeaders.UserAgent.ParseAdd("UDC-Casino (https://github.com/Unity-Developer-Community/UDC-Bot)");
        });

        // Keys persist outside the container so a deploy does not log everyone out.
        services.AddDataProtection().SetApplicationName("udc-casino");
        services.AddOptions<KeyManagementOptions>().Configure<IOptions<CasinoHostOptions>, ILoggerFactory>((keys, host, loggers) =>
        {
            if (!string.IsNullOrWhiteSpace(host.Value.KeysPath))
                keys.XmlRepository = new FileSystemXmlRepository(new DirectoryInfo(host.Value.KeysPath), loggers);
        });

        services.AddAuthentication(options =>
            {
                options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
                options.DefaultChallengeScheme = CookieAuthenticationDefaults.AuthenticationScheme;
            })
            .AddCookie(ConfigureCookie)
            .AddDiscord(ConfigureDiscord);

        // The client id and secret come from configuration, which is read when the options are first used.
        services.AddOptions<DiscordAuthenticationOptions>(DiscordAuthenticationDefaults.AuthenticationScheme)
            .Configure<IOptions<DiscordOAuthOptions>>((discord, credentials) =>
            {
                discord.ClientId = credentials.Value.ClientId;
                discord.ClientSecret = credentials.Value.ClientSecret;
            });

        return services;
    }

    private static void ConfigureCookie(CookieAuthenticationOptions cookie)
    {
        cookie.Cookie.Name = CookieName;
        cookie.Cookie.HttpOnly = true;
        cookie.Cookie.SecurePolicy = CookieSecurePolicy.Always;
        cookie.Cookie.SameSite = SameSiteMode.Lax;
        // Discord access tokens last 7 days. Revalidation re-issues the cookie, so its expiry keeps moving; what ends a
        // login after 7 days is Discord refusing the expired token, which SessionRevalidator treats as "not a member".
        cookie.ExpireTimeSpan = TimeSpan.FromDays(7);
        cookie.SlidingExpiration = false;

        // API and hub clients get a status code, never a redirect to a login page.
        cookie.Events.OnRedirectToLogin = context =>
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return Task.CompletedTask;
        };
        cookie.Events.OnRedirectToAccessDenied = context =>
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return Task.CompletedTask;
        };
        cookie.Events.OnValidatePrincipal = async context =>
        {
            var revalidator = context.HttpContext.RequestServices.GetRequiredService<SessionRevalidator>();
            switch (await revalidator.CheckAsync(context.Properties, context.HttpContext.RequestAborted))
            {
                case SessionCheck.Revoked:
                    context.RejectPrincipal();
                    await context.HttpContext.SignOutAsync(context.Scheme.Name);
                    break;
                case SessionCheck.Renewed:
                    context.ShouldRenew = true;
                    break;
            }
        };
    }

    private static void ConfigureDiscord(DiscordAuthenticationOptions discord)
    {
        discord.Scope.Add("guilds.members.read");
        discord.SaveTokens = false; // only the access token is kept, see OnCreatingTicket
        // The callback is a top-level navigation from discord.com, which Lax cookies accompany.
        discord.CorrelationCookie.SameSite = SameSiteMode.Lax;

        discord.Events.OnCreatingTicket = async context =>
        {
            var checker = context.HttpContext.RequestServices.GetRequiredService<IGuildMembershipChecker>();
            var status = await checker.CheckAsync(context.AccessToken!, context.HttpContext.RequestAborted);
            if (status == MembershipStatus.NotMember)
                throw new GuildAccessDeniedException();
            if (status == MembershipStatus.Unavailable)
                throw new HttpRequestException("Discord could not confirm the guild membership.");

            var clock = context.HttpContext.RequestServices.GetRequiredService<TimeProvider>();
            context.Properties.StoreTokens([new AuthenticationToken { Name = SessionRevalidator.AccessTokenName, Value = context.AccessToken! }]);
            SessionRevalidator.MarkChecked(context.Properties, clock.GetUtcNow());

            var displayName = context.User.TryGetProperty("global_name", out var globalName) && globalName.ValueKind == JsonValueKind.String
                ? globalName.GetString()
                : null;
            var username = context.Principal!.FindFirstValue(ClaimTypes.Name);
            context.Identity!.AddClaim(new Claim(DisplayNameClaim, string.IsNullOrWhiteSpace(displayName) ? username ?? "?" : displayName));
        };
        discord.Events.OnRemoteFailure = context =>
        {
            context.Response.Redirect(context.Failure is GuildAccessDeniedException ? "/access-denied" : "/login-failed");
            context.HandleResponse();
            return Task.CompletedTask;
        };
    }
}
