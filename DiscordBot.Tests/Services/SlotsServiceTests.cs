using Discord;
using DiscordBot.Domain;
using DiscordBot.Services.Fun.Casino;
using DiscordBot.Services.Logging;
using DiscordBot.Settings;

namespace DiscordBot.Tests.Services;

public class SlotsServiceTests
{
    private const ulong UserId = 42;
    private static readonly long Floor = SlotSettlementMath.JackpotFloorHundredths;

    #region Test doubles

    private sealed class FakeLedger : ISlotsLedger
    {
        public long Tokens { get; set; } = 1_000;
        public long Pot { get; set; } = Floor;
        public List<(long Delta, Dictionary<string, string> Details)> Credits { get; } = [];
        public bool FailCredit { get; set; }
        public bool ClaimReturnsNothing { get; set; }
        public Func<Task>? BeforeGetTokens { get; set; }

        public async Task<long> GetTokens(string userId)
        {
            if (BeforeGetTokens is not null) await BeforeGetTokens();
            return Tokens;
        }

        public Task Credit(string userId, long delta, Dictionary<string, string> details)
        {
            if (FailCredit) throw new InvalidOperationException("database down");
            Tokens += delta;
            Credits.Add((delta, details));
            return Task.CompletedTask;
        }

        public Task<long> GetPot() => Task.FromResult(Pot);

        public Task<long> AddContribution(long hundredths) => Task.FromResult(Pot += hundredths);

        public Task<long> ClaimShare(int bet)
        {
            if (ClaimReturnsNothing) return Task.FromResult(0L);
            var share = SlotSettlementMath.JackpotShare(Pot, bet);
            Pot = SlotSettlementMath.PotAfterClaim(Pot, share);
            return Task.FromResult(share);
        }
    }

    /// <summary>Hands out the scripted values in order, so a test chooses the stops and the hold roll.</summary>
    private sealed class ScriptedRandom : ISlotRandom
    {
        private readonly Queue<int> _values = new();

        public void Enqueue(params int[] values)
        {
            foreach (var value in values) _values.Enqueue(value);
        }

        public int Next(int maxExclusive)
        {
            var value = _values.Dequeue();
            Assert.InRange(value, 0, maxExclusive - 1);
            return value;
        }
    }

    private sealed class StubRenderer(bool fail = false) : SlotsRenderer(new BotSettings { AssetsRootPath = "unused" })
    {
        public override Task<MemoryStream> RenderSpinAsync(SlotResult result) =>
            fail ? throw new InvalidOperationException("no art") : Task.FromResult(new MemoryStream([1, 2, 3]));
    }

    private sealed class FakeTime : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 11, 4, 12, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class FakeLogging : ILoggingService
    {
        public List<string> Messages { get; } = [];
        public void LogXp(string channel, string user, float baseXp, float bonusXp, float xpReduce, int totalXp) { }

        public Task Log(LogBehaviour behaviour, string message, ExtendedLogSeverity severity = ExtendedLogSeverity.Info, Embed? embed = null)
        {
            Messages.Add(message);
            return Task.CompletedTask;
        }
    }

    private sealed class Fixture
    {
        public FakeLedger Ledger { get; } = new();
        public ScriptedRandom Random { get; } = new();
        public FakeTime Time { get; } = new();
        public FakeLogging Logging { get; } = new();
        public SlotsService Service { get; }

        public Fixture(bool renderFails = false)
        {
            Service = new SlotsService(Ledger, Random, new StubRenderer(renderFails), Logging, Time);
        }

        /// <summary>Queues the stops that put these symbols on the payline.</summary>
        public void Stops(params SlotSymbol[] line)
        {
            Random.Enqueue(line.Select((symbol, reel) => SlotMachine.Reels[reel].ToList().IndexOf(symbol)).ToArray());
        }

        /// <summary>A losing spin whose hold roll offers a hold (rolls below the offer chance).</summary>
        public void LosingSpinWithHold()
        {
            Stops(SlotSymbol.Lemon, SlotSymbol.Bell, SlotSymbol.Grape);
            Random.Enqueue(0);
        }

        public void LosingSpinWithoutHold()
        {
            Stops(SlotSymbol.Lemon, SlotSymbol.Bell, SlotSymbol.Grape);
            Random.Enqueue(99);
        }
    }

    private static int StopOf(int reel, SlotSymbol symbol) => SlotMachine.Reels[reel].ToList().IndexOf(symbol);

    #endregion

    #region Settlement (AC2, AC6, AC7)

    [Fact]
    public async Task Spin_Loss_ChargesTheBetAndFeedsTheJackpot()
    {
        var f = new Fixture();
        f.LosingSpinWithoutHold();

        var play = await f.Service.SpinAsync(UserId, 10);

        Assert.Equal(SlotsStatus.Ok, play.Status);
        Assert.Equal(-10, play.Net);
        Assert.Equal(990, play.Balance);
        Assert.Equal(990, f.Ledger.Tokens);
        var credit = Assert.Single(f.Ledger.Credits);
        Assert.Equal(-10, credit.Delta);
        Assert.Equal("Slots", credit.Details["game"]);
        Assert.Equal(Floor + 50, f.Ledger.Pot);
        Assert.Equal(f.Ledger.Pot, play.PotHundredths);
    }

    [Fact]
    public async Task Spin_SmallBet_StillFeedsTheJackpot()
    {
        var f = new Fixture();
        f.LosingSpinWithoutHold();

        await f.Service.SpinAsync(UserId, 1);

        Assert.Equal(Floor + 5, f.Ledger.Pot);
    }

    [Fact]
    public async Task Spin_Win_PaysMultiplierTimesBet()
    {
        var f = new Fixture();
        f.Stops(SlotSymbol.Seven, SlotSymbol.Seven, SlotSymbol.Seven);

        var play = await f.Service.SpinAsync(UserId, 5);

        Assert.Equal(500, play.Payout);
        Assert.Equal(495, play.Net);
        Assert.Equal(1_495, f.Ledger.Tokens);
        Assert.Equal(495, Assert.Single(f.Ledger.Credits).Delta);
    }

    [Theory]
    [InlineData(100)]
    [InlineData(50)]
    [InlineData(1)]
    public async Task Spin_Jackpot_PaysBetOverHundredOfThePotAndKeepsTheRest(int bet)
    {
        var f = new Fixture();
        f.Ledger.Pot = 250_000;
        f.Stops(SlotSymbol.Diamond, SlotSymbol.Diamond, SlotSymbol.Diamond);

        var play = await f.Service.SpinAsync(UserId, bet);

        var potWithBet = 250_000 + SlotSettlementMath.Contribution(bet);
        var share = SlotSettlementMath.JackpotShare(potWithBet, bet);
        Assert.True(play.Result!.IsJackpot);
        Assert.Equal(share / 100, play.Payout);
        Assert.Equal(share / 100 - bet, play.Net);
        Assert.Equal(Math.Max(potWithBet - share, Floor), f.Ledger.Pot);
        Assert.Equal(share / 100, long.Parse(Assert.Single(f.Ledger.Credits).Details["jackpot"]));
    }

    [Fact]
    public async Task Spin_Jackpot_TopsThePotUpToTheFloor()
    {
        var f = new Fixture();
        f.Ledger.Pot = Floor;
        f.Stops(SlotSymbol.Diamond, SlotSymbol.Diamond, SlotSymbol.Diamond);

        await f.Service.SpinAsync(UserId, 100);

        Assert.Equal(Floor, f.Ledger.Pot);
    }

    [Fact]
    public async Task Spin_JackpotSettlementFails_LogsTheClaimedAmount()
    {
        var f = new Fixture();
        f.Ledger.FailCredit = true;
        f.Stops(SlotSymbol.Diamond, SlotSymbol.Diamond, SlotSymbol.Diamond);

        var play = await f.Service.SpinAsync(UserId, 100);

        Assert.Equal(SlotsStatus.Failed, play.Status);
        Assert.Contains(f.Logging.Messages, m => m.Contains("claimed") && m.Contains($"{Floor / 100 + 5}") && m.Contains(UserId.ToString()));
    }

    [Fact]
    public async Task Spin_CreditFails_LogsWhatThePlayerIsOwed()
    {
        var f = new Fixture();
        f.Ledger.FailCredit = true;
        f.Stops(SlotSymbol.Star, SlotSymbol.Star, SlotSymbol.Star);

        var play = await f.Service.SpinAsync(UserId, 10);

        Assert.Equal(SlotsStatus.Failed, play.Status);
        Assert.Contains(f.Logging.Messages, m => m.Contains(UserId.ToString()) && m.Contains("290 tokens for a spin") && m.Contains("bet 10"));
    }

    [Fact]
    public async Task Spin_JackpotRowMissing_RefusesBeforeAnythingChanges()
    {
        var f = new Fixture();
        f.Ledger.Pot = 0;
        f.Stops(SlotSymbol.Diamond, SlotSymbol.Diamond, SlotSymbol.Diamond);

        var play = await f.Service.SpinAsync(UserId, 10);

        Assert.Equal(SlotsStatus.Failed, play.Status);
        Assert.Equal(1_000, f.Ledger.Tokens);
        Assert.Empty(f.Ledger.Credits);
        Assert.Equal(0, f.Ledger.Pot);
        Assert.Contains(f.Logging.Messages, m => m.Contains("jackpot"));
    }

    [Fact]
    public async Task Spin_JackpotClaimReturnsNothing_DoesNotChargeTheBet()
    {
        var f = new Fixture();
        f.Ledger.ClaimReturnsNothing = true;
        f.Stops(SlotSymbol.Diamond, SlotSymbol.Diamond, SlotSymbol.Diamond);

        var play = await f.Service.SpinAsync(UserId, 10);

        Assert.Equal(SlotsStatus.Failed, play.Status);
        Assert.Equal(1_000, f.Ledger.Tokens);
        Assert.Empty(f.Ledger.Credits);
        Assert.Contains(f.Logging.Messages, m => m.Contains("returned 0"));
    }

    #endregion

    #region Refusals (AC3)

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(101)]
    public async Task Spin_BetOutsideLimits_ChangesNothing(int bet)
    {
        var f = new Fixture();

        var play = await f.Service.SpinAsync(UserId, bet);

        Assert.Equal(SlotsStatus.InvalidBet, play.Status);
        AssertUntouched(f);
    }

    [Fact]
    public async Task Spin_BalanceBelowTheBet_ChangesNothing()
    {
        var f = new Fixture();
        f.Ledger.Tokens = 9;

        var play = await f.Service.SpinAsync(UserId, 10);

        Assert.Equal(SlotsStatus.InsufficientTokens, play.Status);
        Assert.Equal(9, f.Ledger.Tokens);
        AssertUntouched(f);
    }

    private static void AssertUntouched(Fixture f)
    {
        Assert.Empty(f.Ledger.Credits);
        Assert.Equal(Floor, f.Ledger.Pot);
    }

    #endregion

    #region Per-user guard (AC4)

    [Fact]
    public async Task Spin_WhilePreviousSpinIsProcessing_IsRefusedAndChangesNothing()
    {
        var f = new Fixture();
        f.LosingSpinWithoutHold();
        var release = new TaskCompletionSource();
        f.Ledger.BeforeGetTokens = () => release.Task;

        var first = f.Service.SpinAsync(UserId, 10);
        var second = await f.Service.SpinAsync(UserId, 10);
        release.SetResult();
        var firstPlay = await first;

        Assert.Equal(SlotsStatus.Busy, second.Status);
        Assert.Equal(SlotsStatus.Ok, firstPlay.Status);
        Assert.Single(f.Ledger.Credits);
        Assert.Equal(990, f.Ledger.Tokens);
    }

    [Fact]
    public async Task Spin_AfterAFailure_IsAllowedAgain()
    {
        var f = new Fixture();
        f.Ledger.FailCredit = true;
        f.LosingSpinWithoutHold();
        Assert.Equal(SlotsStatus.Failed, (await f.Service.SpinAsync(UserId, 10)).Status);

        f.Ledger.FailCredit = false;
        f.LosingSpinWithoutHold();
        Assert.Equal(SlotsStatus.Ok, (await f.Service.SpinAsync(UserId, 10)).Status);
    }

    [Fact]
    public async Task Spin_DifferentUsers_DoNotBlockEachOther()
    {
        var f = new Fixture();
        f.LosingSpinWithoutHold();
        f.LosingSpinWithoutHold();
        var release = new TaskCompletionSource();
        var gated = false;
        // Only the first player's balance read waits, so the two spins never touch the scripted randomness at the same time.
        f.Ledger.BeforeGetTokens = () =>
        {
            if (gated) return Task.CompletedTask;
            gated = true;
            return release.Task;
        };

        var first = f.Service.SpinAsync(UserId, 10);
        var second = await f.Service.SpinAsync(UserId + 1, 10);
        release.SetResult();

        Assert.Equal(SlotsStatus.Ok, second.Status);
        Assert.Equal(SlotsStatus.Ok, (await first).Status);
    }

    #endregion

    #region Hold and respin (AC9)

    [Fact]
    public async Task Hold_IsOfferedOnALosingSpin_WithNothingHeld()
    {
        var f = new Fixture();
        f.LosingSpinWithHold();

        var play = await f.Service.SpinAsync(UserId, 10);

        Assert.NotNull(play.Hold);
        Assert.Equal(10, play.Hold!.Bet);
        Assert.All(play.Hold.Held, held => Assert.False(held));
    }

    [Fact]
    public async Task Respin_KeepsHeldReelsPaysAtTheOriginalBetAndCostsNothing()
    {
        var f = new Fixture();
        f.LosingSpinWithHold();
        var spin = await f.Service.SpinAsync(UserId, 10);
        var hold = f.Service.ToggleHold(UserId, spin.Hold!.OfferId, 0)!;
        Assert.True(hold.Held[0]);
        // Reel 0 stays on the lemon; the other two land on lemons.
        f.Random.Enqueue(StopOf(1, SlotSymbol.Lemon), StopOf(2, SlotSymbol.Lemon));
        var potBefore = f.Ledger.Pot;

        var respin = await f.Service.RespinAsync(UserId, hold.OfferId);

        Assert.Equal(SlotsStatus.Ok, respin.Status);
        Assert.True(respin.Result!.IsRespin);
        Assert.Equal(spin.Result!.Stops[0], respin.Result.Stops[0]);
        Assert.Equal(40, respin.Payout);
        Assert.Equal(40, respin.Net);
        Assert.Null(respin.Hold);
        Assert.Equal(potBefore, f.Ledger.Pot);
        var credit = f.Ledger.Credits[^1];
        Assert.Equal(40, credit.Delta);
        Assert.Equal("Slots", credit.Details["game"]);
        Assert.Equal(1_030, f.Ledger.Tokens);
    }

    [Fact]
    public async Task Respin_CannotBeUsedTwiceOnTheSameOffer()
    {
        var f = new Fixture();
        f.LosingSpinWithHold();
        var spin = await f.Service.SpinAsync(UserId, 10);
        f.Random.Enqueue(0, 0, 0);
        Assert.Equal(SlotsStatus.Ok, (await f.Service.RespinAsync(UserId, spin.Hold!.OfferId)).Status);
        var creditsAfterFirst = f.Ledger.Credits.Count;
        var balanceAfterFirst = f.Ledger.Tokens;

        var second = await f.Service.RespinAsync(UserId, spin.Hold.OfferId);

        Assert.Equal(SlotsStatus.NoHold, second.Status);
        Assert.Equal(creditsAfterFirst, f.Ledger.Credits.Count);
        Assert.Equal(balanceAfterFirst, f.Ledger.Tokens);
    }

    [Fact]
    public async Task Respin_ThatWinsNothing_RecordsNoTransaction()
    {
        var f = new Fixture();
        f.LosingSpinWithHold();
        var spin = await f.Service.SpinAsync(UserId, 10);
        var creditsBefore = f.Ledger.Credits.Count;
        f.Random.Enqueue(StopOf(0, SlotSymbol.Lemon), StopOf(1, SlotSymbol.Bell), StopOf(2, SlotSymbol.Grape));

        var respin = await f.Service.RespinAsync(UserId, spin.Hold!.OfferId);

        Assert.Equal(SlotsStatus.Ok, respin.Status);
        Assert.Equal(0, respin.Net);
        Assert.Equal(creditsBefore, f.Ledger.Credits.Count);
        Assert.Equal(990, respin.Balance);
    }

    [Fact]
    public async Task Respin_JackpotRowMissing_KeepsTheOffer()
    {
        var f = new Fixture();
        f.LosingSpinWithHold();
        var spin = await f.Service.SpinAsync(UserId, 10);
        f.Ledger.Pot = 0;

        var failed = await f.Service.RespinAsync(UserId, spin.Hold!.OfferId);
        f.Ledger.Pot = Floor;
        f.Random.Enqueue(StopOf(0, SlotSymbol.Lemon), StopOf(1, SlotSymbol.Bell), StopOf(2, SlotSymbol.Grape));
        var retry = await f.Service.RespinAsync(UserId, spin.Hold!.OfferId);

        Assert.Equal(SlotsStatus.Failed, failed.Status);
        Assert.Equal(SlotsStatus.Ok, retry.Status);
    }

    [Fact]
    public async Task Respin_CreditFails_LogsWhatThePlayerIsOwed()
    {
        var f = new Fixture();
        f.LosingSpinWithHold();
        var spin = await f.Service.SpinAsync(UserId, 10);
        f.Ledger.FailCredit = true;
        f.Random.Enqueue(StopOf(0, SlotSymbol.Cherry), StopOf(1, SlotSymbol.Bell), StopOf(2, SlotSymbol.Grape));

        var respin = await f.Service.RespinAsync(UserId, spin.Hold!.OfferId);

        Assert.Equal(SlotsStatus.Failed, respin.Status);
        Assert.Contains(f.Logging.Messages, m => m.Contains(UserId.ToString()) && m.Contains("10 tokens for a free respin") && m.Contains("payout 10, bet 10"));
    }

    [Fact]
    public async Task Respin_OfAnOfferThatNeverExisted_IsRefused()
    {
        var f = new Fixture();

        Assert.Equal(SlotsStatus.NoHold, (await f.Service.RespinAsync(UserId, 999)).Status);
    }

    [Fact]
    public async Task Hold_ExpiresAfterTenMinutes()
    {
        var f = new Fixture();
        f.LosingSpinWithHold();
        var spin = await f.Service.SpinAsync(UserId, 10);

        f.Time.Now += TimeSpan.FromMinutes(9);
        Assert.NotNull(f.Service.ToggleHold(UserId, spin.Hold!.OfferId, 1));

        f.Time.Now += TimeSpan.FromMinutes(1);
        Assert.Null(f.Service.ToggleHold(UserId, spin.Hold.OfferId, 1));
        Assert.Equal(SlotsStatus.NoHold, (await f.Service.RespinAsync(UserId, spin.Hold.OfferId)).Status);
    }

    [Fact]
    public async Task Hold_EndsAtThePlayersNextSpin()
    {
        var f = new Fixture();
        f.LosingSpinWithHold();
        var spin = await f.Service.SpinAsync(UserId, 10);
        f.LosingSpinWithoutHold();
        await f.Service.SpinAsync(UserId, 10);

        Assert.Null(f.Service.ToggleHold(UserId, spin.Hold!.OfferId, 0));
        Assert.Equal(SlotsStatus.NoHold, (await f.Service.RespinAsync(UserId, spin.Hold.OfferId)).Status);
    }

    [Fact]
    public async Task Hold_BelongsToThePlayerWhoGotIt()
    {
        var f = new Fixture();
        f.LosingSpinWithHold();
        var spin = await f.Service.SpinAsync(UserId, 10);

        Assert.Null(f.Service.ToggleHold(UserId + 1, spin.Hold!.OfferId, 0));
        Assert.Equal(SlotsStatus.NoHold, (await f.Service.RespinAsync(UserId + 1, spin.Hold.OfferId)).Status);
    }

    [Fact]
    public async Task Hold_ToggleFlipsTheReelBackAndForth()
    {
        var f = new Fixture();
        f.LosingSpinWithHold();
        var spin = await f.Service.SpinAsync(UserId, 10);

        Assert.True(f.Service.ToggleHold(UserId, spin.Hold!.OfferId, 2)!.Held[2]);
        Assert.False(f.Service.ToggleHold(UserId, spin.Hold.OfferId, 2)!.Held[2]);
        Assert.Null(f.Service.ToggleHold(UserId, spin.Hold.OfferId, 3));
    }

    #endregion

    #region Render failure (AC11)

    [Fact]
    public async Task Spin_WhenTheGifFails_StillSettlesAndReturnsTheResult()
    {
        var f = new Fixture(renderFails: true);
        f.Stops(SlotSymbol.Seven, SlotSymbol.Seven, SlotSymbol.Seven);

        var play = await f.Service.SpinAsync(UserId, 5);

        Assert.Equal(SlotsStatus.Ok, play.Status);
        Assert.Null(play.Gif);
        Assert.Equal(500, play.Payout);
        Assert.Equal(1_495, f.Ledger.Tokens);
        Assert.Contains(f.Logging.Messages, m => m.Contains("render"));
    }

    [Fact]
    public async Task Spin_WhenTheGifRenders_ReturnsItsBytes()
    {
        var f = new Fixture();
        f.LosingSpinWithoutHold();

        var play = await f.Service.SpinAsync(UserId, 5);

        Assert.Equal(new byte[] { 1, 2, 3 }, play.Gif);
    }

    #endregion

    #region Public posts (AC7, AC8)

    [Theory]
    [InlineData(SlotSymbol.Seven, SlotSymbol.Seven, SlotSymbol.Seven, true)]   // 100x
    [InlineData(SlotSymbol.Star, SlotSymbol.Star, SlotSymbol.Star, true)]      // 30x
    [InlineData(SlotSymbol.Bell, SlotSymbol.Bell, SlotSymbol.Bell, false)]     // 15x
    [InlineData(SlotSymbol.Cherry, SlotSymbol.Lemon, SlotSymbol.Grape, false)] // 1x
    [InlineData(SlotSymbol.Diamond, SlotSymbol.Diamond, SlotSymbol.Diamond, true)]
    public async Task Play_IsPostedPublicly_OnlyForBigWinsAndTheJackpot(SlotSymbol a, SlotSymbol b, SlotSymbol c, bool expected)
    {
        var f = new Fixture();
        f.Stops(a, b, c);

        var play = await f.Service.SpinAsync(UserId, 10);

        Assert.Equal(expected, play.PostPublicly);
    }

    [Fact]
    public void Refusal_IsNeverPostedPublicly()
    {
        Assert.False(SlotsPlay.Refused(SlotsStatus.Busy).PostPublicly);
    }

    #endregion
}
