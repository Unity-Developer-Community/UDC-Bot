using System.Net;
using System.Net.Http.Headers;
using System.Web;

namespace CasinoWeb.Tests;

public sealed class LoginTests : IDisposable
{
    // One app per test: the clock, the fake Discord and the fake table are mutated by the tests.
    private readonly CasinoWebFactory _factory = new();

    public void Dispose() => _factory.Dispose();

    private static bool HasCasinoCookie(HttpResponseMessage response) =>
        response.Headers.TryGetValues("Set-Cookie", out var cookies)
        && cookies.Any(c => c.StartsWith("udc_casino=", StringComparison.Ordinal) && !c.Contains("expires=Thu, 01 Jan 1970", StringComparison.OrdinalIgnoreCase));

    [Fact]
    public async Task Login_sends_the_player_to_discord_with_the_public_redirect_url_and_both_scopes()
    {
        var browser = _factory.CreateBrowser();

        var response = await browser.GetAsync("/login");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var location = response.Headers.Location!;
        Assert.Equal("discord.com", location.Host);
        var query = HttpUtility.ParseQueryString(location.Query);
        Assert.Equal($"{CasinoWebFactory.PublicOrigin}/signin-discord", query["redirect_uri"]);
        Assert.Equal("client-id", query["client_id"]);
        Assert.Contains("guilds.members.read", query["scope"]);
        Assert.Contains("identify", query["scope"]);
    }

    [Fact]
    public async Task A_guild_member_gets_a_secure_http_only_lax_cookie_and_sees_their_profile_and_balance()
    {
        var browser = _factory.CreateBrowser();

        var callback = await CasinoWebFactory.LogInAsync(browser);

        Assert.Equal(HttpStatusCode.Redirect, callback.StatusCode);
        Assert.Equal("/", callback.Headers.Location!.OriginalString);
        var cookie = callback.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith("udc_casino=", StringComparison.Ordinal));
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("secure", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=lax", cookie, StringComparison.OrdinalIgnoreCase);

        var me = await CasinoWebFactory.GetMeAsync(browser);
        Assert.Equal(FakeDiscordHandler.UserId, me.Id);
        Assert.Equal("Ada Lovelace", me.Name);
        Assert.Equal($"https://cdn.discordapp.com/avatars/{FakeDiscordHandler.UserId}/abc123.png?size=128", me.AvatarUrl);
        Assert.Equal(CasinoWebFactory.StartingTokens, me.Balance);
        Assert.True(me.CasinoEnabled);
        Assert.Equal(FakeDiscordHandler.AccessToken, _factory.Discord.LastMemberLookupToken);
    }

    [Fact]
    public async Task Data_protection_keys_are_written_to_the_configured_folder()
    {
        var browser = _factory.CreateBrowser();

        await CasinoWebFactory.LogInAsync(browser);

        Assert.NotEmpty(Directory.GetFiles(_factory.KeysPath, "key-*.xml"));
    }

    [Fact]
    public async Task The_balance_is_the_one_in_the_casino_table()
    {
        var browser = _factory.CreateBrowser();
        await CasinoWebFactory.LogInAsync(browser);
        await CasinoWebFactory.GetMeAsync(browser); // creates the row, as /casino tokens balance does
        _factory.Database.Repo.Users[FakeDiscordHandler.UserId].Tokens = 4321;

        var me = await CasinoWebFactory.GetMeAsync(browser);

        Assert.Equal(4321, me.Balance);
    }

    [Fact]
    public async Task A_non_member_is_sent_to_access_denied_and_gets_no_cookie()
    {
        _factory.Discord.MemberStatus = HttpStatusCode.NotFound;
        var browser = _factory.CreateBrowser();

        var callback = await CasinoWebFactory.LogInAsync(browser);

        Assert.Equal(HttpStatusCode.Redirect, callback.StatusCode);
        Assert.Equal("/access-denied", callback.Headers.Location!.OriginalString);
        Assert.False(HasCasinoCookie(callback));
        Assert.Equal(HttpStatusCode.Forbidden, (await browser.GetAsync("/access-denied")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await browser.GetAsync("/api/me")).StatusCode);
    }

    [Fact]
    public async Task When_discord_cannot_confirm_membership_login_fails_without_a_cookie()
    {
        _factory.Discord.MemberStatus = HttpStatusCode.InternalServerError;
        var browser = _factory.CreateBrowser();

        var callback = await CasinoWebFactory.LogInAsync(browser);

        Assert.Equal("/login-failed", callback.Headers.Location!.OriginalString);
        Assert.False(HasCasinoCookie(callback));
    }

    [Theory]
    [InlineData("/play", "/play")]
    [InlineData("https://evil.example/", "/")]
    [InlineData("//evil.example", "/")]
    [InlineData("/\\evil.example", "/")]
    [InlineData("/\t/evil.example", "/")]
    [InlineData("/\n/evil.example", "/")]
    public async Task Login_returns_only_to_a_path_on_this_site(string returnUrl, string expected)
    {
        var browser = _factory.CreateBrowser();

        var callback = await CasinoWebFactory.LogInAsync(browser, returnUrl);

        Assert.Equal(expected, callback.Headers.Location!.OriginalString);
    }

    [Fact]
    public async Task A_closed_casino_reports_it_and_hides_the_balance()
    {
        var browser = _factory.CreateBrowser();
        await CasinoWebFactory.LogInAsync(browser);
        _factory.BotSettings.Casino.Enabled = false;

        var me = await CasinoWebFactory.GetMeAsync(browser);

        Assert.False(me.CasinoEnabled);
        Assert.Null(me.Balance);
        Assert.Equal("Ada Lovelace", me.Name);
    }

    [Fact]
    public async Task Requests_without_a_valid_cookie_are_refused()
    {
        var browser = _factory.CreateBrowser();
        Assert.Equal(HttpStatusCode.Unauthorized, (await browser.GetAsync("/api/me")).StatusCode);

        var forged = new HttpRequestMessage(HttpMethod.Get, "/api/me");
        forged.Headers.Add("Cookie", "udc_casino=CfDJ8-not-a-real-ticket");
        Assert.Equal(HttpStatusCode.Unauthorized, (await _factory.CreateBrowser().SendAsync(forged)).StatusCode);
    }

    [Fact]
    public async Task Logout_ends_the_session()
    {
        var browser = _factory.CreateBrowser();
        await CasinoWebFactory.LogInAsync(browser);

        var logout = await browser.PostAsync("/api/logout", null);

        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await browser.GetAsync("/api/me")).StatusCode);
    }

    [Fact]
    public async Task Logout_from_another_origin_is_refused_and_the_session_stays()
    {
        var browser = _factory.CreateBrowser();
        await CasinoWebFactory.LogInAsync(browser);

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/logout");
        request.Headers.Add("Origin", "https://evil.example");
        var logout = await browser.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, logout.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await browser.GetAsync("/api/me")).StatusCode);
    }

    [Fact]
    public async Task A_removed_member_is_signed_out_at_the_next_check_after_ten_minutes()
    {
        var browser = _factory.CreateBrowser();
        await CasinoWebFactory.LogInAsync(browser);
        _factory.Discord.MemberStatus = HttpStatusCode.NotFound;

        _factory.Clock.Advance(TimeSpan.FromMinutes(9));
        Assert.Equal(HttpStatusCode.OK, (await browser.GetAsync("/api/me")).StatusCode);

        _factory.Clock.Advance(TimeSpan.FromMinutes(2));
        var refused = await browser.GetAsync("/api/me");
        Assert.Equal(HttpStatusCode.Unauthorized, refused.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await browser.GetAsync("/api/me")).StatusCode);
    }

    [Fact]
    public async Task A_member_who_stays_is_rechecked_only_every_ten_minutes_and_keeps_playing()
    {
        var browser = _factory.CreateBrowser();
        await CasinoWebFactory.LogInAsync(browser);
        var lookupsAfterLogin = _factory.Discord.MemberLookups;

        await browser.GetAsync("/api/me");
        Assert.Equal(lookupsAfterLogin, _factory.Discord.MemberLookups);

        _factory.Clock.Advance(TimeSpan.FromMinutes(11));
        Assert.Equal(HttpStatusCode.OK, (await browser.GetAsync("/api/me")).StatusCode);
        Assert.Equal(lookupsAfterLogin + 1, _factory.Discord.MemberLookups);

        await browser.GetAsync("/api/me");
        Assert.Equal(lookupsAfterLogin + 1, _factory.Discord.MemberLookups);
    }
}
