using DiscordBot.Domain;

namespace DiscordBot.Tests.Domain.Casino;

public class SlotMachineTests
{
    private sealed class SeededSlotRandom(int seed) : ISlotRandom
    {
        private readonly Random _random = new(seed);
        public int Next(int maxExclusive) => _random.Next(maxExclusive);
    }

    private static SlotResult Result(int multiplier = 0, bool jackpot = false, bool respin = false) =>
        new([0, 0, 0], [SlotSymbol.Cherry, SlotSymbol.Cherry, SlotSymbol.Cherry], multiplier, jackpot, false, respin);

    private static SlotResult FindHoldOffer(int seed)
    {
        var rng = new SeededSlotRandom(seed);
        while (true)
        {
            var spin = SlotMachine.Spin(rng);
            if (spin.HoldOffered) return spin;
        }
    }

    #region Exact return (AC5)

    [Fact]
    public void ExactReturn_StaysBetween92And96Percent()
    {
        var report = SlotReturnAnalysis.Compute();

        Assert.InRange(report.ReturnLow, 0.92, 0.96);
        Assert.InRange(report.ReturnHigh, 0.92, 0.96);
    }

    [Fact]
    public void ExactReturn_WinsOnOneSpinInThreeToFour()
    {
        Assert.InRange(SlotReturnAnalysis.Compute().HitRate, 1 / 4.0, 1 / 3.0);
    }

    [Fact]
    public void ExactReturn_JackpotLineOnceEvery3000To10000Spins()
    {
        var spinsPerJackpot = 1 / SlotReturnAnalysis.Compute().JackpotChance;

        Assert.InRange(spinsPerJackpot, 3_000, 10_000);
    }

    [Fact]
    public void ExactReturn_AssumedJackpotValueIsCloseToTheSteadyState()
    {
        var steady = SlotReturnAnalysis.Compute().SteadyStateJackpotMultiple;

        Assert.InRange(steady, SlotReturnAnalysis.AssumedJackpotMultiple / 2, SlotReturnAnalysis.AssumedJackpotMultiple * 2);
    }

    [Fact]
    public void ExactReturn_JackpotContributionIsFivePercent()
    {
        Assert.Equal(0.05, SlotReturnAnalysis.Compute().JackpotContribution, 10);
    }

    [Fact]
    public void ExactReturn_AgreesWithSeededFirstSpins()
    {
        // Guards the enumeration against drifting away from what Spin actually does.
        var report = SlotReturnAnalysis.Compute();
        var rng = new SeededSlotRandom(2024);
        const int spins = 1_000_000;
        double wins = 0;
        long hits = 0;
        for (var i = 0; i < spins; i++)
        {
            var spin = SlotMachine.Spin(rng);
            wins += spin.Multiplier;
            if (spin.IsWin) hits++;
        }

        Assert.Equal(report.BaseReturn, wins / spins, 2);
        Assert.Equal(report.HitRate, (double)hits / spins, 2);
    }

    #endregion

    #region Strips and paytable

    [Fact]
    public void Reels_AreNotEmptyAndContainEverySymbol()
    {
        Assert.Equal(SlotMachine.ReelCount, SlotMachine.Reels.Count);
        foreach (var reel in SlotMachine.Reels)
            foreach (var symbol in Enum.GetValues<SlotSymbol>())
                Assert.Contains(symbol, reel);
    }

    [Fact]
    public void Reels_NeverPutTheSameSymbolNextToItself()
    {
        foreach (var reel in SlotMachine.Reels)
            for (var i = 0; i < reel.Count; i++)
                Assert.NotEqual(reel[i], reel[(i + 1) % reel.Count]);
    }

    [Fact]
    public void Paytable_ListsEveryWinningLineOnceWithTheMultipliersTheEnginePays()
    {
        var paytable = SlotMachine.Paytable;

        Assert.Single(paytable, e => e.IsJackpot);
        foreach (var symbol in Enum.GetValues<SlotSymbol>())
            Assert.Single(paytable, e => e.Symbol == symbol && e.Count == 3);
        Assert.Equal(paytable.Count, paytable.Select(e => e.Pattern).Distinct().Count());
        foreach (var entry in paytable)
        {
            SlotSymbol[] line = entry.Count == 3 ? [entry.Symbol, entry.Symbol, entry.Symbol] : [entry.Symbol, SlotSymbol.Lemon, entry.Symbol];
            Assert.Equal((entry.Multiplier, entry.IsJackpot), SlotMachine.Evaluate(line));
        }
    }

    [Fact]
    public void Paytable_ChancesMatchEveryStopCombination()
    {
        var reels = SlotMachine.Reels;
        var counted = SlotMachine.Paytable.ToDictionary(e => e, _ => 0.0);
        var weight = 1.0 / (reels[0].Count * reels[1].Count * reels[2].Count);
        foreach (var a in reels[0])
        foreach (var b in reels[1])
        foreach (var c in reels[2])
            if (SlotMachine.Match([a, b, c]) is { } entry)
                counted[entry] += weight;

        foreach (var (entry, chance) in counted)
            Assert.Equal(chance, entry.Chance, 12);
    }

    [Fact]
    public void Paytable_EveryWinPaysMoreThanTheBet()
    {
        // A win that only gives the bet back feels like a loss; every line is a real gain instead.
        Assert.All(SlotMachine.Paytable.Where(e => !e.IsJackpot), e => Assert.True(e.Multiplier >= 2, e.Pattern));
    }

    [Fact]
    public void Paytable_ARarerLineNeverPaysLessThanAMoreCommonOneOfTheSameKind()
    {
        var lines = SlotMachine.Paytable.Where(e => !e.IsJackpot).ToList();
        foreach (var rare in lines)
            foreach (var common in lines.Where(e => e.Count == rare.Count && e.Chance > rare.Chance))
            {
                // Three of a kind strictly, so its payout ranks the symbols; pairs may share a multiplier.
                if (rare.Count == 3) Assert.True(rare.Multiplier > common.Multiplier, $"{rare.Pattern} vs {common.Pattern}");
                else Assert.True(rare.Multiplier >= common.Multiplier, $"{rare.Pattern} vs {common.Pattern}");
            }
    }

    [Fact]
    public void Paytable_AnnouncesExactlyTheLinesRarerThanTheThreshold()
    {
        var paytable = SlotMachine.Paytable;

        Assert.All(paytable, e => Assert.Equal(e.Chance < 1.0 / SlotMachine.PublicWinOneIn, e.IsAnnounced));
        Assert.Contains(paytable, e => e.IsAnnounced && !e.IsJackpot);
        Assert.Contains(paytable, e => e.IsJackpot && e.IsAnnounced);
        Assert.DoesNotContain(paytable, e => e.Count == 2 && e.IsAnnounced);
    }

    [Theory]
    [InlineData(SlotSymbol.Lemon, SlotSymbol.Grape, SlotSymbol.Bell)]
    [InlineData(SlotSymbol.Cherry, SlotSymbol.Lemon, SlotSymbol.Grape)]
    [InlineData(SlotSymbol.Lemon, SlotSymbol.Lemon, SlotSymbol.Seven)]
    [InlineData(SlotSymbol.Diamond, SlotSymbol.Diamond, SlotSymbol.Seven)]
    public void Evaluate_NoWinningLine_PaysNothing(SlotSymbol a, SlotSymbol b, SlotSymbol c)
    {
        Assert.Equal((0, false), SlotMachine.Evaluate([a, b, c]));
        Assert.False(SlotMachine.IsRareWin([a, b, c]));
    }

    [Fact]
    public void Evaluate_PairsPayAnywhereOnTheLine()
    {
        var pair = SlotMachine.Evaluate([SlotSymbol.Bell, SlotSymbol.Bell, SlotSymbol.Lemon]).Multiplier;

        Assert.True(pair > 0);
        Assert.Equal(pair, SlotMachine.Evaluate([SlotSymbol.Bell, SlotSymbol.Lemon, SlotSymbol.Bell]).Multiplier);
        Assert.Equal(pair, SlotMachine.Evaluate([SlotSymbol.Lemon, SlotSymbol.Bell, SlotSymbol.Bell]).Multiplier);
        Assert.True(SlotMachine.Evaluate([SlotSymbol.Bell, SlotSymbol.Bell, SlotSymbol.Bell]).Multiplier > pair);
    }

    [Fact]
    public void Evaluate_ThreeDiamonds_IsTheJackpotAndPaysNoMultiple()
    {
        Assert.Equal((0, true), SlotMachine.Evaluate([SlotSymbol.Diamond, SlotSymbol.Diamond, SlotSymbol.Diamond]));
    }

    #endregion

    #region Spin and hold

    [Fact]
    public void Spin_SameSeed_GivesTheSameSpins()
    {
        var first = new SeededSlotRandom(42);
        var second = new SeededSlotRandom(42);

        for (var i = 0; i < 200; i++)
            Assert.Equal(SlotMachine.Spin(first).Stops, SlotMachine.Spin(second).Stops);
    }

    [Fact]
    public void Spin_SymbolsMatchTheStops()
    {
        var rng = new SeededSlotRandom(7);
        for (var i = 0; i < 200; i++)
        {
            var spin = SlotMachine.Spin(rng);
            for (var reel = 0; reel < SlotMachine.ReelCount; reel++)
                Assert.Equal(SlotMachine.Reels[reel][spin.Stops[reel]], spin.Symbols[reel]);
        }
    }

    [Fact]
    public void Spin_OffersAHoldOnlyAfterALoss_AboutOneTimeInFour()
    {
        var rng = new SeededSlotRandom(1);
        int losses = 0, offers = 0;
        for (var i = 0; i < 40_000; i++)
        {
            var spin = SlotMachine.Spin(rng);
            Assert.False(spin.IsRespin);
            if (spin.IsWin)
            {
                Assert.False(spin.HoldOffered);
                continue;
            }
            losses++;
            if (spin.HoldOffered) offers++;
        }

        Assert.InRange((double)offers / losses, 0.23, 0.27);
    }

    [Fact]
    public void Respin_KeepsHeldReelsAndNeverOffersAnotherHold()
    {
        for (var seed = 0; seed < 100; seed++)
        {
            var offer = FindHoldOffer(seed);
            var rng = new SeededSlotRandom(seed + 1000);

            var respin = SlotMachine.Respin(offer, [true, false, true], rng);

            Assert.Equal(offer.Stops[0], respin.Stops[0]);
            Assert.Equal(offer.Stops[2], respin.Stops[2]);
            Assert.True(respin.IsRespin);
            Assert.False(respin.HoldOffered);
        }
    }

    [Fact]
    public void Respin_HoldingNothing_SpinsEveryReel()
    {
        var offer = FindHoldOffer(5);
        var rng = new SeededSlotRandom(99);
        var changed = false;
        for (var i = 0; i < 20 && !changed; i++)
            changed = !SlotMachine.Respin(offer, [false, false, false], rng).Stops.SequenceEqual(offer.Stops);

        Assert.True(changed);
    }

    [Fact]
    public void Respin_HoldingEverything_ReplaysTheSameLine()
    {
        var offer = FindHoldOffer(3);

        var respin = SlotMachine.Respin(offer, [true, true, true], new SeededSlotRandom(1));

        Assert.Equal(offer.Stops, respin.Stops);
        Assert.False(respin.IsWin);
    }

    [Fact]
    public void Respin_WithoutAnOffer_Throws()
    {
        var noOffer = Result();
        var respin = SlotMachine.Respin(FindHoldOffer(2), [false, false, false], new SeededSlotRandom(1));

        Assert.Throws<InvalidOperationException>(() => SlotMachine.Respin(noOffer, [false, false, false], new SeededSlotRandom(1)));
        Assert.Throws<InvalidOperationException>(() => SlotMachine.Respin(respin, [false, false, false], new SeededSlotRandom(1)));
    }

    [Fact]
    public void Respin_WithTheWrongNumberOfFlags_Throws()
    {
        Assert.Throws<ArgumentException>(() => SlotMachine.Respin(FindHoldOffer(2), [true, false], new SeededSlotRandom(1)));
    }

    #endregion

    #region Settlement (AC2, AC6, AC7)

    [Theory]
    [InlineData(1, 0, -1)]
    [InlineData(100, 0, -100)]
    [InlineData(10, 2, 10)]
    [InlineData(100, 30, 2_900)]
    public void Settle_PaidSpin_NetIsPayoutMinusBet(int bet, int multiplier, long expectedNet)
    {
        var settlement = SlotSettlementMath.Settle(Result(multiplier), bet, SlotSettlementMath.JackpotFloorHundredths);

        Assert.Equal((long)multiplier * bet, settlement.Payout);
        Assert.Equal(expectedNet, settlement.Net);
        Assert.Equal(0, settlement.JackpotShareHundredths);
    }

    [Theory]
    [InlineData(1, 5)]
    [InlineData(2, 10)]
    [InlineData(3, 15)]
    [InlineData(100, 500)]
    public void Settle_PaidSpin_AddsExactlyFivePercentOfTheBetToTheJackpot(int bet, long expectedHundredths)
    {
        var pot = 123_456L;

        var settlement = SlotSettlementMath.Settle(Result(), bet, pot);

        Assert.Equal(expectedHundredths, settlement.ContributionHundredths);
        Assert.Equal(pot + expectedHundredths, settlement.PotAfterHundredths);
        // 5% of the bet in tokens, whatever the bet
        Assert.Equal(bet * 0.05, settlement.ContributionHundredths / (double)SlotSettlementMath.HundredthsPerToken, 10);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(101)]
    public void Settle_BetOutsideTheLimits_Throws(int bet)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => SlotSettlementMath.Settle(Result(), bet, SlotSettlementMath.JackpotFloorHundredths));
    }

    [Fact]
    public void Settle_Respin_PaysItsWinAsPureGainAndFeedsNothing()
    {
        var pot = 200_000L;

        var win = SlotSettlementMath.Settle(Result(multiplier: 5, respin: true), 20, pot);
        var loss = SlotSettlementMath.Settle(Result(respin: true), 20, pot);

        Assert.Equal(100, win.Payout);
        Assert.Equal(100, win.Net);
        Assert.Equal(0, win.ContributionHundredths);
        Assert.Equal(pot, win.PotAfterHundredths);
        Assert.Equal(0, loss.Net);
        Assert.Equal(pot, loss.PotAfterHundredths);
    }

    [Fact]
    public void Settle_JackpotWithMaxBet_TakesTheWholePotAndTopsItUpToTheFloor()
    {
        var pot = SlotSettlementMath.JackpotFloorHundredths;

        var settlement = SlotSettlementMath.Settle(Result(jackpot: true), 100, pot);

        // the bet's own 5 tokens are in the pot before the claim
        Assert.Equal(1_005, settlement.Payout);
        Assert.Equal(905, settlement.Net);
        Assert.Equal(100_500, settlement.JackpotShareHundredths);
        Assert.Equal(SlotSettlementMath.JackpotFloorHundredths, settlement.PotAfterHundredths);
    }

    [Fact]
    public void Settle_JackpotWithSmallerBet_TakesBetOverHundredOfThePotAndKeepsTheRest()
    {
        var pot = 400_000L;

        var settlement = SlotSettlementMath.Settle(Result(jackpot: true), 25, pot);

        // pot after contribution 400,125 hundredths; 25/100 of it is 1,000.31 tokens, rounded down to 1,000
        Assert.Equal(1_000, settlement.Payout);
        Assert.Equal(975, settlement.Net);
        Assert.Equal(100_000, settlement.JackpotShareHundredths);
        Assert.Equal(300_125, settlement.PotAfterHundredths);
    }

    [Fact]
    public void Settle_JackpotLeavingLessThanTheFloor_TopsThePotUp()
    {
        var pot = SlotSettlementMath.JackpotFloorHundredths;

        var settlement = SlotSettlementMath.Settle(Result(jackpot: true), 50, pot);

        // pot after contribution 100,250 hundredths; half of it is 501.25 tokens, rounded down to 501
        Assert.Equal(501, settlement.Payout);
        Assert.Equal(SlotSettlementMath.JackpotFloorHundredths, settlement.PotAfterHundredths);
    }

    [Fact]
    public void Settle_JackpotOnARespin_PaysTheShareOfTheOriginalBetWithoutContribution()
    {
        var pot = 400_000L;

        var settlement = SlotSettlementMath.Settle(Result(jackpot: true, respin: true), 100, pot);

        Assert.Equal(4_000, settlement.Payout);
        Assert.Equal(4_000, settlement.Net);
        Assert.Equal(0, settlement.ContributionHundredths);
        Assert.Equal(SlotSettlementMath.JackpotFloorHundredths, settlement.PotAfterHundredths);
    }

    [Fact]
    public void JackpotFloorTopUp_NeverExceedsTenTimesTheBet()
    {
        // The exact-return bound relies on this: a claim costs the house at most 10 tokens per token bet.
        foreach (var bet in new[] { 1, 7, 25, 50, 99, 100 })
            foreach (var pot in new long[] { SlotSettlementMath.JackpotFloorHundredths, 150_000, 1_000_000, 50_000_000 })
            {
                var settlement = SlotSettlementMath.Settle(Result(jackpot: true), bet, pot);
                var beforeTopUp = pot + settlement.ContributionHundredths - settlement.JackpotShareHundredths;
                var topUp = settlement.PotAfterHundredths - beforeTopUp;

                Assert.InRange(topUp, 0, 10L * bet * SlotSettlementMath.HundredthsPerToken);
            }
    }

    #endregion
}
