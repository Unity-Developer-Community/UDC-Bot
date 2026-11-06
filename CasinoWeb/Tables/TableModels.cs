using CasinoWeb.Games.Poker;
using DiscordBot.Domain;

namespace CasinoWeb.Tables;

/// <summary>The fixed numbers of the poker tables.</summary>
public static class TableRules
{
    public const int MaxTables = 20;
    public const int Seats = 6;

    /// <summary>A player needs this many big blinds to sit and to be dealt into a hand.</summary>
    public const int MinBigBlinds = 10;

    /// <summary>The most a player commits to one hand, in big blinds (their balance when the hand starts, if lower).</summary>
    public const int MaxBigBlinds = 100;

    public const int MaxMissedTurns = 2;
    public static readonly TimeSpan TurnTime = TimeSpan.FromSeconds(30);

    /// <summary>How long a seat is kept for a player whose connections all dropped.</summary>
    public static readonly TimeSpan Grace = TimeSpan.FromSeconds(60);

    /// <summary>Between the end of a hand and the next deal, so the result can be read.</summary>
    public static readonly TimeSpan BetweenHands = TimeSpan.FromSeconds(5);
}

/// <summary>The blinds of a table.</summary>
public sealed record TableStakes(long SmallBlind, long BigBlind)
{
    public static IReadOnlyList<TableStakes> Presets { get; } = [new(1, 2), new(5, 10), new(25, 50)];

    public static TableStakes? FromBigBlind(long bigBlind) => Presets.FirstOrDefault(p => p.BigBlind == bigBlind);
}

public enum TableStatus
{
    Ok,
    NoSuchTable,
    NoSuchSeat,
    SeatTaken,
    TableFull,
    AlreadySeated,
    NotSeated,

    /// <summary>The balance is under 10 big blinds.</summary>
    BelowMinimum,
    TooManyTables,
    BadStakes,
    AiNotAllowed,

    /// <summary>There is no AI seat to remove.</summary>
    NoAiSeat,

    /// <summary>The server is stopping.</summary>
    Closed,

    /// <summary>A game action was refused; <see cref="TableResult.Refusal"/> says why and the view carries the allowed options.</summary>
    Refused,
    Failed,
}

public enum TableRefusal
{
    None,
    HandOver,
    NotYourTurn,
    NotAllowed,
    AmountTooLow,
    AmountTooHigh,

    /// <summary>The balance no longer covers the action: the stake was lowered to what is left.</summary>
    InsufficientBalance,
}

/// <param name="Kind"><c>fold</c>, <c>check</c>, <c>call</c>, <c>bet</c>, <c>raise</c> or <c>allIn</c>.</param>
/// <param name="Amount">For a bet or raise, the total of the player's bet in the street.</param>
public sealed record TableAction(string Kind, long Amount = 0);

/// <summary>What an action would cost and whether the rules allow it now.</summary>
public sealed record ActionQuote(TableRefusal Refusal, long Cost)
{
    public bool Allowed => Refusal == TableRefusal.None;
}

/// <summary>A seat dealt into a hand with the most it can commit.</summary>
public sealed record DealtSeat(int Seat, long Stake);

/// <summary>A row of the lobby.</summary>
public sealed record TableSummary(int Id, TableStakes Stakes, int SeatsTaken, int MaxSeats, bool HandRunning);

public sealed record TableSeatView(int Seat, ulong? UserId, string Name, bool IsAi, bool Away);

/// <summary>What one viewer may know of the hand of a table. The shape depends on the game.</summary>
public abstract record HandView;

/// <param name="HoleCards">The viewer's own cards, or those shown at showdown; <c>null</c> when hidden from this viewer.</param>
public sealed record PokerSeatView(int Seat, long Committed, long RoundBet, bool Folded, bool AllIn, IReadOnlyList<Card>? HoleCards);

/// <param name="Options">What the viewer may do; <c>null</c> unless it is their turn.</param>
/// <param name="Result">Set once the hand is over; it holds the hands shown at showdown.</param>
public sealed record PokerHandView(
    HoldemStreet Street,
    IReadOnlyList<Card> Board,
    long Pot,
    IReadOnlyList<HoldemPot> Pots,
    int ButtonSeat,
    int SmallBlindSeat,
    int BigBlindSeat,
    long CurrentBet,
    IReadOnlyList<PokerSeatView> Seats,
    HoldemOptions? Options,
    HoldemResult? Result) : HandView;

/// <summary>A table as one viewer sees it.</summary>
/// <param name="TurnSeat">The seat that must act, with <paramref name="TurnSecondsLeft"/> seconds to do it; <c>null</c> between hands.</param>
/// <param name="NextHandSecondsLeft">Between hands, the seconds until the next deal; <c>null</c> when it waits for players.</param>
/// <param name="MySeat">The viewer's seat; <c>null</c> for a watcher.</param>
public sealed record TableView(
    int Id,
    TableStakes Stakes,
    int MaxSeats,
    IReadOnlyList<TableSeatView> Seats,
    bool HandRunning,
    HandView? Hand,
    int? TurnSeat,
    int? TurnSecondsLeft,
    int? NextHandSecondsLeft,
    int? MySeat);

/// <param name="View">The table after the call, from the caller's side; <c>null</c> when the caller is no longer at a table.</param>
public sealed record TableResult(TableStatus Status, TableView? View = null, TableRefusal Refusal = TableRefusal.None)
{
    public bool Ok => Status == TableStatus.Ok;
}
