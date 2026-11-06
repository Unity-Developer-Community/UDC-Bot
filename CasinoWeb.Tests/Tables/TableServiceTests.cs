using CasinoWeb.Configuration;
using CasinoWeb.Games.Poker;
using CasinoWeb.Tables;
using CasinoWeb.Tests.Poker;
using DiscordBot.Domain;
using DiscordBot.Services.Fun.Casino;
using DiscordBot.Settings;
using Microsoft.Extensions.Options;

namespace CasinoWeb.Tests.Tables;

/// <summary>
/// The table system over a fake balance and a fake clock. Ann sits at seat 0 and Bob at seat 1 of a 5/10 table; the first
/// hand's button is seat 0, so heads-up Ann posts the small blind (5), Bob the big blind (10) and Ann acts first.
/// </summary>
public sealed class TableServiceTests
{
    private const ulong Ann = 1;
    private const ulong Bob = 2;
    private const ulong Cid = 3;
    private const long Start = 1000;
    private const long BigBlind = 10;

    private readonly FakeCasinoDatabase _database = new();
    private readonly RecordingLoggingService _logging = new();
    private readonly FakeTimeProvider _clock = new(DateTimeOffset.UtcNow);
    private readonly BotSettings _settings = new() { Casino = new CasinoSettings { Enabled = true, StartingTokens = Start } };
    private readonly CasinoHostOptions _host = new();
    private readonly Queue<List<Card>> _decks = new();
    private readonly RecordingNotifier _notifier = new();
    private readonly TableService _tables;

    public TableServiceTests()
    {
        var ledger = new TableLedger(new CasinoService(_database, _logging, _settings), _logging);
        _tables = new TableService(
            ledger, _settings, Options.Create(_host), _clock, _logging, new PresenceTracker(), _notifier,
            stakes => new PokerTableGame(stakes, () => _decks.Count > 0 ? _decks.Dequeue() : HoldemDeck.Shuffled()));
    }

    private long BalanceOf(ulong user) => _database.Repo.Users[user.ToString()].Tokens;

    private void SetBalance(ulong user, long tokens) => _database.Repo.Users[user.ToString()] = new CasinoUser { UserID = user.ToString(), Tokens = tokens };

    private List<TokenTransaction> GameTransactions => _database.Repo.Transactions.Where(t => t.Kind == TransactionKind.Game).ToList();

    /// <summary>Queues the next hand's cards for seats 0 and 1: Ann has aces, Bob kings, and the board helps nobody.</summary>
    private void Rig(int button = 0, string board = "2h 7s 9h Js 3d") => _decks.Enqueue(HoldemTestKit.Deck(button, 2, null, board));

    private async Task<TableView> ViewOf(ulong user) => (await _tables.GetSeatedViewAsync(user))!;

    private async Task<TableView> OpenTableAsync(ulong creator = Ann, long bigBlind = BigBlind)
    {
        var result = await _tables.CreateAsync(creator, $"User {creator}", bigBlind);
        Assert.Equal(TableStatus.Ok, result.Status);
        return result.View!;
    }

    private async Task SitAsync(ulong user, int tableId = 1)
    {
        var result = await _tables.SitAsync(user, $"User {user}", tableId, null);
        Assert.Equal(TableStatus.Ok, result.Status);
    }

    /// <summary>Ann and Bob at one table with the first hand dealt and the blinds in.</summary>
    private async Task StartHeadsUpHandAsync()
    {
        Rig();
        await OpenTableAsync();
        await SitAsync(Bob);
        await _tables.TickAsync();
        Assert.True((await ViewOf(Ann)).HandRunning);
    }

    private async Task TickAfterAsync(TimeSpan by)
    {
        _clock.Advance(by);
        await _tables.TickAsync();
    }

    private ulong UserAt(int seat) => seat == 0 ? Ann : Bob;

    private async Task<TableResult> CheckOrCallAsync(ulong user)
    {
        var result = await _tables.ActAsync(user, "check");
        return result.Ok ? result : await _tables.ActAsync(user, "call");
    }

    private async Task CheckDownAsync()
    {
        while ((await ViewOf(Ann)).HandRunning)
        {
            var turn = (await ViewOf(Ann)).TurnSeat!.Value;
            Assert.True((await CheckOrCallAsync(UserAt(turn))).Ok);
        }
    }

    private static PokerHandView Hand(TableView view) => (PokerHandView)view.Hand!;

    #region Opening and filling tables (AC2)

    [Fact]
    public async Task Creating_a_table_seats_the_creator_lists_it_and_the_last_player_leaving_closes_it()
    {
        var view = await OpenTableAsync();

        Assert.Equal(0, view.MySeat);
        Assert.Equal(new TableStakes(5, 10), view.Stakes);
        var row = Assert.Single(_tables.List());
        Assert.Equal((1, 1, TableRules.Seats, false), (row.Id, row.SeatsTaken, row.MaxSeats, row.HandRunning));
        Assert.Equal(row, Assert.Single(_notifier.Lists.Last()));

        await SitAsync(Bob);
        Assert.Equal(2, _tables.List().Single().SeatsTaken);
        Assert.Equal(2, Assert.Single(_notifier.Lists.Last()).SeatsTaken);

        await _tables.LeaveAsync(Ann);
        Assert.Single(_tables.List());
        Assert.Null(await _tables.GetSeatedViewAsync(Ann));

        await _tables.LeaveAsync(Bob);
        Assert.Empty(_tables.List());
        Assert.Empty(_notifier.Lists.Last());
        Assert.Null(await _tables.GetSeatedViewAsync(Bob));
    }

    [Fact]
    public async Task A_member_sits_at_one_table_at_a_time()
    {
        await OpenTableAsync();
        await OpenTableAsync(Bob);

        Assert.Equal(TableStatus.AlreadySeated, (await _tables.CreateAsync(Ann, "Ann", BigBlind)).Status);
        Assert.Equal(TableStatus.AlreadySeated, (await _tables.SitAsync(Ann, "Ann", 2, null)).Status);
        Assert.Equal(TableStatus.AlreadySeated, (await _tables.SitAsync(Bob, "Bob", 1, null)).Status);
        Assert.Equal(2, _tables.List().Count);

        await _tables.LeaveAsync(Ann);
        Assert.Equal(TableStatus.Ok, (await _tables.SitAsync(Ann, "Ann", 2, null)).Status);
    }

    [Fact]
    public async Task A_full_table_a_taken_seat_and_an_unknown_table_are_refused()
    {
        await OpenTableAsync();
        Assert.Equal(TableStatus.SeatTaken, (await _tables.SitAsync(Bob, "Bob", 1, 0)).Status);
        Assert.Equal(TableStatus.NoSuchSeat, (await _tables.SitAsync(Bob, "Bob", 1, 6)).Status);
        Assert.Equal(TableStatus.NoSuchTable, (await _tables.SitAsync(Bob, "Bob", 99, null)).Status);

        for (ulong user = 2; user <= 6; user++)
        {
            await SitAsync(user);
        }

        Assert.Equal(TableStatus.TableFull, (await _tables.SitAsync(7, "User 7", 1, null)).Status);
        Assert.Equal(TableRules.Seats, _tables.List().Single().SeatsTaken);

        // The refused member holds no seat: they can still take a seat elsewhere.
        Assert.Equal(TableStatus.Ok, (await _tables.CreateAsync(7, "User 7", BigBlind)).Status);
    }

    [Fact]
    public async Task A_balance_under_ten_big_blinds_cannot_create_or_sit()
    {
        SetBalance(Ann, 99);
        Assert.Equal(TableStatus.BelowMinimum, (await _tables.CreateAsync(Ann, "Ann", BigBlind)).Status);
        Assert.Empty(_tables.List());

        SetBalance(Ann, 100);
        var tableId = (await OpenTableAsync()).Id;
        SetBalance(Bob, 99);
        Assert.Equal(TableStatus.BelowMinimum, (await _tables.SitAsync(Bob, "Bob", tableId, null)).Status);
        Assert.Null(await _tables.GetSeatedViewAsync(Bob));
    }

    [Fact]
    public async Task Only_the_stakes_presets_open_a_table_and_a_21st_table_is_refused()
    {
        Assert.Equal(TableStatus.BadStakes, (await _tables.CreateAsync(Ann, "Ann", 7)).Status);

        for (ulong user = 1; user <= TableRules.MaxTables; user++)
        {
            Assert.Equal(TableStatus.Ok, (await _tables.CreateAsync(user, $"User {user}", 2)).Status);
        }

        Assert.Equal(TableStatus.TooManyTables, (await _tables.CreateAsync(99, "User 99", 2)).Status);
        Assert.Equal(TableRules.MaxTables, _tables.List().Count);
        Assert.Null(await _tables.GetSeatedViewAsync(99));

        await _tables.LeaveAsync(1);
        Assert.Equal(TableStatus.Ok, (await _tables.CreateAsync(99, "User 99", 2)).Status);
    }

    #endregion

    #region Starting hands (AC3)

    [Fact]
    public async Task A_hand_needs_two_players_who_can_cover_ten_big_blinds()
    {
        Rig();
        await OpenTableAsync();
        await _tables.TickAsync();
        Assert.False((await ViewOf(Ann)).HandRunning);

        await SitAsync(Bob);
        SetBalance(Bob, 99);
        await _tables.TickAsync();
        Assert.False((await ViewOf(Ann)).HandRunning);

        SetBalance(Bob, 100);
        await _tables.TickAsync();
        Assert.True((await ViewOf(Ann)).HandRunning);
        Assert.True(_tables.List().Single().HandRunning);
    }

    [Fact]
    public async Task The_blinds_are_taken_at_once_and_the_button_moves_after_a_pause_to_read_the_result()
    {
        await StartHeadsUpHandAsync();

        var first = Hand(await ViewOf(Ann));
        Assert.Equal((0, 0, 1), (first.ButtonSeat, first.SmallBlindSeat, first.BigBlindSeat));
        Assert.Equal((Start - 5, Start - 10), (BalanceOf(Ann), BalanceOf(Bob)));
        Assert.Equal(0, (await ViewOf(Ann)).TurnSeat);

        await _tables.ActAsync(Ann, "fold");
        var over = await ViewOf(Ann);
        Assert.False(over.HandRunning);
        Assert.Equal(5, over.NextHandSecondsLeft);

        Rig(button: 1);
        await TickAfterAsync(TimeSpan.FromSeconds(4));
        Assert.False((await ViewOf(Ann)).HandRunning);

        await TickAfterAsync(TimeSpan.FromSeconds(1));
        var second = Hand(await ViewOf(Ann));
        Assert.True((await ViewOf(Ann)).HandRunning);
        Assert.Equal((1, 1, 0), (second.ButtonSeat, second.SmallBlindSeat, second.BigBlindSeat));
        // Bob won 5 in the first hand, then posted the small blind; Ann posted the big blind.
        Assert.Equal((Start - 5 - 10, Start + 5 - 5), (BalanceOf(Ann), BalanceOf(Bob)));
    }

    [Fact]
    public async Task No_hand_is_dealt_while_the_casino_is_closed()
    {
        Rig();
        await OpenTableAsync();
        await SitAsync(Bob);
        _settings.Casino.Enabled = false;
        await _tables.TickAsync();
        Assert.False((await ViewOf(Ann)).HandRunning);
        Assert.Equal(Start, BalanceOf(Ann));

        _settings.Casino.Enabled = true;
        await _tables.TickAsync();
        Assert.True((await ViewOf(Ann)).HandRunning);
    }

    #endregion

    #region Money (AC5, AC7)

    [Fact]
    public async Task A_balance_spent_elsewhere_refuses_the_action_lowers_the_stake_and_offers_the_new_options()
    {
        await StartHeadsUpHandAsync();
        SetBalance(Ann, 20);

        var refused = await _tables.ActAsync(Ann, "raise", 100);

        Assert.Equal((TableStatus.Refused, TableRefusal.InsufficientBalance), (refused.Status, refused.Refusal));
        Assert.Equal(20, BalanceOf(Ann));
        var options = Hand(refused.View!).Options!;
        Assert.Equal(25, options.MaxRaiseTo);
        Assert.Equal(20, options.MinRaiseTo);
        Assert.Contains(HoldemActionKind.AllIn, options.Kinds);

        var allIn = await _tables.ActAsync(Ann, "allIn");
        Assert.True(allIn.Ok);
        Assert.Equal(0, BalanceOf(Ann));
        Assert.Contains(_logging.Lines, l => l.Message.Contains("took 20 tokens from user 1, table 1, hand 1"));
    }

    [Fact]
    public async Task An_action_the_rules_refuse_costs_nothing()
    {
        await StartHeadsUpHandAsync();

        Assert.Equal(TableRefusal.NotYourTurn, (await _tables.ActAsync(Bob, "check")).Refusal);
        Assert.Equal(TableRefusal.NotAllowed, (await _tables.ActAsync(Ann, "check")).Refusal);
        Assert.Equal(TableRefusal.NotAllowed, (await _tables.ActAsync(Ann, "dance")).Refusal);
        Assert.Equal(TableRefusal.AmountTooLow, (await _tables.ActAsync(Ann, "raise", 15)).Refusal);
        Assert.Equal(TableRefusal.AmountTooHigh, (await _tables.ActAsync(Ann, "raise", 5000)).Refusal);
        Assert.Equal(Start - 5, BalanceOf(Ann));
        Assert.Equal(TableStatus.NotSeated, (await _tables.ActAsync(Cid, "fold")).Status);
    }

    [Fact]
    public async Task Showdown_credits_the_winner_and_records_each_players_net_result_as_texas_holdem()
    {
        await StartHeadsUpHandAsync();

        await _tables.ActAsync(Ann, "call");
        await CheckDownAsync();

        Assert.Equal((Start + 10, Start - 10), (BalanceOf(Ann), BalanceOf(Bob)));
        var transactions = GameTransactions;
        Assert.Equal(2, transactions.Count);
        Assert.All(transactions, t => Assert.Equal("Texas Hold'em", t.Details!["game"]));
        Assert.Equal(10, transactions.Single(t => t.UserID == "1").Amount);
        Assert.Equal(-10, transactions.Single(t => t.UserID == "2").Amount);
        Assert.Contains(_logging.Lines, l => l.Message.Contains("settled table 1, hand 1 of user 1: staked 10, credited 20, result 10"));

        var result = Hand(await ViewOf(Bob)).Result!;
        Assert.True(result.Showdown);
    }

    [Fact]
    public async Task When_everyone_else_folds_the_last_player_wins_and_both_results_are_recorded()
    {
        await StartHeadsUpHandAsync();

        await _tables.ActAsync(Ann, "fold");

        Assert.Equal((Start - 5, Start + 5), (BalanceOf(Ann), BalanceOf(Bob)));
        Assert.Equal(-5, GameTransactions.Single(t => t.UserID == "1").Amount);
        Assert.Equal(5, GameTransactions.Single(t => t.UserID == "2").Amount);
    }

    [Fact]
    public async Task Leaving_mid_hand_folds_the_hand_and_the_chips_stay_in_the_pot()
    {
        await StartHeadsUpHandAsync();

        var left = await _tables.LeaveAsync(Ann);

        Assert.True(left.Ok);
        Assert.Equal((Start - 5, Start + 5), (BalanceOf(Ann), BalanceOf(Bob)));
        Assert.Equal(-5, GameTransactions.Single(t => t.UserID == "1").Amount);
        Assert.Null(await _tables.GetSeatedViewAsync(Ann));
        // The member is free to sit elsewhere at once.
        Assert.Equal(TableStatus.Ok, (await _tables.CreateAsync(Ann, "Ann", BigBlind)).Status);
    }

    [Fact]
    public async Task A_hand_with_no_human_left_is_played_down_by_the_ai_seats_and_the_table_closes()
    {
        _host.AllowAiSeats = true;
        Rig();
        await OpenTableAsync();
        await _tables.AddAiAsync(Ann);
        await _tables.TickAsync();
        Assert.True((await ViewOf(Ann)).HandRunning);

        await _tables.LeaveAsync(Ann);

        Assert.Empty(_tables.List());
        Assert.Equal(Start - 5, BalanceOf(Ann));
        Assert.Equal(-5, GameTransactions.Single().Amount);
    }

    [Fact]
    public async Task When_the_last_human_leaves_a_running_hand_the_ai_seats_play_it_down_and_the_leaver_is_charged()
    {
        _host.AllowAiSeats = true;
        _decks.Enqueue(HoldemTestKit.Deck(0, 3, null, "2h 7s 9h Js 3d"));
        await OpenTableAsync();
        await _tables.AddAiAsync(Ann);
        await _tables.AddAiAsync(Ann);
        await _tables.TickAsync();
        Assert.Equal(0, (await ViewOf(Ann)).TurnSeat);
        Assert.True((await _tables.ActAsync(Ann, "call")).Ok);

        await _tables.LeaveAsync(Ann);

        Assert.Empty(_tables.List());
        Assert.Equal(Start - 10, BalanceOf(Ann));
        Assert.Equal(-10, GameTransactions.Single().Amount);
    }

    #endregion

    #region Hidden cards (AC8)

    [Fact]
    public async Task Each_viewer_gets_only_their_own_hole_cards_until_showdown()
    {
        await StartHeadsUpHandAsync();
        var watcher = await _tables.WatchAsync(Cid, 1);
        Assert.True(watcher.Ok);

        static IReadOnlyList<Card>? CardsAt(TableView view, int seat) => Hand(view).Seats.Single(s => s.Seat == seat).HoleCards;

        var ann = await ViewOf(Ann);
        var bob = await ViewOf(Bob);
        Assert.NotNull(CardsAt(ann, 0));
        Assert.Null(CardsAt(ann, 1));
        Assert.Null(CardsAt(bob, 0));
        Assert.NotNull(CardsAt(bob, 1));
        Assert.Null(CardsAt(watcher.View!, 0));
        Assert.Null(CardsAt(watcher.View!, 1));
        Assert.Null(Hand(bob).Options);
        Assert.NotNull(Hand(ann).Options);

        await _tables.ActAsync(Ann, "call");
        await CheckDownAsync();

        var after = _notifier.Updates.Last().Views;
        foreach (var user in new[] { Ann, Bob, Cid })
        {
            Assert.NotNull(CardsAt(after[user]!, 0));
            Assert.NotNull(CardsAt(after[user]!, 1));
        }
    }

    [Fact]
    public async Task A_player_who_folds_never_shows_their_cards()
    {
        await StartHeadsUpHandAsync();
        await _tables.ActAsync(Ann, "fold");

        var bob = await ViewOf(Bob);
        Assert.Null(Hand(bob).Seats.Single(s => s.Seat == 0).HoleCards);
        Assert.False(Hand(bob).Result!.Showdown);
        Assert.Empty(Hand(bob).Result!.Shown);
    }

    #endregion

    #region Turn timer (AC9)

    [Fact]
    public async Task The_view_tells_whose_turn_it_is_and_the_seconds_left()
    {
        await StartHeadsUpHandAsync();

        var view = await ViewOf(Bob);
        Assert.Equal((0, 30), (view.TurnSeat, view.TurnSecondsLeft));

        await TickAfterAsync(TimeSpan.FromSeconds(12));
        Assert.Equal(18, (await ViewOf(Bob)).TurnSecondsLeft);
    }

    [Fact]
    public async Task A_player_who_lets_30_seconds_pass_folds_when_they_cannot_check()
    {
        await StartHeadsUpHandAsync();

        await TickAfterAsync(TimeSpan.FromSeconds(29));
        Assert.True((await ViewOf(Ann)).HandRunning);

        await TickAfterAsync(TimeSpan.FromSeconds(1));
        Assert.False((await ViewOf(Ann)).HandRunning);
        Assert.Equal((Start - 5, Start + 5), (BalanceOf(Ann), BalanceOf(Bob)));
    }

    [Fact]
    public async Task A_player_who_lets_the_turn_pass_checks_when_they_can_and_two_misses_in_a_row_stand_them_up_after_the_hand()
    {
        await StartHeadsUpHandAsync();
        await TickAfterAsync(TimeSpan.FromSeconds(31));
        Assert.False((await ViewOf(Ann)).HandRunning);

        // Second hand: Bob posts the small blind and calls; Ann holds the big blind and can check.
        Rig(button: 1);
        await TickAfterAsync(TimeSpan.FromSeconds(5));
        Assert.True((await _tables.ActAsync(Bob, "call")).Ok);
        Assert.Equal(0, (await ViewOf(Ann)).TurnSeat);
        await TickAfterAsync(TimeSpan.FromSeconds(31));
        Assert.True((await ViewOf(Ann)).HandRunning);
        Assert.Equal(HoldemStreet.Flop, Hand(await ViewOf(Ann)).Street);

        // The second miss is made: Ann stays until the hand ends, then stands up.
        while ((await ViewOf(Bob)).HandRunning)
        {
            var turn = (await ViewOf(Bob)).TurnSeat!.Value;
            if (turn == 1)
            {
                Assert.True((await CheckOrCallAsync(Bob)).Ok);
            }
            else
            {
                await TickAfterAsync(TimeSpan.FromSeconds(31));
            }
        }

        Assert.Null(await _tables.GetSeatedViewAsync(Ann));
        Assert.Equal([1], (await ViewOf(Bob)).Seats.Select(s => s.Seat));
    }

    [Fact]
    public async Task An_action_resets_the_missed_turns()
    {
        await StartHeadsUpHandAsync();
        await TickAfterAsync(TimeSpan.FromSeconds(31));

        Rig(button: 1);
        await TickAfterAsync(TimeSpan.FromSeconds(5));
        await _tables.ActAsync(Bob, "call");
        Assert.True((await _tables.ActAsync(Ann, "check")).Ok);
        await CheckDownAsync();

        Assert.NotNull(await _tables.GetSeatedViewAsync(Ann));
    }

    #endregion

    #region Disconnects (AC10)

    [Fact]
    public async Task A_dropped_seat_is_kept_for_60_seconds_and_a_reconnect_brings_it_back_with_the_own_cards()
    {
        await StartHeadsUpHandAsync();
        await _tables.UserConnectedAsync(Ann);
        await _tables.UserDisconnectedAsync(Ann);
        Assert.True((await ViewOf(Bob)).Seats.Single(s => s.UserId == Ann).Away);

        await TickAfterAsync(TimeSpan.FromSeconds(20));
        Assert.True((await ViewOf(Bob)).HandRunning);

        await _tables.UserConnectedAsync(Ann);
        var back = await ViewOf(Ann);
        Assert.False(back.Seats.Single(s => s.UserId == Ann).Away);
        Assert.NotNull(Hand(back).Seats.Single(s => s.Seat == 0).HoleCards);
        Assert.Equal(0, back.TurnSeat);
        Assert.Equal(10, back.TurnSecondsLeft);

        await TickAfterAsync(TimeSpan.FromSeconds(50));
        Assert.NotNull(await _tables.GetSeatedViewAsync(Ann));
    }

    [Fact]
    public async Task A_second_connection_keeps_the_seat_present_until_the_last_one_drops()
    {
        await StartHeadsUpHandAsync();
        await _tables.UserConnectedAsync(Ann);
        await _tables.UserConnectedAsync(Ann);

        await _tables.UserDisconnectedAsync(Ann);
        Assert.False((await ViewOf(Bob)).Seats.Single(s => s.UserId == Ann).Away);

        await _tables.UserDisconnectedAsync(Ann);
        Assert.True((await ViewOf(Bob)).Seats.Single(s => s.UserId == Ann).Away);
    }

    [Fact]
    public async Task A_seat_still_away_after_60_seconds_folds_and_leaves()
    {
        await StartHeadsUpHandAsync();
        await _tables.UserConnectedAsync(Ann);
        await _tables.UserDisconnectedAsync(Ann);

        await TickAfterAsync(TimeSpan.FromSeconds(60));

        Assert.Null(await _tables.GetSeatedViewAsync(Ann));
        Assert.False((await ViewOf(Bob)).HandRunning);
        Assert.Equal((Start - 5, Start + 5), (BalanceOf(Ann), BalanceOf(Bob)));
        Assert.Equal(-5, GameTransactions.Single(t => t.UserID == "1").Amount);
    }

    [Fact]
    public async Task An_away_player_is_not_dealt_into_the_next_hand()
    {
        Rig();
        await OpenTableAsync();
        await SitAsync(Bob);
        await _tables.UserConnectedAsync(Bob);
        await _tables.UserDisconnectedAsync(Bob);

        await _tables.TickAsync();
        Assert.False((await ViewOf(Ann)).HandRunning);

        await _tables.UserConnectedAsync(Bob);
        await _tables.TickAsync();
        Assert.True((await ViewOf(Ann)).HandRunning);
    }

    [Fact]
    public async Task A_table_whose_only_player_stays_away_closes_after_the_grace()
    {
        await OpenTableAsync();
        await _tables.UserConnectedAsync(Ann);
        await _tables.UserDisconnectedAsync(Ann);
        Assert.Single(_tables.List());

        await TickAfterAsync(TimeSpan.FromSeconds(59));
        Assert.Single(_tables.List());

        await TickAfterAsync(TimeSpan.FromSeconds(1));
        Assert.Empty(_tables.List());
        Assert.Null(await _tables.GetSeatedViewAsync(Ann));
    }

    [Fact]
    public async Task A_watcher_is_dropped_when_their_connections_close()
    {
        await OpenTableAsync();
        await _tables.UserConnectedAsync(Cid);
        await _tables.WatchAsync(Cid, 1);
        await SitAsync(Bob);
        Assert.True(_notifier.Updates.Last().Views.ContainsKey(Cid));

        await _tables.UserDisconnectedAsync(Cid);
        await _tables.LeaveAsync(Bob);

        Assert.False(_notifier.Updates.Last().Views.ContainsKey(Cid));
    }

    #endregion

    #region Stopping (AC11)

    [Fact]
    public async Task A_graceful_stop_gives_back_every_token_of_an_unfinished_hand_and_closes_the_tables()
    {
        await StartHeadsUpHandAsync();
        Assert.Equal((Start - 5, Start - 10), (BalanceOf(Ann), BalanceOf(Bob)));

        await _tables.ReleaseAllAsync();

        Assert.Equal((Start, Start), (BalanceOf(Ann), BalanceOf(Bob)));
        Assert.Empty(_tables.List());
        Assert.Empty(GameTransactions);
        Assert.Null(await _tables.GetSeatedViewAsync(Ann));
        Assert.Contains(_logging.Lines, l => l.Message.Contains("took 5 tokens from user 1, table 1, hand 1"));
        Assert.Contains(_logging.Lines, l => l.Message.Contains("gave back 5 tokens to user 1, table 1, hand 1"));
        Assert.Contains(_logging.Lines, l => l.Message.Contains("gave back 10 tokens to user 2, table 1, hand 1"));
        Assert.Null(_notifier.Updates.Last().Views[Ann]);

        Assert.Equal(TableStatus.Closed, (await _tables.CreateAsync(Cid, "Cid", BigBlind)).Status);
    }

    [Fact]
    public async Task A_stop_between_hands_gives_back_nothing_twice()
    {
        await StartHeadsUpHandAsync();
        await _tables.ActAsync(Ann, "fold");
        var after = (BalanceOf(Ann), BalanceOf(Bob));

        await _tables.ReleaseAllAsync();

        Assert.Equal(after, (BalanceOf(Ann), BalanceOf(Bob)));
        Assert.DoesNotContain(_logging.Lines, l => l.Message.Contains("gave back"));
    }

    #endregion

    #region AI seats (AC12)

    [Fact]
    public async Task A_seated_player_adds_and_removes_ai_seats_that_never_touch_a_balance()
    {
        _host.AllowAiSeats = true;
        Rig();
        await OpenTableAsync();

        var added = await _tables.AddAiAsync(Ann);
        Assert.True(added.Ok);
        var ai = added.View!.Seats.Single(s => s.IsAi);
        Assert.Equal(("AI 1", 1), (ai.Name, ai.Seat));
        Assert.Null(ai.UserId);

        await _tables.TickAsync();
        while ((await ViewOf(Ann)).HandRunning)
        {
            var view = await ViewOf(Ann);
            if (view.TurnSeat == 0)
            {
                Assert.True((await CheckOrCallAsync(Ann)).Ok);
            }
            else
            {
                await _tables.TickAsync();
            }
        }

        Assert.Equal(Start + 10, BalanceOf(Ann));
        Assert.Equal(new[] { "1" }, _database.Repo.Users.Keys);
        Assert.Equal("1", GameTransactions.Single().UserID);

        var removed = await _tables.RemoveAiAsync(Ann);
        Assert.True(removed.Ok);
        Assert.DoesNotContain(removed.View!.Seats, s => s.IsAi);
        Assert.Equal(TableStatus.NoAiSeat, (await _tables.RemoveAiAsync(Ann)).Status);
    }

    [Fact]
    public async Task An_ai_seat_moves_one_action_per_tick_by_checking_or_calling()
    {
        _host.AllowAiSeats = true;
        Rig();
        await OpenTableAsync();
        await _tables.AddAiAsync(Ann);
        await _tables.TickAsync();

        // Ann (small blind) raises: the AI to act answers on the next tick by calling, never raising or folding.
        Assert.True((await _tables.ActAsync(Ann, "raise", 30)).Ok);
        Assert.Equal(1, (await ViewOf(Ann)).TurnSeat);
        await _tables.TickAsync();

        var hand = Hand(await ViewOf(Ann));
        Assert.Equal(HoldemStreet.Flop, hand.Street);
        Assert.False(hand.Seats.Single(s => s.Seat == 1).Folded);
        Assert.Equal(60, hand.Pot);
    }

    [Fact]
    public async Task Ai_seats_are_refused_when_the_setting_is_off()
    {
        await OpenTableAsync();

        Assert.Equal(TableStatus.AiNotAllowed, (await _tables.AddAiAsync(Ann)).Status);
        Assert.Equal(TableStatus.AiNotAllowed, (await _tables.RemoveAiAsync(Ann)).Status);
        Assert.Single((await ViewOf(Ann)).Seats);
    }

    [Fact]
    public async Task Only_a_seated_player_can_add_an_ai_seat()
    {
        _host.AllowAiSeats = true;
        await OpenTableAsync();

        Assert.Equal(TableStatus.NotSeated, (await _tables.AddAiAsync(Bob)).Status);
    }

    #endregion

    #region Watching

    [Fact]
    public async Task Watching_shows_a_table_without_taking_a_seat_and_a_seated_member_can_only_follow_their_own()
    {
        await OpenTableAsync();
        await OpenTableAsync(Bob);

        var watch = await _tables.WatchAsync(Cid, 1);
        Assert.True(watch.Ok);
        Assert.Null(watch.View!.MySeat);
        Assert.Null(await _tables.GetSeatedViewAsync(Cid));

        Assert.Equal(TableStatus.AlreadySeated, (await _tables.WatchAsync(Ann, 2)).Status);
        Assert.Equal(TableStatus.NoSuchTable, (await _tables.WatchAsync(Cid, 99)).Status);

        await _tables.WatchAsync(Cid, 2);
        await _tables.LeaveAsync(Ann);
        await _tables.LeaveAsync(Bob);
        var last = _notifier.Updates.Last();
        Assert.Equal(2, last.TableId);
        Assert.Null(last.Views[Bob]);
        Assert.True(last.Views.ContainsKey(Cid));
        Assert.Null(last.Views[Cid]);
    }

    #endregion

    private sealed class RecordingNotifier : ITableNotifier
    {
        public List<IReadOnlyList<TableSummary>> Lists { get; } = [];
        public List<(int TableId, IReadOnlyDictionary<ulong, TableView?> Views)> Updates { get; } = [];

        public Task TablesChangedAsync(IReadOnlyList<TableSummary> tables)
        {
            Lists.Add(tables);
            return Task.CompletedTask;
        }

        public Task TableUpdatedAsync(int tableId, IReadOnlyDictionary<ulong, TableView?> views)
        {
            Updates.Add((tableId, views));
            return Task.CompletedTask;
        }
    }
}
