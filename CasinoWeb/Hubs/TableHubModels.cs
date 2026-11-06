using CasinoWeb.Games.Poker;
using CasinoWeb.Tables;

namespace CasinoWeb.Hubs;

/// <param name="Stakes">The presets a table can be created with.</param>
/// <param name="AllowAiSeats">Whether a seated player can add AI seats (dev only).</param>
/// <param name="Table">The table the member sits at, as they see it, so a reload goes back to it; <c>null</c> when they sit nowhere.</param>
public sealed record PokerInfo(IReadOnlyList<StakesDto> Stakes, bool AllowAiSeats, TableViewDto? Table)
{
    public static PokerInfo Create(bool allowAiSeats, TableView? seated) =>
        new([.. TableStakes.Presets.Select(StakesDto.From)], allowAiSeats, seated is null ? null : TableViewDto.From(seated));
}

/// <param name="MinBalance">The balance needed to sit and to be dealt into a hand.</param>
/// <param name="MaxCommit">The most a player puts in during one hand.</param>
public sealed record StakesDto(long SmallBlind, long BigBlind, long MinBalance, long MaxCommit)
{
    public static StakesDto From(TableStakes stakes) =>
        new(stakes.SmallBlind, stakes.BigBlind, stakes.BigBlind * TableRules.MinBigBlinds, stakes.BigBlind * TableRules.MaxBigBlinds);
}

public sealed record TableSummaryDto(int Id, StakesDto Stakes, int SeatsTaken, int MaxSeats, bool HandRunning)
{
    public static TableSummaryDto From(TableSummary table) =>
        new(table.Id, StakesDto.From(table.Stakes), table.SeatsTaken, table.MaxSeats, table.HandRunning);
}

/// <summary>A seat of the table. The member behind it is not named by id: the viewer's own seat is <see cref="TableViewDto.MySeat"/>.</summary>
public sealed record TableSeatDto(int Seat, string Name, bool IsAi, bool Away)
{
    public static TableSeatDto From(TableSeatView seat) => new(seat.Seat, seat.Name, seat.IsAi, seat.Away);
}

/// <param name="TurnSeat">The seat that must act, with <paramref name="TurnSecondsLeft"/> seconds to do it; <c>null</c> between hands.</param>
/// <param name="NextHandSecondsLeft">Between hands, the seconds until the next deal; <c>null</c> when it waits for players.</param>
/// <param name="MySeat">The viewer's seat; <c>null</c> for a watcher.</param>
public sealed record TableViewDto(
    int Id,
    StakesDto Stakes,
    int MaxSeats,
    IReadOnlyList<TableSeatDto> Seats,
    bool HandRunning,
    PokerHandDto? Hand,
    int? TurnSeat,
    int? TurnSecondsLeft,
    int? NextHandSecondsLeft,
    int? MySeat)
{
    public static TableViewDto From(TableView view) => new(
        view.Id,
        StakesDto.From(view.Stakes),
        view.MaxSeats,
        [.. view.Seats.Select(TableSeatDto.From)],
        view.HandRunning,
        view.Hand is PokerHandView hand ? PokerHandDto.From(hand) : null,
        view.TurnSeat,
        view.TurnSecondsLeft,
        view.NextHandSecondsLeft,
        view.MySeat);
}

/// <param name="Street"><c>preflop</c>, <c>flop</c>, <c>turn</c>, <c>river</c> or <c>showdown</c> (the hand is over).</param>
/// <param name="Pot">Everything in the middle, bets of the current street included.</param>
/// <param name="CurrentBet">The highest bet of the current street.</param>
/// <param name="Options">What the viewer may do; <c>null</c> unless it is their turn.</param>
/// <param name="Result">Set once the hand is over.</param>
public sealed record PokerHandDto(
    string Street,
    IReadOnlyList<CardDto> Board,
    long Pot,
    IReadOnlyList<PotDto> Pots,
    int ButtonSeat,
    int SmallBlindSeat,
    int BigBlindSeat,
    long CurrentBet,
    IReadOnlyList<PokerSeatDto> Seats,
    PokerOptionsDto? Options,
    PokerResultDto? Result)
{
    public static PokerHandDto From(PokerHandView hand) => new(
        TableWire.Name(hand.Street),
        [.. hand.Board.Select(CardDto.From)],
        hand.Pot,
        [.. hand.Pots.Select(p => new PotDto(p.Amount, p.EligibleSeats))],
        hand.ButtonSeat,
        hand.SmallBlindSeat,
        hand.BigBlindSeat,
        hand.CurrentBet,
        [.. hand.Seats.Select(PokerSeatDto.From)],
        hand.Options is null ? null : PokerOptionsDto.From(hand.Options),
        hand.Result is null ? null : PokerResultDto.From(hand.Result));
}

public sealed record PotDto(long Amount, IReadOnlyList<int> EligibleSeats);

/// <param name="Committed">What the seat put in during the whole hand.</param>
/// <param name="RoundBet">What the seat put in during the current street.</param>
/// <param name="HoleCards">The viewer's own cards, or those shown at showdown; <c>null</c> when hidden from this viewer.</param>
public sealed record PokerSeatDto(int Seat, long Committed, long RoundBet, bool Folded, bool AllIn, IReadOnlyList<CardDto>? HoleCards)
{
    public static PokerSeatDto From(PokerSeatView seat) => new(
        seat.Seat, seat.Committed, seat.RoundBet, seat.Folded, seat.AllIn, seat.HoleCards?.Select(CardDto.From).ToArray());
}

/// <param name="Actions">The allowed actions: <c>fold</c>, <c>check</c>, <c>call</c>, <c>bet</c>, <c>raise</c>, <c>allIn</c>.</param>
/// <param name="CallAmount">The tokens a call costs; 0 when the player can check.</param>
/// <param name="MinRaiseTo">The smallest bet or raise, as a total for the street; 0 when neither is allowed.</param>
/// <param name="MaxRaiseTo">The biggest bet or raise, as a total for the street.</param>
/// <param name="AllInTo">The player's total for the street if they go all in.</param>
public sealed record PokerOptionsDto(IReadOnlyList<string> Actions, long CallAmount, long MinRaiseTo, long MaxRaiseTo, long AllInTo)
{
    public static PokerOptionsDto From(HoldemOptions options) =>
        new([.. options.Kinds.Select(TableWire.Name)], options.CallAmount, options.MinRaiseTo, options.MaxRaiseTo, options.AllInTo);
}

/// <param name="Showdown">False when everyone else folded: nobody shows cards.</param>
/// <param name="Winnings">Per seat, the tokens credited back (0 for a loser).</param>
/// <param name="Shown">At showdown, the hands of the players still in.</param>
public sealed record PokerResultDto(bool Showdown, IReadOnlyList<PotResultDto> Pots, IReadOnlyList<SeatWinningDto> Winnings, IReadOnlyList<ShownHandDto> Shown)
{
    public static PokerResultDto From(HoldemResult result) => new(
        result.Showdown,
        [.. result.Pots.Select(p => new PotResultDto(p.Amount, p.WinnerSeats))],
        [.. result.Winnings.OrderBy(w => w.Key).Select(w => new SeatWinningDto(w.Key, w.Value))],
        [.. result.Shown.Select(s => new ShownHandDto(s.Seat, [.. s.HoleCards.Select(CardDto.From)], [.. s.BestFive.Select(CardDto.From)], s.Description))]);
}

public sealed record PotResultDto(long Amount, IReadOnlyList<int> WinnerSeats);

public sealed record SeatWinningDto(int Seat, long Amount);

/// <param name="BestFive">The five cards that make the hand.</param>
/// <param name="Description">The hand in words, such as "Pair of Aces".</param>
public sealed record ShownHandDto(int Seat, IReadOnlyList<CardDto> HoleCards, IReadOnlyList<CardDto> BestFive, string Description);

/// <summary>The answer to a table call. <see cref="View"/> is the table after the call, from the caller's side.</summary>
/// <param name="Status"><c>ok</c>, or why not: <c>noSuchTable</c>, <c>noSuchSeat</c>, <c>seatTaken</c>, <c>tableFull</c>, <c>alreadySeated</c>, <c>notSeated</c>, <c>belowMinimum</c>, <c>tooManyTables</c>, <c>badStakes</c>, <c>aiNotAllowed</c>, <c>noAiSeat</c>, <c>closed</c>, <c>refused</c> (a game action the rules refuse) or <c>failed</c>.</param>
/// <param name="Refusal">For <c>refused</c>: <c>handOver</c>, <c>notYourTurn</c>, <c>notAllowed</c>, <c>amountTooLow</c>, <c>amountTooHigh</c> or <c>insufficientBalance</c> (the balance fell short and the stake was lowered); otherwise <c>null</c>.</param>
public sealed record TableCallDto(string Status, string? Refusal, TableViewDto? View)
{
    public static TableCallDto From(TableResult result) => new(
        TableWire.Name(result.Status),
        result.Refusal == TableRefusal.None ? null : TableWire.Name(result.Refusal),
        result.View is null ? null : TableViewDto.From(result.View));
}

/// <summary>Pushed as <c>TableUpdated</c>. <see cref="View"/> is <c>null</c> when the table closed or the member is no longer at it.</summary>
public sealed record TableUpdateDto(int TableId, TableViewDto? View);

/// <summary>The words the client reads: the C# enum names with a lower-case first letter.</summary>
internal static class TableWire
{
    public static string Name<T>(T value) where T : struct, Enum
    {
        var name = value.ToString();
        return char.ToLowerInvariant(name[0]) + name[1..];
    }
}
