using CasinoWeb.Games;
using DiscordBot.Domain;
using DiscordBot.Services.Fun.Casino;
using DiscordBot.Services.Logging;
using DiscordBot.Settings;
using static CasinoWeb.Tests.RiggedBlackjack;

namespace CasinoWeb.Tests;

public sealed class WebBlackjackServiceTests
{
    private const ulong Player = 4242;
    private const string PlayerId = "4242";
    private const long Start = 1000;

    private readonly FakeCasinoDatabase _database = new();
    private readonly RecordingLoggingService _logging = new();
    private readonly FakeTimeProvider _clock = new(DateTimeOffset.UtcNow);
    private readonly Queue<Card[]> _decks = new();
    private readonly BotSettings _settings = new() { Casino = new CasinoSettings { StartingTokens = Start, GameTimeoutMinutes = 5 } };
    private readonly WebBlackjackService _blackjack;

    public WebBlackjackServiceTests()
    {
        var casino = new CasinoService(_database, _logging, _settings);
        _blackjack = new WebBlackjackService(casino, _settings, _logging, _clock, () => new RiggedBlackjack(_decks.Dequeue()));
    }

    private long Balance => _database.Repo.Users[PlayerId].Tokens;

    private List<TokenTransaction> GameTransactions => _database.Repo.Transactions.Where(t => t.Kind == TransactionKind.Game).ToList();

    private void SetBalance(long tokens) => _database.Repo.Users[PlayerId] = new CasinoUser { UserID = PlayerId, Tokens = tokens };

    /// <summary>Deals a hand from these cards: player, dealer, player, dealer, then the draws.</summary>
    private Task<BlackjackPlay> DealAsync(long bet, params int[] cards)
    {
        _decks.Enqueue(Cards(cards));
        return _blackjack.DealAsync(Player, bet);
    }

    #region Reserving bets (AC6)

    [Fact]
    public async Task A_deal_takes_the_bet_from_the_balance_before_the_hand_is_played()
    {
        var play = await DealAsync(100, 10, 10, 9, 7);

        Assert.Equal(BlackjackStatus.Ok, play.Status);
        Assert.Equal(Start - 100, Balance);
        Assert.Equal(Start - 100, play.Balance);
        Assert.False(play.Hand!.IsFinished);
        Assert.Equal(100, play.Hand.Bet);
        Assert.Empty(GameTransactions);
        Assert.Contains(_logging.Lines, l => l.Message.Contains($"reserved 100 tokens for user {Player}, hand {play.Hand.HandId}"));
    }

    [Fact]
    public async Task A_deal_above_the_balance_is_refused_and_changes_nothing()
    {
        SetBalance(50);

        var play = await DealAsync(100, 10, 10, 9, 7);

        Assert.Equal(BlackjackStatus.InsufficientTokens, play.Status);
        Assert.Equal(50, Balance);
        Assert.Null(await _blackjack.GetOpenHandAsync(Player, 50));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public async Task A_bet_below_one_token_is_refused(long bet)
    {
        var play = await _blackjack.DealAsync(Player, bet);

        Assert.Equal(BlackjackStatus.InvalidBet, play.Status);
        Assert.Empty(_database.Repo.Users);
    }

    [Fact]
    public async Task A_second_deal_while_a_hand_is_open_returns_that_hand_without_a_second_bet()
    {
        var first = await DealAsync(100, 10, 10, 9, 7);

        var second = await DealAsync(100, 10, 10, 9, 7);

        Assert.Equal(BlackjackStatus.HandInProgress, second.Status);
        Assert.Equal(first.Hand!.HandId, second.Hand!.HandId);
        Assert.Equal(Start - 100, Balance);
    }

    [Fact]
    public async Task A_double_down_takes_a_second_bet_and_doubles_the_stake()
    {
        // 5 + 6 = 11, doubled on a 10 for 21 against the dealer's 17.
        await DealAsync(100, 5, 10, 6, 7, 10);

        var play = await _blackjack.ActAsync(Player, BlackjackPlayerAction.DoubleDown);

        Assert.Equal(BlackjackStatus.Ok, play.Status);
        Assert.True(play.Hand!.IsFinished);
        Assert.Equal(200, play.Hand.Bet);
        Assert.Equal(GamePlayerResult.Won, play.Hand.Result);
        Assert.Equal(400, play.Hand.Credited);
        Assert.Equal(Start + 200, Balance);
        Assert.Equal(200, Assert.Single(GameTransactions).Amount);
    }

    [Fact]
    public async Task A_double_down_the_balance_cannot_cover_is_refused_and_the_hand_goes_on()
    {
        SetBalance(150);
        var deal = await DealAsync(100, 5, 10, 6, 7, 10);
        Assert.DoesNotContain(BlackjackPlayerAction.DoubleDown, deal.Hand!.Allowed);

        var play = await _blackjack.ActAsync(Player, BlackjackPlayerAction.DoubleDown);

        Assert.Equal(BlackjackStatus.InsufficientTokens, play.Status);
        Assert.Equal(50, Balance);
        Assert.False(play.Hand!.IsFinished);
        Assert.Equal(100, play.Hand.Bet);
        Assert.Equal(2, play.Hand.PlayerCards.Count);
    }

    [Fact]
    public async Task A_double_down_is_offered_when_the_balance_covers_it()
    {
        var deal = await DealAsync(100, 5, 10, 6, 7);

        Assert.Equal([BlackjackPlayerAction.Hit, BlackjackPlayerAction.Stand, BlackjackPlayerAction.DoubleDown], deal.Hand!.Allowed);
    }

    #endregion

    #region Settling hands (AC7)

    [Fact]
    public async Task A_win_credits_twice_the_bet_and_records_the_win()
    {
        await DealAsync(100, 10, 10, 9, 7);

        var play = await _blackjack.ActAsync(Player, BlackjackPlayerAction.Stand);

        Assert.Equal(GamePlayerResult.Won, play.Hand!.Result);
        Assert.Equal(200, play.Hand.Credited);
        Assert.Equal(100, play.Hand.Net);
        Assert.Equal(Start + 100, Balance);
        Assert.Equal(Start + 100, play.Balance);
        var transaction = Assert.Single(GameTransactions);
        Assert.Equal(100, transaction.Amount);
        Assert.Equal("Blackjack", transaction.Details!["game"]);
    }

    [Fact]
    public async Task A_lost_double_down_loses_both_bets()
    {
        // 5 + 6 doubled on a 2 is 13 against the dealer's 17.
        await DealAsync(100, 5, 10, 6, 7, 2);

        var play = await _blackjack.ActAsync(Player, BlackjackPlayerAction.DoubleDown);

        Assert.Equal(GamePlayerResult.Lost, play.Hand!.Result);
        Assert.Equal(0, play.Hand.Credited);
        Assert.Equal(Start - 200, Balance);
        Assert.Equal(-200, Assert.Single(GameTransactions).Amount);
    }

    [Fact]
    public async Task A_settlement_that_fails_ends_the_hand_and_logs_what_is_owed()
    {
        await DealAsync(100, 10, 10, 9, 7);
        _database.Repo.FailAdjust = true;

        var play = await _blackjack.ActAsync(Player, BlackjackPlayerAction.Stand);

        Assert.Equal(BlackjackStatus.Failed, play.Status);
        Assert.Null(await _blackjack.GetOpenHandAsync(Player, Balance));
        Assert.Empty(GameTransactions);
        Assert.Contains(_logging.Lines, l => l.Severity == ExtendedLogSeverity.Error && l.Message.Contains("owed 200 tokens"));
    }

    [Fact]
    public async Task A_tie_gives_the_bet_back_and_records_zero()
    {
        await DealAsync(100, 10, 10, 7, 7);

        var play = await _blackjack.ActAsync(Player, BlackjackPlayerAction.Stand);

        Assert.Equal(GamePlayerResult.Tie, play.Hand!.Result);
        Assert.Equal(100, play.Hand.Credited);
        Assert.Equal(Start, Balance);
        Assert.Equal(0, Assert.Single(GameTransactions).Amount);
    }

    [Fact]
    public async Task A_bust_loses_the_bet_and_records_the_loss()
    {
        await DealAsync(100, 10, 10, 6, 7, 10);

        var play = await _blackjack.ActAsync(Player, BlackjackPlayerAction.Hit);

        Assert.True(play.Hand!.IsFinished);
        Assert.Equal(GamePlayerResult.Lost, play.Hand.Result);
        Assert.Equal(0, play.Hand.Credited);
        Assert.Equal(26, play.Hand.PlayerValue);
        Assert.Equal(Start - 100, Balance);
        Assert.Equal(-100, Assert.Single(GameTransactions).Amount);
    }

    [Fact]
    public async Task A_blackjack_on_the_deal_settles_at_once()
    {
        var play = await DealAsync(100, 1, 10, 13, 7);

        Assert.True(play.Hand!.IsFinished);
        Assert.Equal(GamePlayerResult.Won, play.Hand.Result);
        Assert.Empty(play.Hand.Allowed);
        Assert.Equal(Start + 100, Balance);
        Assert.Null(await _blackjack.GetOpenHandAsync(Player, Balance));
    }

    [Fact]
    public async Task The_dealers_second_card_stays_hidden_until_the_hand_ends()
    {
        var deal = await DealAsync(100, 10, 10, 9, 7);
        Assert.Equal(Cards(10), deal.Hand!.DealerCards);
        Assert.Equal(10, deal.Hand.DealerValue);

        var end = await _blackjack.ActAsync(Player, BlackjackPlayerAction.Stand);

        Assert.Equal(Cards(10, 7), end.Hand!.DealerCards);
        Assert.Equal(17, end.Hand.DealerValue);
    }

    [Fact]
    public async Task The_dealer_draws_below_17()
    {
        // The dealer holds 10 + 5 and draws a 4 for 19, beating the player's 18.
        await DealAsync(100, 10, 10, 8, 5, 4);

        var play = await _blackjack.ActAsync(Player, BlackjackPlayerAction.Stand);

        Assert.Equal(Cards(10, 5, 4), play.Hand!.DealerCards);
        Assert.Equal(GamePlayerResult.Lost, play.Hand.Result);
        Assert.Equal(Start - 100, Balance);
    }

    [Fact]
    public async Task An_action_without_an_open_hand_is_refused()
    {
        var play = await _blackjack.ActAsync(Player, BlackjackPlayerAction.Hit);

        Assert.Equal(BlackjackStatus.NoHand, play.Status);
    }

    [Fact]
    public async Task A_finished_hand_takes_no_more_actions_and_a_new_deal_starts_a_new_hand()
    {
        var first = await DealAsync(100, 10, 10, 9, 7);
        await _blackjack.ActAsync(Player, BlackjackPlayerAction.Stand);

        Assert.Equal(BlackjackStatus.NoHand, (await _blackjack.ActAsync(Player, BlackjackPlayerAction.Hit)).Status);
        var next = await DealAsync(100, 10, 10, 9, 7);
        Assert.Equal(BlackjackStatus.Ok, next.Status);
        Assert.NotEqual(first.Hand!.HandId, next.Hand!.HandId);
    }

    #endregion

    #region Idle hands and shutdown (AC8)

    [Fact]
    public async Task A_hand_left_idle_for_the_timeout_stands_and_settles()
    {
        await DealAsync(100, 10, 10, 9, 7);
        _clock.Advance(TimeSpan.FromMinutes(5));

        await _blackjack.SweepIdleHandsAsync();

        Assert.Null(await _blackjack.GetOpenHandAsync(Player, Balance));
        Assert.Equal(Start + 100, Balance);
        Assert.Equal(100, Assert.Single(GameTransactions).Amount);
        Assert.Equal(BlackjackStatus.NoHand, (await _blackjack.ActAsync(Player, BlackjackPlayerAction.Hit)).Status);
    }

    [Fact]
    public async Task An_action_restarts_the_idle_timer()
    {
        await DealAsync(100, 10, 10, 2, 7, 2);
        _clock.Advance(TimeSpan.FromMinutes(4));
        await _blackjack.ActAsync(Player, BlackjackPlayerAction.Hit);
        _clock.Advance(TimeSpan.FromMinutes(4));

        await _blackjack.SweepIdleHandsAsync();

        Assert.NotNull(await _blackjack.GetOpenHandAsync(Player, Balance));
        Assert.Equal(Start - 100, Balance);
        Assert.Empty(GameTransactions);
    }

    [Fact]
    public async Task Stopping_gives_back_every_open_bet_and_refuses_new_deals()
    {
        await DealAsync(100, 10, 10, 9, 7);
        const ulong other = 99;
        _database.Repo.Users["99"] = new CasinoUser { UserID = "99", Tokens = 500 };
        _decks.Enqueue(Cards(10, 10, 9, 7));
        await _blackjack.DealAsync(other, 200);

        await _blackjack.ReleaseAllAsync();

        Assert.Equal(Start, Balance);
        Assert.Equal(500, _database.Repo.Users["99"].Tokens);
        Assert.Empty(GameTransactions);
        Assert.Null(await _blackjack.GetOpenHandAsync(Player, Balance));
        Assert.Equal(BlackjackStatus.Failed, (await DealAsync(100, 10, 10, 9, 7)).Status);
        Assert.Equal(Start, Balance);
    }

    [Fact]
    public async Task The_background_service_gives_the_bets_back_when_the_server_stops()
    {
        await DealAsync(100, 10, 10, 9, 7);
        var sweeper = new BlackjackSweeper(_blackjack, Microsoft.Extensions.Logging.Abstractions.NullLogger<BlackjackSweeper>.Instance);
        await sweeper.StartAsync(CancellationToken.None);

        await sweeper.StopAsync(CancellationToken.None);

        Assert.Equal(Start, Balance);
        Assert.Null(await _blackjack.GetOpenHandAsync(Player, Balance));
    }

    #endregion
}
