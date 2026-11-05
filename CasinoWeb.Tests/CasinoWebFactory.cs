using System.Net;
using System.Net.Http.Json;
using System.Web;
using AspNet.Security.OAuth.Discord;
using CasinoWeb.Auth;
using DiscordBot.Services;
using DiscordBot.Settings;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CasinoWeb.Tests;

/// <summary>The real casino app with Discord, the database and the clock replaced.</summary>
public sealed class CasinoWebFactory : WebApplicationFactory<Program>
{
    public const string PublicOrigin = "https://casino.test";
    public const ulong GuildId = 777;
    public const long StartingTokens = 1000;

    public string KeysPath { get; } = Directory.CreateTempSubdirectory().FullName;
    public FakeDiscordHandler Discord { get; } = new();
    public FakeCasinoDatabase Database { get; } = new();
    public FakeTimeProvider Clock { get; } = new(new DateTimeOffset(2026, 11, 5, 12, 0, 0, TimeSpan.Zero));
    public BotSettings BotSettings { get; } = new()
    {
        GuildId = GuildId,
        DbConnectionString = "unused",
        Casino = new CasinoSettings { Enabled = true, StartingTokens = StartingTokens },
    };

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Production");
        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Casino:PublicOrigin"] = PublicOrigin,
            ["Casino:KeysPath"] = KeysPath,
            ["Discord:ClientId"] = "client-id",
            ["Discord:ClientSecret"] = "client-secret",
        }));
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<BotSettings>();
            services.AddSingleton(BotSettings);
            services.RemoveAll<ICasinoDatabase>();
            services.AddSingleton<ICasinoDatabase>(Database);
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(Clock);

            services.Configure<DiscordAuthenticationOptions>(DiscordAuthenticationDefaults.AuthenticationScheme,
                options => options.BackchannelHttpHandler = Discord);
            services.AddHttpClient(nameof(IGuildMembershipChecker)).ConfigurePrimaryHttpMessageHandler(() => Discord);
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing && Directory.Exists(KeysPath))
            Directory.Delete(KeysPath, recursive: true);
    }

    /// <summary>A browser: keeps cookies, does not follow redirects so a test can read them.</summary>
    public HttpClient CreateBrowser() => CreateClient(new WebApplicationFactoryClientOptions
    {
        AllowAutoRedirect = false,
        BaseAddress = new Uri(PublicOrigin),
    });

    /// <summary>Goes through the login redirect and the OAuth callback, and returns the callback's response.</summary>
    public static async Task<HttpResponseMessage> LogInAsync(HttpClient browser, string? returnUrl = null)
    {
        var start = await browser.GetAsync(returnUrl is null ? "/login" : $"/login?returnUrl={Uri.EscapeDataString(returnUrl)}");
        Assert.Equal(HttpStatusCode.Redirect, start.StatusCode);
        var state = HttpUtility.ParseQueryString(start.Headers.Location!.Query)["state"];
        return await browser.GetAsync($"/signin-discord?code=the-code&state={Uri.EscapeDataString(state!)}");
    }

    public static async Task<MeBody> GetMeAsync(HttpClient browser) =>
        (await browser.GetFromJsonAsync<MeBody>("/api/me"))!;

    public sealed record MeBody(string Id, string Name, string AvatarUrl, long? Balance, bool CasinoEnabled);
}
