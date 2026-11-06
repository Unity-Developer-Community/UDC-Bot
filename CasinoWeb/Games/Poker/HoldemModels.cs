using DiscordBot.Domain;

namespace CasinoWeb.Games.Poker;

public enum HoldemStreet
{
    Preflop,
    Flop,
    Turn,
    River,
    /// <summary>The hand is over: its cards were compared, or everyone else folded.</summary>
    Showdown,
}

public enum HoldemActionKind
{
    Fold,
    Check,
    Call,
    /// <summary>Opens the betting of a street; the amount is the bet.</summary>
    Bet,
    /// <summary>Raises an existing bet; the amount is the new total of the player's bet in this street.</summary>
    Raise,
    /// <summary>Puts every remaining token in; the amount is ignored.</summary>
    AllIn,
}

public enum HoldemRefusal
{
    None,
    HandOver,
    NotYourTurn,
    NotAllowed,
    AmountTooLow,
    AmountTooHigh,
}

/// <summary>A seat dealt into a hand.</summary>
/// <param name="Seat">The seat number, unique at the table.</param>
/// <param name="Stake">The most this player can commit to the hand: their balance when it starts, capped by the table.</param>
public sealed record HoldemSeat(int Seat, long Stake);

/// <summary>A player's state in a running hand.</summary>
public sealed class HoldemPlayer(int seat, long stake)
{
    public int Seat { get; } = seat;

    /// <summary>The most this player can commit to the hand; lowered by <see cref="HoldemHand.CapStake"/>.</summary>
    public long Stake { get; internal set; } = stake;

    /// <summary>Everything the player put in this hand, blinds included.</summary>
    public long Committed { get; internal set; }

    /// <summary>What the player put in during the current street.</summary>
    public long RoundCommitted { get; internal set; }

    public long Remaining => Stake - Committed;

    public bool Folded { get; internal set; }

    public bool IsAllIn => !Folded && Remaining == 0;

    public IReadOnlyList<Card> HoleCards { get; internal set; } = [];

    /// <summary>Acted since the betting was last opened or fully raised; a player who did cannot raise again after a short all-in.</summary>
    internal bool HasActed { get; set; }
}

/// <summary>What the player to act may do.</summary>
/// <param name="Kinds">The allowed actions; <see cref="HoldemActionKind.Fold"/> is always there.</param>
/// <param name="CallAmount">The tokens a call costs: 0 when the player can check, less than the bet when their stake is short.</param>
/// <param name="MinRaiseTo">The smallest bet or raise, as a total for the street; 0 when bet and raise are not allowed.</param>
/// <param name="MaxRaiseTo">The biggest bet or raise (all the player's remaining tokens), as a total for the street.</param>
/// <param name="AllInTo">The player's total for the street if they go all in.</param>
public sealed record HoldemOptions(
    int Seat,
    IReadOnlyList<HoldemActionKind> Kinds,
    long CallAmount,
    long MinRaiseTo,
    long MaxRaiseTo,
    long AllInTo);

public sealed record HoldemActResult(HoldemRefusal Refusal, HoldemOptions? Options)
{
    public bool Accepted => Refusal == HoldemRefusal.None;
}

/// <summary>A pot, the main one or a side pot, and the players who can win it.</summary>
public sealed record HoldemPot(long Amount, IReadOnlyList<int> EligibleSeats);

public sealed record HoldemPotResult(long Amount, IReadOnlyList<int> WinnerSeats);

/// <summary>A hand shown at showdown.</summary>
/// <param name="BestFive">The five cards that make the hand.</param>
public sealed record HoldemShownHand(int Seat, IReadOnlyList<Card> HoleCards, IReadOnlyList<Card> BestFive, PokerHandRank Rank, string Description);

/// <summary>How a hand ended.</summary>
/// <param name="Showdown">False when everyone else folded: nobody shows cards.</param>
/// <param name="Winnings">Per seat, the tokens to credit back (0 for a loser); they add up to everything committed.</param>
/// <param name="Shown">At showdown, the hands of the players still in, in seat order; empty otherwise.</param>
public sealed record HoldemResult(
    bool Showdown,
    IReadOnlyList<HoldemPotResult> Pots,
    IReadOnlyDictionary<int, long> Winnings,
    IReadOnlyList<HoldemShownHand> Shown);
