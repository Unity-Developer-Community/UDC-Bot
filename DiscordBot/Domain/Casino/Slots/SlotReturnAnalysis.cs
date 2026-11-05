namespace DiscordBot.Domain;

/// <summary>Exact statistics of the slot machine, per token bet.</summary>
/// <param name="BaseReturn">Paytable wins of the first spin, jackpot excluded.</param>
/// <param name="HoldReturn">Paytable wins gained by respinning with the best hold choice, jackpot excluded.</param>
/// <param name="HitRate">Share of first spins that win something, jackpot included.</param>
/// <param name="JackpotChance">Chance of the jackpot line per paid spin, counting the respin it may lead to.</param>
/// <param name="JackpotContribution">Share of every bet that goes into the jackpot, and is paid back out of it.</param>
/// <param name="MaxFloorTopUp">Upper bound of what the house adds to keep the jackpot at its floor.</param>
public sealed record SlotReturnReport(
    double BaseReturn,
    double HoldReturn,
    double HitRate,
    double JackpotChance,
    double JackpotContribution,
    double MaxFloorTopUp)
{
    /// <summary>Return to players without the house-funded floor.</summary>
    public double ReturnLow => BaseReturn + HoldReturn + JackpotContribution;

    /// <summary>Return to players with the floor top-up at its upper bound.</summary>
    public double ReturnHigh => ReturnLow + MaxFloorTopUp;

    /// <summary>Jackpot win as a multiple of the bet once the pot has settled where contributions balance payouts.</summary>
    public double SteadyStateJackpotMultiple => JackpotContribution / JackpotChance;
}

/// <summary>
/// Computes the exact return of the machine by enumerating every stop combination, with no random simulation.
/// </summary>
public static class SlotReturnAnalysis
{
    /// <summary>
    /// What a player expects a jackpot to be worth, as a multiple of the bet, when choosing which reels to hold.
    /// Close to the steady state, which a test checks. The reported return therefore holds for a jackpot at its long-run
    /// size; a player who only plays when the pot is far above it has an edge, as with any shared progressive jackpot.
    /// </summary>
    public const double AssumedJackpotMultiple = 250;

    /// <summary>
    /// A claim with the largest bet takes the whole pot and tops it up by at most 1,000 tokens, so the top-up is at most
    /// 10 tokens per token bet, and it happens only at the jackpot chance.
    /// </summary>
    private const double MaxFloorTopUpPerJackpot = 10;

    private readonly record struct HoldKey(int Mask, int S0, int S1, int S2);

    public static SlotReturnReport Compute()
    {
        var reels = SlotMachine.Reels;
        var memo = new Dictionary<HoldKey, (double Wins, double Jackpot)>();
        double baseReturn = 0, holdReturn = 0, hitRate = 0, jackpotChance = 0;

        for (var a = 0; a < reels[0].Count; a++)
        for (var b = 0; b < reels[1].Count; b++)
        for (var c = 0; c < reels[2].Count; c++)
        {
            var weight = 1.0 / ((double)reels[0].Count * reels[1].Count * reels[2].Count);
            SlotSymbol[] line = [reels[0][a], reels[1][b], reels[2][c]];
            var (multiplier, isJackpot) = SlotMachine.Evaluate(line);

            if (isJackpot)
            {
                jackpotChance += weight;
                hitRate += weight;
            }
            else if (multiplier > 0)
            {
                baseReturn += weight * multiplier;
                hitRate += weight;
            }
            else
            {
                var best = BestHold(line, memo);
                var offered = weight * SlotMachine.HoldOfferPercent / 100.0;
                holdReturn += offered * best.Wins;
                jackpotChance += offered * best.Jackpot;
            }
        }

        return new SlotReturnReport(
            baseReturn,
            holdReturn,
            hitRate,
            jackpotChance,
            SlotSettlementMath.ContributionPercent / 100.0,
            MaxFloorTopUpPerJackpot * jackpotChance);
    }

    /// <summary>The hold with the highest expected value for a losing line, jackpot valued at <see cref="AssumedJackpotMultiple"/>.</summary>
    private static (double Wins, double Jackpot) BestHold(SlotSymbol[] line, Dictionary<HoldKey, (double, double)> memo)
    {
        (double Wins, double Jackpot) best = default;
        var bestValue = double.NegativeInfinity;
        for (var mask = 0; mask < 1 << SlotMachine.ReelCount; mask++)
        {
            var value = HoldValue(mask, line, memo);
            var total = value.Wins + value.Jackpot * AssumedJackpotMultiple;
            if (total > bestValue)
            {
                bestValue = total;
                best = value;
            }
        }
        return best;
    }

    /// <summary>Expected wins and jackpot chance of one respin that holds the reels in <paramref name="mask"/>.</summary>
    private static (double Wins, double Jackpot) HoldValue(int mask, SlotSymbol[] line, Dictionary<HoldKey, (double, double)> memo)
    {
        int Held(int reel) => (mask & (1 << reel)) != 0 ? (int)line[reel] : -1;
        var key = new HoldKey(mask, Held(0), Held(1), Held(2));
        if (memo.TryGetValue(key, out var cached))
            return cached;

        var reels = SlotMachine.Reels;
        double wins = 0, jackpot = 0;
        var respun = new SlotSymbol[SlotMachine.ReelCount];
        var stopsA = Held(0) >= 0 ? [-1] : Enumerable.Range(0, reels[0].Count).ToArray();
        var stopsB = Held(1) >= 0 ? [-1] : Enumerable.Range(0, reels[1].Count).ToArray();
        var stopsC = Held(2) >= 0 ? [-1] : Enumerable.Range(0, reels[2].Count).ToArray();

        // Every free reel is enumerated over its whole strip; a held reel contributes its single known symbol.
        var combinations = (double)stopsA.Length * stopsB.Length * stopsC.Length;
        foreach (var a in stopsA)
        foreach (var b in stopsB)
        foreach (var c in stopsC)
        {
            respun[0] = a < 0 ? line[0] : reels[0][a];
            respun[1] = b < 0 ? line[1] : reels[1][b];
            respun[2] = c < 0 ? line[2] : reels[2][c];
            var (multiplier, isJackpot) = SlotMachine.Evaluate(respun);
            if (isJackpot) jackpot += 1 / combinations;
            else wins += multiplier / combinations;
        }

        memo[key] = (wins, jackpot);
        return (wins, jackpot);
    }
}
