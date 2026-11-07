using System.Net;
using System.Net.Http.Json;
using System.Web;
using AspNet.Security.OAuth.Discord;
using CasinoWeb.Auth;
using CasinoWeb.Games;
using CasinoWeb.Hubs;
using DiscordBot.Domain;
using DiscordBot.Services;
using DiscordBot.Services.Fun.Casino;
using DiscordBot.Services.Logging;
using DiscordBot.Settings;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace CasinoWeb.Tests;

/// <summary>The real casino app with Discord, the database and the clock replaced.</summary>
public sealed class CasinoWebFactory : WebApplicationFactory<Program>
{
    public const string PublicOrigin = "https://casino.test";
    public const ulong GuildId = 777;
    public const long StartingTokens = 1000;

    /// <summary>A valid webhook address whose token must never reach a log line.</summary>
    public const string WebhookToken = "fake-webhook-token_123";
    public const string WebhookUrl = "https://discord.com/api/webhooks/9876543210/" + WebhookToken;

    public string KeysPath { get; } = Directory.CreateTempSubdirectory().FullName;
    public string WebRootPath { get; }
    public FakeDiscordHandler Discord { get; } = new();
    public FakeCasinoDatabase Database { get; } = new();
    public FakeSlotsLedger SlotsLedger { get; } = new();
    public ScriptedSlotRandom SlotRandom { get; } = new();
    public FakeTimeProvider Clock { get; } = new(DateTimeOffset.UtcNow);
    public FakeWebhookHandler Webhook { get; } = new();
    public FakeSlotsRenderer Renderer { get; } = new();
    public CapturingLoggerProvider Logs { get; } = new();

    /// <summary>Set before the first request, when the host is built. Production, like the real deploy, unless a test needs Development.</summary>
    public string Environment { get; set; } = "Production";
    public string? AnnouncementWebhookUrl { get; set; } = WebhookUrl;

    /// <summary>How long the announcement client waits for the webhook; a test of a webhook that never answers shortens it.</summary>
    public TimeSpan? WebhookTimeout { get; set; }

    /// <summary>The cards of the next blackjack hands, one array per hand; a random deck when empty.</summary>
    public Queue<Card[]> BlackjackDecks { get; } = new();
    public BotSettings BotSettings { get; } = new()
    {
        GuildId = GuildId,
        DbConnectionString = "unused",
        Casino = new CasinoSettings { Enabled = true, StartingTokens = StartingTokens },
    };

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(Environment);
        builder.ConfigureLogging(logging => logging.SetMinimumLevel(LogLevel.Trace).AddProvider(Logs));
        builder.UseWebRoot(WebRootPath);
        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Casino:PublicOrigin"] = PublicOrigin,
            ["Casino:KeysPath"] = KeysPath,
            ["Discord:ClientId"] = "client-id",
            ["Discord:ClientSecret"] = "client-secret",
            // Always set, so a developer's user-secrets webhook never leaks into the tests.
            ["Casino:AnnouncementWebhookUrl"] = AnnouncementWebhookUrl ?? string.Empty,
        }));
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<BotSettings>();
            services.AddSingleton(BotSettings);
            services.RemoveAll<ICasinoDatabase>();
            services.AddSingleton<ICasinoDatabase>(Database);
            // The slots rules and settlement run for real; only the balance, the dice and the clock are the test's.
            services.RemoveAll<SlotsService>();
            services.AddSingleton(sp => new SlotsService(SlotsLedger, SlotRandom, null, sp.GetRequiredService<ILoggingService>(), Clock));
            services.RemoveAll<Func<Blackjack>>();
            services.AddSingleton<Func<Blackjack>>(_ => () => BlackjackDecks.TryDequeue(out var cards) ? new RiggedBlackjack(cards) : new Blackjack());
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(Clock);

            services.Configure<DiscordAuthenticationOptions>(DiscordAuthenticationDefaults.AuthenticationScheme,
                options => options.BackchannelHttpHandler = Discord);
            services.AddHttpClient(nameof(IGuildMembershipChecker)).ConfigurePrimaryHttpMessageHandler(() => Discord);

            // No picture drawing on the test machine and no warm-up racing the tests: the warm-up has its own test.
            services.RemoveAll<SlotsRenderer>();
            services.AddSingleton<SlotsRenderer>(Renderer);
            services.Remove(services.Single(d => d.ImplementationType == typeof(SlotsPictureWarmUp)));
            var webhookClient = services.AddHttpClient(SlotsWinAnnouncer.HttpClientName).ConfigurePrimaryHttpMessageHandler(() => Webhook);
            if (WebhookTimeout is { } timeout)
                webhookClient.ConfigureHttpClient(client => client.Timeout = timeout);
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (!disposing)
            return;
        foreach (var folder in new[] { KeysPath, WebRootPath })
        {
            if (Directory.Exists(folder))
                Directory.Delete(folder, recursive: true);
        }
    }

    /// <param name="withClient">Whether the web root holds a built client; false is a checkout that never ran <c>npm run build</c>.</param>
    public CasinoWebFactory(bool withClient = true)
    {
        WebRootPath = Directory.CreateTempSubdirectory().FullName;
        if (withClient)
            File.WriteAllText(Path.Combine(WebRootPath, "index.html"), ClientHtml);
    }

    public const string ClientHtml = "<!doctype html><title>client</title>";

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

    public sealed record MeBody(string Id, string Name, string AvatarUrl, long? Balance, bool CasinoEnabled, DailyInfo? Daily);
}
