namespace DiscordBot.Domain;

/// <summary>What one settled spin does to the player's balance and to the shared jackpot.</summary>
/// <param name="Payout">Tokens paid to the player: the paytable win, or the jackpot share.</param>
/// <param name="Net">Change of the player's balance: payout minus the bet (a respin has no bet).</param>
/// <param name="ContributionHundredths">Added to the jackpot, in hundredths of a token. 0 for a respin.</param>
/// <param name="JackpotShareHundredths">Taken from the jackpot, in hundredths of a token. 0 unless three diamonds landed.</param>
/// <param name="PotAfterHundredths">The jackpot after contribution, claim and floor top-up.</param>
public sealed record SlotSettlement(
    long Payout,
    long Net,
    long ContributionHundredths,
    long JackpotShareHundredths,
    long PotAfterHundredths);

/// <summary>
/// Settlement math. The jackpot is counted in hundredths of a token so a 1-token bet adds exactly 5% of it.
/// </summary>
public static class SlotSettlementMath
{
    public const int HundredthsPerToken = 100;

    /// <summary>The jackpot never stays below this: a claim tops it up, and a new database starts with it.</summary>
    public const long JackpotFloorHundredths = 1_000 * HundredthsPerToken;

    /// <summary>Share of every paid bet that feeds the jackpot, in percent.</summary>
    public const int ContributionPercent = 5;

    /// <summary>5% of the bet, in hundredths of a token: a bet of 1 token is 100 hundredths, 5% of it is 5.</summary>
    public static long Contribution(int bet) => (long)bet * ContributionPercent;

    /// <summary>
    /// A jackpot win pays bet/100 of the pot, rounded down to whole tokens. The rest stays in the pot.
    /// </summary>
    public static long JackpotShare(long potHundredths, int bet) =>
        potHundredths * bet / (100L * HundredthsPerToken) * HundredthsPerToken;

    /// <summary>The pot after a claim, topped up to the floor when it fell below it.</summary>
    public static long PotAfterClaim(long potHundredths, long shareHundredths) =>
        Math.Max(potHundredths - shareHundredths, JackpotFloorHundredths);

    /// <param name="result">The spin to settle.</param>
    /// <param name="bet">The bet of the spin, for a respin the bet of the spin it replays.</param>
    /// <param name="potHundredths">The jackpot before this spin.</param>
    public static SlotSettlement Settle(SlotResult result, int bet, long potHundredths)
    {
        // The return analysis and the floor top-up bound only hold for bets inside the limits.
        ArgumentOutOfRangeException.ThrowIfLessThan(bet, SlotMachine.MinBet);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(bet, SlotMachine.MaxBet);

        // A respin is free: it feeds nothing, and its win is pure gain.
        var contribution = result.IsRespin ? 0 : Contribution(bet);
        var pot = potHundredths + contribution;

        var share = result.IsJackpot ? JackpotShare(pot, bet) : 0;
        if (result.IsJackpot)
            pot = PotAfterClaim(pot, share);

        var payout = result.IsJackpot ? share / HundredthsPerToken : (long)result.Multiplier * bet;
        var cost = result.IsRespin ? 0 : bet;
        return new SlotSettlement(payout, payout - cost, contribution, share, pot);
    }
}
