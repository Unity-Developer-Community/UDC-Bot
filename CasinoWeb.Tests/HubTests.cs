using System.Net;
using CasinoWeb.Auth;
using CasinoWeb.Hubs;
using DiscordBot.Domain;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;

namespace CasinoWeb.Tests;

public sealed class HubTests : IAsyncDisposable
{
    private const string HubUrl = CasinoWebFactory.PublicOrigin + HubSetup.HubPath;

    // One app per test: the clock, the fake Discord, the dice and the ledger are mutated by the tests.
    private readonly CasinoWebFactory _factory = new();
    private readonly List<HubConnection> _connections = [];

    public async ValueTask DisposeAsync()
    {
        foreach (var connection in _connections)
            await connection.DisposeAsync();
        _factory.Dispose();
    }

    private async Task<string> LogInAndGetCookieAsync()
    {
        var callback = await CasinoWebFactory.LogInAsync(_factory.CreateBrowser());
        var cookie = callback.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith("udc_casino=", StringComparison.Ordinal));
        return cookie[..cookie.IndexOf(';')];
    }

    private HubConnection CreateConnection(string? cookie, string? origin = CasinoWebFactory.PublicOrigin)
    {
        var connection = new HubConnectionBuilder()
            .WithUrl(new Uri(HubUrl), options =>
            {
                options.Transports = HttpTransportType.WebSockets;
                options.HttpMessageHandlerFactory = _ => _factory.Server.CreateHandler();
                if (cookie is not null) options.Headers["Cookie"] = cookie;
                if (origin is not null) options.Headers["Origin"] = origin;
                options.WebSocketFactory = async (context, cancellationToken) =>
                {
                    var client = _factory.Server.CreateWebSocketClient();
                    client.ConfigureRequest = request =>
                    {
                        if (cookie is not null) request.Headers["Cookie"] = cookie;
                        if (origin is not null) request.Headers["Origin"] = origin;
                    };
                    return await client.ConnectAsync(context.Uri, cancellationToken);
                };
            })
            .Build();
        _connections.Add(connection);
        return connection;
    }

    private async Task<HubConnection> ConnectAsync()
    {
        var connection = CreateConnection(await LogInAndGetCookieAsync());
        await connection.StartAsync();
        return connection;
    }

    private static int StopOf(int reel, SlotSymbol symbol) => SlotMachine.Reels[reel].ToList().IndexOf(symbol);

    private void ScriptStops(SlotSymbol first, SlotSymbol second, SlotSymbol third) =>
        _factory.SlotRandom.Enqueue(StopOf(0, first), StopOf(1, second), StopOf(2, third));

    private static async Task<string> RefusalAsync(Func<Task> call) => (await Assert.ThrowsAsync<HubException>(call)).Message;

    #region Access (AC4)

    [Fact]
    public async Task Without_a_cookie_the_hub_refuses_the_connection()
    {
        var connection = CreateConnection(cookie: null);

        var failure = await Assert.ThrowsAsync<HttpRequestException>(() => connection.StartAsync());

        Assert.Equal(HttpStatusCode.Unauthorized, failure.StatusCode);
    }

    [Fact]
    public async Task A_garbage_cookie_is_refused_like_no_cookie()
    {
        var connection = CreateConnection("udc_casino=not-a-ticket");

        var failure = await Assert.ThrowsAsync<HttpRequestException>(() => connection.StartAsync());

        Assert.Equal(HttpStatusCode.Unauthorized, failure.StatusCode);
    }

    [Fact]
    public async Task Another_sites_origin_is_refused_even_with_a_valid_cookie()
    {
        var connection = CreateConnection(await LogInAndGetCookieAsync(), origin: "https://evil.example");

        var failure = await Assert.ThrowsAsync<HttpRequestException>(() => connection.StartAsync());

        Assert.Equal(HttpStatusCode.Forbidden, failure.StatusCode);
    }

    [Theory]
    [InlineData("https://evil.example")]
    [InlineData(null)]
    public async Task A_websocket_handshake_from_another_origin_or_without_one_is_rejected(string? origin)
    {
        var cookie = await LogInAndGetCookieAsync();
        // A real connection id, so the only thing wrong with the handshake is the origin.
        using var http = _factory.CreateClient();
        using var negotiate = new HttpRequestMessage(HttpMethod.Post, $"{HubUrl}/negotiate?negotiateVersion=1");
        negotiate.Headers.Add("Cookie", cookie);
        negotiate.Headers.Add("Origin", CasinoWebFactory.PublicOrigin);
        var token = System.Text.Json.JsonDocument.Parse(await (await http.SendAsync(negotiate)).Content.ReadAsStringAsync())
            .RootElement.GetProperty("connectionToken").GetString();

        var client = _factory.Server.CreateWebSocketClient();
        client.ConfigureRequest = request =>
        {
            request.Headers["Cookie"] = cookie;
            if (origin is not null) request.Headers["Origin"] = origin;
        };
        var failure = await Assert.ThrowsAsync<InvalidOperationException>(
            () => client.ConnectAsync(new Uri($"wss://casino.test{HubSetup.HubPath}?id={token}"), CancellationToken.None));

        Assert.Contains("403", failure.Message);
    }

    [Fact]
    public async Task A_member_with_the_sites_own_origin_connects_and_gets_the_state()
    {
        var connection = await ConnectAsync();

        var state = await connection.InvokeAsync<HubState>(nameof(CasinoHub.GetState));

        Assert.True(state.CasinoEnabled);
        Assert.Equal(CasinoWebFactory.StartingTokens, state.Balance);
        Assert.Equal(SlotMachine.MinBet, state.Slots.MinBet);
        Assert.Equal(SlotMachine.MaxBet, state.Slots.MaxBet);
        Assert.Equal(SlotMachine.ReelCount, state.Slots.Reels.Count);
        Assert.Equal(SlotSettlementMath.JackpotFloorHundredths / SlotSettlementMath.HundredthsPerToken, state.Slots.PotTokens);
        Assert.Contains(state.Slots.Paytable, line => line.IsJackpot && line.Symbol == "diamond");
    }

    #endregion

    #region Slots (AC5)

    [Fact]
    public async Task A_losing_spin_takes_the_bet_and_records_one_slots_transaction()
    {
        var connection = await ConnectAsync();
        ScriptStops(SlotSymbol.Cherry, SlotSymbol.Lemon, SlotSymbol.Grape);
        _factory.SlotRandom.Enqueue(99); // no hold offer

        var play = await connection.InvokeAsync<SlotsPlayDto>(nameof(CasinoHub.SlotsSpin), 10);

        Assert.Equal("ok", play.Status);
        Assert.Equal(["cherry", "lemon", "grape"], play.Result!.Symbols);
        Assert.Equal(0, play.Payout);
        Assert.Equal(-10, play.Net);
        Assert.Equal(990, play.Balance);
        Assert.Null(play.Hold);
        var settlement = Assert.Single(_factory.SlotsLedger.Settlements);
        Assert.Equal(-10, settlement.Net);
        Assert.Equal("Slots", settlement.Details["game"]);
    }

    [Fact]
    public async Task A_winning_spin_pays_the_paytable_multiple()
    {
        var connection = await ConnectAsync();
        ScriptStops(SlotSymbol.Cherry, SlotSymbol.Cherry, SlotSymbol.Lemon);

        var play = await connection.InvokeAsync<SlotsPlayDto>(nameof(CasinoHub.SlotsSpin), 10);

        Assert.Equal(20, play.Payout);
        Assert.Equal(10, play.Net);
        Assert.Equal(1010, play.Balance);
        Assert.Equal(2, play.Result!.Multiplier);
    }

    [Fact]
    public async Task A_jackpot_pays_a_share_of_the_pot_and_is_flagged()
    {
        var connection = await ConnectAsync();
        ScriptStops(SlotSymbol.Diamond, SlotSymbol.Diamond, SlotSymbol.Diamond);

        var play = await connection.InvokeAsync<SlotsPlayDto>(nameof(CasinoHub.SlotsSpin), 100);

        Assert.True(play.Result!.IsJackpot);
        Assert.True(play.Payout >= 1000);
        Assert.True(_factory.SlotsLedger.Settlements.Single().Details.ContainsKey("jackpot"));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(SlotMachine.MaxBet + 1)]
    public async Task A_bet_outside_the_limits_is_refused_without_touching_the_balance(int bet)
    {
        var connection = await ConnectAsync();

        var play = await connection.InvokeAsync<SlotsPlayDto>(nameof(CasinoHub.SlotsSpin), bet);

        Assert.Equal("invalidBet", play.Status);
        Assert.Equal(1000, _factory.SlotsLedger.Tokens);
        Assert.Empty(_factory.SlotsLedger.Settlements);
    }

    [Fact]
    public async Task A_bet_above_the_balance_is_refused()
    {
        var connection = await ConnectAsync();
        _factory.SlotsLedger.Tokens = 5;

        var play = await connection.InvokeAsync<SlotsPlayDto>(nameof(CasinoHub.SlotsSpin), 10);

        Assert.Equal("insufficientTokens", play.Status);
        Assert.Equal(5, _factory.SlotsLedger.Tokens);
    }

    [Fact]
    public async Task A_hold_offer_can_be_toggled_and_spent_once_on_a_free_respin()
    {
        var connection = await ConnectAsync();
        ScriptStops(SlotSymbol.Cherry, SlotSymbol.Lemon, SlotSymbol.Grape);
        _factory.SlotRandom.Enqueue(0); // hold offered
        var spin = await connection.InvokeAsync<SlotsPlayDto>(nameof(CasinoHub.SlotsSpin), 10);
        var offer = spin.Hold!;
        Assert.Equal([false, false, false], offer.Held);

        var toggled = await connection.InvokeAsync<SlotsHoldDto?>(nameof(CasinoHub.SlotsToggleHold), offer.OfferId, 0);
        Assert.Equal([true, false, false], toggled!.Held);

        // The held cherry stays; the other two reels roll to a second cherry and a lemon: a pair pays 2x.
        _factory.SlotRandom.Enqueue(StopOf(1, SlotSymbol.Cherry), StopOf(2, SlotSymbol.Lemon));
        var respin = await connection.InvokeAsync<SlotsPlayDto>(nameof(CasinoHub.SlotsRespin), offer.OfferId);

        Assert.Equal("ok", respin.Status);
        Assert.True(respin.Result!.IsRespin);
        Assert.Equal(20, respin.Payout);
        Assert.Equal(20, respin.Net);
        Assert.Equal(1010, respin.Balance);
        Assert.Null(respin.Hold);

        var again = await connection.InvokeAsync<SlotsPlayDto>(nameof(CasinoHub.SlotsRespin), offer.OfferId);
        Assert.Equal("noHold", again.Status);
        Assert.Null(await connection.InvokeAsync<SlotsHoldDto?>(nameof(CasinoHub.SlotsToggleHold), offer.OfferId, 0));
    }

    [Fact]
    public async Task An_unknown_hold_offer_is_refused()
    {
        var connection = await ConnectAsync();

        var toggled = await connection.InvokeAsync<SlotsHoldDto?>(nameof(CasinoHub.SlotsToggleHold), 12345L, 0);

        Assert.Null(toggled);
    }

    #endregion

    #region Rate limit (AC9)

    [Fact]
    public async Task The_sixth_action_within_a_second_is_refused_and_the_limit_recovers_after_it()
    {
        var connection = await ConnectAsync();
        for (var i = 0; i < ActionRateLimiter.MaxActions; i++)
            await connection.InvokeAsync<SlotsHoldDto?>(nameof(CasinoHub.SlotsToggleHold), 1L, 0);

        Assert.Contains(HubErrors.SlowDown, await RefusalAsync(() => connection.InvokeAsync<SlotsHoldDto?>(nameof(CasinoHub.SlotsToggleHold), 1L, 0)));

        _factory.Clock.Advance(TimeSpan.FromSeconds(1));
        await connection.InvokeAsync<SlotsHoldDto?>(nameof(CasinoHub.SlotsToggleHold), 1L, 0);
    }

    [Fact]
    public void The_limit_is_per_player()
    {
        var limiter = new ActionRateLimiter(_factory.Clock);
        for (var i = 0; i < ActionRateLimiter.MaxActions; i++)
            Assert.True(limiter.TryAcquire("a"));

        Assert.False(limiter.TryAcquire("a"));
        Assert.True(limiter.TryAcquire("b"));
    }

    [Fact]
    public async Task Reading_the_state_does_not_use_up_the_limit()
    {
        var connection = await ConnectAsync();

        for (var i = 0; i < ActionRateLimiter.MaxActions * 2; i++)
            await connection.InvokeAsync<HubState>(nameof(CasinoHub.GetState));
    }

    #endregion

    #region Casino closed (AC10)

    [Fact]
    public async Task While_the_casino_is_closed_games_are_refused_and_the_state_says_so()
    {
        _factory.BotSettings.Casino.Enabled = false;
        var connection = await ConnectAsync();

        var state = await connection.InvokeAsync<HubState>(nameof(CasinoHub.GetState));
        var refusal = await RefusalAsync(() => connection.InvokeAsync<SlotsPlayDto>(nameof(CasinoHub.SlotsSpin), 10));

        Assert.False(state.CasinoEnabled);
        Assert.Contains(HubErrors.CasinoClosed, refusal);
        Assert.Equal(1000, _factory.SlotsLedger.Tokens);
    }

    #endregion

    #region Membership (AC3)

    [Fact]
    public async Task An_open_connection_keeps_working_while_the_last_check_is_recent()
    {
        var connection = await ConnectAsync();
        var lookups = _factory.Discord.MemberLookups;
        _factory.Discord.MemberStatus = HttpStatusCode.NotFound;

        _factory.Clock.Advance(TimeSpan.FromMinutes(9));
        await connection.InvokeAsync<HubState>(nameof(CasinoHub.GetState));

        Assert.Equal(lookups, _factory.Discord.MemberLookups);
    }

    [Fact]
    public async Task A_member_who_left_the_guild_is_cut_off_on_the_next_call_after_ten_minutes()
    {
        var cookie = await LogInAndGetCookieAsync();
        var connection = CreateConnection(cookie);
        await connection.StartAsync();
        var closed = new TaskCompletionSource();
        connection.Closed += _ =>
        {
            closed.TrySetResult();
            return Task.CompletedTask;
        };
        _factory.Discord.MemberStatus = HttpStatusCode.NotFound;
        _factory.Clock.Advance(SessionRevalidator.Interval);
        ScriptStops(SlotSymbol.Cherry, SlotSymbol.Lemon, SlotSymbol.Grape);

        // The server drops the socket as it refuses, so the caller sees either the refusal or the closed connection.
        await Assert.ThrowsAnyAsync<Exception>(() => connection.InvokeAsync<SlotsPlayDto>(nameof(CasinoHub.SlotsSpin), 10));

        await closed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(1000, _factory.SlotsLedger.Tokens);
        Assert.Empty(_factory.SlotsLedger.Settlements);
        var again = await Assert.ThrowsAsync<HttpRequestException>(() => CreateConnection(cookie).StartAsync());
        Assert.Equal(HttpStatusCode.Unauthorized, again.StatusCode);
    }

    [Fact]
    public async Task When_discord_cannot_answer_the_connection_is_kept()
    {
        var connection = await ConnectAsync();
        _factory.Discord.MemberStatus = HttpStatusCode.ServiceUnavailable;
        _factory.Clock.Advance(SessionRevalidator.Interval);

        var state = await connection.InvokeAsync<HubState>(nameof(CasinoHub.GetState));

        Assert.True(state.CasinoEnabled);
    }

    #endregion
}
