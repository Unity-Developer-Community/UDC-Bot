using System.Collections.Concurrent;
using CasinoWeb.Hubs;
using CasinoWeb.Tables;
using CasinoWeb.Tests.Poker;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;

namespace CasinoWeb.Tests;

/// <summary>
/// The poker tables over real SignalR clients. The sweeper is off in these tests: a test deals the next hand and moves the
/// turn clocks itself with <see cref="TickAsync"/>. Ann sits at seat 0 and Bob at seat 1 of a 5/10 table; the first
/// hand's button is seat 0, so heads-up Ann posts the small blind and acts first. The rigged deck gives Ann aces and Bob kings.
/// </summary>
public sealed class TableHubTests : IAsyncDisposable
{
    private const string Ann = "1001";
    private const string Bob = "1002";
    private const string Cid = "1003";
    private const long BigBlind = 10;

    private static readonly CardDto[] AnnCards = [new("A", "clubs"), new("A", "diamonds")];
    private static readonly CardDto[] BobCards = [new("K", "clubs"), new("K", "diamonds")];

    private readonly CasinoWebFactory _factory = new();
    private readonly List<HubConnection> _connections = [];

    public async ValueTask DisposeAsync()
    {
        foreach (var connection in _connections)
            await connection.DisposeAsync();
        _factory.Dispose();
    }

    private sealed class Player(string id, string cookie)
    {
        public string Id { get; } = id;

        public string Cookie { get; } = cookie;

        public HubConnection Connection { get; set; } = null!;

        public ConcurrentQueue<TableUpdateDto> Updates { get; } = new();

        public ConcurrentQueue<TableSummaryDto[]> Lists { get; } = new();

        public Task<T> InvokeAsync<T>(string method, params object?[] args) => Connection.InvokeCoreAsync<T>(method, args);

        public Task<TableCallDto> Call(string method, params object?[] args) => InvokeAsync<TableCallDto>(method, args);

        public Task<TableCallDto> Act(string action, long amount = 0) => Call(nameof(CasinoHub.PokerAct), action, amount);

        public Task<HubState> State() => InvokeAsync<HubState>(nameof(CasinoHub.GetState));
    }

    private async Task<Player> ConnectAsync(string id)
    {
        var player = new Player(id, await _factory.LogInAsAsync(id));
        await OpenAsync(player);
        return player;
    }

    private async Task OpenAsync(Player player)
    {
        var connection = _factory.CreateHubConnection(player.Cookie);
        connection.On<TableUpdateDto>("TableUpdated", player.Updates.Enqueue);
        connection.On<TableSummaryDto[]>("TablesChanged", player.Lists.Enqueue);
        _connections.Add(connection);
        await connection.StartAsync();
        player.Connection = connection;
    }

    private static async Task<T> WaitAsync<T>(Func<T?> probe)
        where T : class
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < deadline)
        {
            if (probe() is { } found)
                return found;
            await Task.Delay(10);
        }

        throw new TimeoutException("The expected push never arrived.");
    }

    private static Task<TableUpdateDto> UpdateWhereAsync(Player player, Func<TableViewDto, bool> match) =>
        WaitAsync(() => player.Updates.ToArray().FirstOrDefault(u => u.View is { } view && match(view)));

    private static Task<TableSummaryDto[]> ListWhereAsync(Player player, Func<TableSummaryDto[], bool> match) =>
        WaitAsync(() => player.Lists.ToArray().FirstOrDefault(match));

    private async Task TickAsync(TimeSpan? advance = null)
    {
        if (advance is { } by)
            _factory.Clock.Advance(by);
        await _factory.Services.GetRequiredService<TableService>().TickAsync();
    }

    /// <summary>Moves the fake clock one second, so the 5 actions per second limit does not get in the way.</summary>
    private void Pace() => _factory.Clock.Advance(TimeSpan.FromSeconds(1));

    private void RigDeck() => _factory.PokerDecks.Enqueue(HoldemTestKit.Deck(0, 2, null, "2h 7s 9h Js 3d"));

    private static async Task<string> RefusalAsync(Func<Task> call) => (await Assert.ThrowsAsync<HubException>(call)).Message;

    /// <summary>Ann opens a table, Bob sits next to her and the first hand is dealt.</summary>
    private async Task<(Player Ann, Player Bob)> StartHeadsUpHandAsync()
    {
        var ann = await ConnectAsync(Ann);
        var bob = await ConnectAsync(Bob);
        RigDeck();
        Assert.Equal("ok", (await ann.Call(nameof(CasinoHub.TableCreate), BigBlind)).Status);
        Assert.Equal("ok", (await bob.Call(nameof(CasinoHub.TableSit), 1, (int?)null)).Status);
        await TickAsync();
        await UpdateWhereAsync(ann, view => view.HandRunning);
        await UpdateWhereAsync(bob, view => view.HandRunning);
        return (ann, bob);
    }

    /// <summary>Both players check or call until the hand is over.</summary>
    private async Task CheckDownAsync(Player ann, Player bob)
    {
        var view = (await ann.State()).Poker.Table!;
        while (view.HandRunning)
        {
            var actor = view.TurnSeat == 0 ? ann : bob;
            Pace();
            var result = await actor.Act("check");
            if (result.Status != "ok")
                result = await actor.Act("call");
            Assert.Equal("ok", result.Status);
            view = result.View!;
        }
    }

    #region The lobby (AC1)

    [Fact]
    public async Task The_lobby_lists_open_tables_and_follows_them_as_they_fill_start_and_close()
    {
        var ann = await ConnectAsync(Ann);
        var bob = await ConnectAsync(Bob);
        Assert.Empty(await ann.InvokeAsync<TableSummaryDto[]>(nameof(CasinoHub.TablesList)));

        var created = await bob.Call(nameof(CasinoHub.TableCreate), BigBlind);

        Assert.Equal("ok", created.Status);
        Assert.Equal(0, created.View!.MySeat);
        Assert.Equal(new StakesDto(5, 10, 100, 1000), created.View.Stakes);
        var opened = Assert.Single(await ListWhereAsync(ann, rows => rows.Length == 1));
        Assert.Equal(new TableSummaryDto(1, new StakesDto(5, 10, 100, 1000), 1, 6, false), opened);

        Assert.Equal("ok", (await ann.Call(nameof(CasinoHub.TableSit), 1, (int?)null)).Status);
        Assert.Equal(2, Assert.Single(await ListWhereAsync(bob, rows => rows is [{ SeatsTaken: 2 }])).SeatsTaken);

        await TickAsync();
        Assert.True(Assert.Single(await ListWhereAsync(ann, rows => rows is [{ HandRunning: true }])).HandRunning);
        var cid = await ConnectAsync(Cid);
        var late = Assert.Single(await cid.InvokeAsync<TableSummaryDto[]>(nameof(CasinoHub.TablesList)));
        Assert.Equal((1, 2, true), (late.Id, late.SeatsTaken, late.HandRunning));

        await ann.Call(nameof(CasinoHub.TableLeave));
        await bob.Call(nameof(CasinoHub.TableLeave));
        await ListWhereAsync(cid, rows => rows.Length == 0);
        Assert.Empty(await cid.InvokeAsync<TableSummaryDto[]>(nameof(CasinoHub.TablesList)));
    }

    [Fact]
    public async Task The_state_carries_the_stakes_presets_and_the_table_the_member_sits_at()
    {
        var ann = await ConnectAsync(Ann);
        var before = (await ann.State()).Poker;
        Assert.Equal([new StakesDto(1, 2, 20, 200), new StakesDto(5, 10, 100, 1000), new StakesDto(25, 50, 500, 5000)], before.Stakes);
        Assert.False(before.AllowAiSeats);
        Assert.Null(before.Table);

        await ann.Call(nameof(CasinoHub.TableCreate), BigBlind);

        var table = (await ann.State()).Poker.Table!;
        Assert.Equal(1, table.Id);
        Assert.Equal(0, table.MySeat);
        Assert.Equal("Player 1001", Assert.Single(table.Seats).Name);
    }

    [Fact]
    public async Task Refused_table_calls_answer_with_the_reason()
    {
        var ann = await ConnectAsync(Ann);
        var bob = await ConnectAsync(Bob);

        Assert.Equal("badStakes", (await ann.Call(nameof(CasinoHub.TableCreate), 7L)).Status);
        Assert.Equal("noSuchTable", (await ann.Call(nameof(CasinoHub.TableSit), 9, (int?)null)).Status);
        Assert.Equal("notSeated", (await ann.Call(nameof(CasinoHub.TableLeave))).Status);
        await ann.Call(nameof(CasinoHub.TableCreate), BigBlind);
        Pace();
        Assert.Equal("alreadySeated", (await ann.Call(nameof(CasinoHub.TableCreate), BigBlind)).Status);
        Assert.Equal("seatTaken", (await bob.Call(nameof(CasinoHub.TableSit), 1, 0)).Status);
        Assert.Equal("noSuchSeat", (await bob.Call(nameof(CasinoHub.TableSit), 1, 6)).Status);
        _factory.Database.Repo.Users[Bob] = new DiscordBot.Domain.CasinoUser { UserID = Bob, Tokens = 99 };
        Assert.Equal("belowMinimum", (await bob.Call(nameof(CasinoHub.TableSit), 1, (int?)null)).Status);
    }

    #endregion

    #region Hidden cards (AC8)

    [Fact]
    public async Task Each_player_gets_only_their_own_hole_cards_and_a_watcher_none_until_the_showdown()
    {
        var ann = await ConnectAsync(Ann);
        var bob = await ConnectAsync(Bob);
        var cid = await ConnectAsync(Cid);
        RigDeck();
        await ann.Call(nameof(CasinoHub.TableCreate), BigBlind);
        var watched = await cid.Call(nameof(CasinoHub.TableWatch), 1);
        Assert.Equal("ok", watched.Status);
        Assert.Null(watched.View!.MySeat);
        await bob.Call(nameof(CasinoHub.TableSit), 1, (int?)null);
        await TickAsync();
        await UpdateWhereAsync(cid, view => view.HandRunning);

        var annView = (await UpdateWhereAsync(ann, view => view.HandRunning)).View!;
        var bobView = (await UpdateWhereAsync(bob, view => view.HandRunning)).View!;
        var watcherView = (await UpdateWhereAsync(cid, view => view.HandRunning)).View!;
        Assert.Equal(AnnCards, annView.Hand!.Seats.Single(s => s.Seat == 0).HoleCards);
        Assert.Null(annView.Hand.Seats.Single(s => s.Seat == 1).HoleCards);
        Assert.Equal(BobCards, bobView.Hand!.Seats.Single(s => s.Seat == 1).HoleCards);
        Assert.Null(bobView.Hand.Seats.Single(s => s.Seat == 0).HoleCards);
        Assert.All(watcherView.Hand!.Seats, seat => Assert.Null(seat.HoleCards));
        // The options of the player to act go to that player only.
        Assert.NotNull(annView.Hand.Options);
        Assert.Null(bobView.Hand.Options);
        Assert.Null(watcherView.Hand.Options);

        await CheckDownAsync(ann, bob);

        foreach (var player in new[] { ann, bob, cid })
        {
            var last = (await UpdateWhereAsync(player, view => view.Hand?.Result is not null)).View!.Hand!;
            Assert.True(last.Result!.Showdown);
            Assert.Equal([AnnCards, BobCards], last.Seats.OrderBy(s => s.Seat).Select(s => s.HoleCards));
            Assert.Equal([0, 1], last.Result.Shown.Select(h => h.Seat));
        }

        // Before the showdown no payload of anyone showed a card that was not the recipient's own.
        foreach (var (player, own) in new[] { (ann, 0), (bob, 1), (cid, -1) })
        {
            foreach (var hand in player.Updates.ToArray().Select(u => u.View?.Hand).Where(h => h is { Result: null }))
                Assert.All(hand!.Seats.Where(s => s.Seat != own), seat => Assert.Null(seat.HoleCards));
        }
    }

    [Fact]
    public async Task A_hand_won_by_folding_shows_nobodys_cards()
    {
        var (ann, bob) = await StartHeadsUpHandAsync();
        var cid = await ConnectAsync(Cid);
        await cid.Call(nameof(CasinoHub.TableWatch), 1);

        Assert.Equal("ok", (await ann.Act("fold")).Status);

        foreach (var player in new[] { ann, bob })
        {
            var hand = (await UpdateWhereAsync(player, view => view.Hand?.Result is not null)).View!.Hand!;
            Assert.False(hand.Result!.Showdown);
            Assert.Empty(hand.Result.Shown);
            Assert.Equal(10 + 5, hand.Result.Winnings.Single(w => w.Seat == 1).Amount);
        }

        Assert.Null((await UpdateWhereAsync(bob, view => view.Hand?.Result is not null)).View!.Hand!.Seats.Single(s => s.Seat == 0).HoleCards);
        Assert.Null((await UpdateWhereAsync(ann, view => view.Hand?.Result is not null)).View!.Hand!.Seats.Single(s => s.Seat == 1).HoleCards);
        await TickAsync(TimeSpan.FromSeconds(5));
        Assert.All(cid.Updates.ToArray().Select(u => u.View?.Hand).Where(h => h is not null), hand =>
            Assert.All(hand!.Seats, seat => Assert.Null(seat.HoleCards)));
    }

    [Fact]
    public async Task A_bad_action_is_refused_with_the_reason_and_the_hand_goes_on()
    {
        var (ann, bob) = await StartHeadsUpHandAsync();

        var outOfTurn = await bob.Act("check");
        Pace();
        var tooLow = await ann.Act("raise", 11);
        Pace();
        var unknown = await ann.Act("dance");

        Assert.Equal(("refused", "notYourTurn"), (outOfTurn.Status, outOfTurn.Refusal));
        Assert.Equal(("refused", "amountTooLow"), (tooLow.Status, tooLow.Refusal));
        Assert.Equal(("refused", "notAllowed"), (unknown.Status, unknown.Refusal));
        Assert.Equal(["fold", "call", "raise", "allIn"], unknown.View!.Hand!.Options!.Actions);
        Assert.Equal(995, _factory.Database.Repo.Users[Ann].Tokens);
    }

    #endregion

    #region Disconnects (AC10)

    [Fact]
    public async Task A_player_who_reconnects_within_a_minute_gets_the_table_back_with_their_own_cards()
    {
        var (ann, bob) = await StartHeadsUpHandAsync();

        await ann.Connection.StopAsync();
        var away = await UpdateWhereAsync(bob, view => view.Seats.Any(s => s.Seat == 0 && s.Away));
        Assert.True(away.View!.HandRunning);
        await TickAsync(TimeSpan.FromSeconds(20)); // her turn would time out at 30 s

        await OpenAsync(ann);
        var table = (await ann.State()).Poker.Table!;

        Assert.Equal(0, table.MySeat);
        Assert.All(table.Seats, seat => Assert.False(seat.Away));
        Assert.True(table.HandRunning);
        Assert.Equal(AnnCards, table.Hand!.Seats.Single(s => s.Seat == 0).HoleCards);
        await UpdateWhereAsync(bob, view => view.Seats.All(s => !s.Away) && view.Seats.Count == 2);
    }

    [Fact]
    public async Task A_player_who_stays_away_for_a_minute_folds_and_loses_the_seat()
    {
        var (ann, bob) = await StartHeadsUpHandAsync();

        await ann.Connection.StopAsync();
        await UpdateWhereAsync(bob, view => view.Seats.Any(s => s.Seat == 0 && s.Away));
        await TickAsync(TimeSpan.FromSeconds(61));

        var over = (await UpdateWhereAsync(bob, view => view.Hand?.Result is not null)).View!;
        Assert.False(over.Hand!.Result!.Showdown);
        Assert.Equal([1], over.Seats.Select(s => s.Seat));
        await OpenAsync(ann);
        var state = await ann.State();
        Assert.Null(state.Poker.Table);
        Assert.Equal(995, state.Balance);
        Assert.Equal(1005, (await bob.State()).Balance);
    }

    [Fact]
    public async Task A_second_connection_keeps_the_seat_present_when_the_first_one_closes()
    {
        var (ann, bob) = await StartHeadsUpHandAsync();
        var firstConnection = ann.Connection;
        await OpenAsync(ann);

        await firstConnection.StopAsync();
        await TickAsync(TimeSpan.FromSeconds(61));

        var table = (await ann.State()).Poker.Table!;
        Assert.Equal(0, table.MySeat);
        Assert.All(table.Seats, seat => Assert.False(seat.Away));
        Assert.DoesNotContain(bob.Updates.ToArray(), u => u.View is { } view && view.Seats.Any(s => s.Away));
    }

    #endregion

    #region AI seats (AC12)

    [Fact]
    public async Task When_allowed_a_seated_player_adds_and_removes_AI_seats_that_never_touch_a_balance()
    {
        _factory.AllowAiSeats = true;
        var ann = await ConnectAsync(Ann);
        Assert.True((await ann.State()).Poker.AllowAiSeats);
        await ann.Call(nameof(CasinoHub.TableCreate), BigBlind);

        var added = await ann.Call(nameof(CasinoHub.TableAddAi));

        Assert.Equal("ok", added.Status);
        Assert.Equal([("Player 1001", false), ("AI 1", true)], added.View!.Seats.Select(s => (s.Name, s.IsAi)));
        await TickAsync();
        Assert.True((await ann.State()).Poker.Table!.HandRunning);
        Assert.Equal(995, Assert.Single(_factory.Database.Repo.Users).Value.Tokens);

        Pace();
        var removed = await ann.Call(nameof(CasinoHub.TableRemoveAi));
        Assert.Equal("ok", removed.Status);
        Assert.DoesNotContain(removed.View!.Seats, s => s.IsAi);
        Pace();
        Assert.Equal("noAiSeat", (await ann.Call(nameof(CasinoHub.TableRemoveAi))).Status);
    }

    [Fact]
    public async Task When_not_allowed_the_hub_refuses_AI_seats()
    {
        var ann = await ConnectAsync(Ann);
        Assert.False((await ann.State()).Poker.AllowAiSeats);
        await ann.Call(nameof(CasinoHub.TableCreate), BigBlind);

        Assert.Equal("aiNotAllowed", (await ann.Call(nameof(CasinoHub.TableAddAi))).Status);
        Assert.Equal("aiNotAllowed", (await ann.Call(nameof(CasinoHub.TableRemoveAi))).Status);
        Assert.Single((await ann.State()).Poker.Table!.Seats);
    }

    #endregion

    #region The hub filter (AC13)

    [Fact]
    public async Task While_the_casino_is_closed_only_watching_and_leaving_are_allowed()
    {
        var (ann, bob) = await StartHeadsUpHandAsync();
        var cid = await ConnectAsync(Cid);
        _factory.BotSettings.Casino.Enabled = false;

        Assert.Contains(HubErrors.CasinoClosed, await RefusalAsync(() => ann.Act("check")));
        Assert.Contains(HubErrors.CasinoClosed, await RefusalAsync(() => cid.InvokeAsync<TableSummaryDto[]>(nameof(CasinoHub.TablesList))));
        Assert.Contains(HubErrors.CasinoClosed, await RefusalAsync(() => cid.Call(nameof(CasinoHub.TableCreate), BigBlind)));
        Assert.Contains(HubErrors.CasinoClosed, await RefusalAsync(() => cid.Call(nameof(CasinoHub.TableSit), 1, (int?)null)));
        Assert.Contains(HubErrors.CasinoClosed, await RefusalAsync(() => ann.Call(nameof(CasinoHub.TableAddAi))));
        Assert.Contains(HubErrors.CasinoClosed, await RefusalAsync(() => ann.Call(nameof(CasinoHub.TableRemoveAi))));

        var state = await bob.State();
        Assert.False(state.CasinoEnabled);
        Assert.Equal(1, state.Poker.Table!.Id);
        Assert.Equal("ok", (await cid.Call(nameof(CasinoHub.TableWatch), 1)).Status);
        await cid.Connection.InvokeAsync(nameof(CasinoHub.TableUnwatch));
        Assert.Equal("ok", (await ann.Call(nameof(CasinoHub.TableLeave))).Status);
        Assert.Null((await ann.State()).Poker.Table);
        Assert.Equal("ok", (await bob.Call(nameof(CasinoHub.TableLeave))).Status);
    }

    [Fact]
    public async Task While_the_casino_is_closed_no_new_hand_starts()
    {
        var ann = await ConnectAsync(Ann);
        var bob = await ConnectAsync(Bob);
        await ann.Call(nameof(CasinoHub.TableCreate), BigBlind);
        await bob.Call(nameof(CasinoHub.TableSit), 1, (int?)null);
        _factory.BotSettings.Casino.Enabled = false;

        await TickAsync(TimeSpan.FromSeconds(10));

        Assert.False((await ann.State()).Poker.Table!.HandRunning);
        Assert.Equal(1000, _factory.Database.Repo.Users[Ann].Tokens);

        _factory.BotSettings.Casino.Enabled = true;
        await TickAsync();
        Assert.True((await ann.State()).Poker.Table!.HandRunning);
    }

    [Fact]
    public async Task Table_calls_count_toward_the_rate_limit_except_watching_and_leaving()
    {
        var ann = await ConnectAsync(Ann);
        await ann.Call(nameof(CasinoHub.TableCreate), BigBlind);
        for (var i = 1; i < ActionRateLimiter.MaxActions; i++)
            await ann.InvokeAsync<TableSummaryDto[]>(nameof(CasinoHub.TablesList));

        Assert.Contains(HubErrors.SlowDown, await RefusalAsync(() => ann.InvokeAsync<TableSummaryDto[]>(nameof(CasinoHub.TablesList))));
        Assert.Contains(HubErrors.SlowDown, await RefusalAsync(() => ann.Act("check")));
        await ann.Connection.InvokeAsync(nameof(CasinoHub.TableUnwatch));
        Assert.Equal("ok", (await ann.Call(nameof(CasinoHub.TableLeave))).Status);
    }

    #endregion
}
