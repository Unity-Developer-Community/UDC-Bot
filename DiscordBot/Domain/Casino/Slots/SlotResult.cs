namespace DiscordBot.Domain;

/// <summary>
/// The outcome of one slot spin or respin.
/// </summary>
/// <param name="Stops">Index each reel stopped on in its strip. The middle row of the machine shows the symbol at this index.</param>
/// <param name="Symbols">The symbol each reel shows on the payline.</param>
/// <param name="Multiplier">Win as a multiple of the bet, 0 when nothing is won. Always 0 for the jackpot, which pays a share of the pot instead.</param>
/// <param name="IsJackpot">Three diamonds on the payline.</param>
/// <param name="HoldOffered">The player may hold reels and respin for free. Never set on a respin.</param>
/// <param name="IsRespin">The result of a free respin.</param>
public sealed record SlotResult(
    IReadOnlyList<int> Stops,
    IReadOnlyList<SlotSymbol> Symbols,
    int Multiplier,
    bool IsJackpot,
    bool HoldOffered,
    bool IsRespin)
{
    public bool IsWin => IsJackpot || Multiplier > 0;
}

/// <summary>One row of the paytable shown to players.</summary>
/// <param name="Pattern">The symbols as they appear on the line, for example "🍒🍒🍒" or "🍒 + any".</param>
/// <param name="Multiplier">Win as a multiple of the bet, 0 for the jackpot.</param>
/// <param name="IsJackpot">This row pays a share of the shared jackpot.</param>
public sealed record SlotPaytableEntry(string Pattern, int Multiplier, bool IsJackpot);
