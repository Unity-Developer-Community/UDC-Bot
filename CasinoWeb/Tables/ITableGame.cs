namespace CasinoWeb.Tables;

/// <summary>
/// The rules of the game played at one <see cref="Table"/>, with no tokens, clock or users: the table decides who sits,
/// whose balance pays and when time runs out. One instance per table, used under the table's gate.
/// </summary>
public interface ITableGame
{
    /// <summary>The game name recorded on each player's transaction.</summary>
    string Name { get; }

    int MaxSeats { get; }

    /// <summary>The balance a player needs to sit and to be dealt into a hand.</summary>
    long MinBalance { get; }

    /// <summary>The most a player commits to one hand, and the stake of an AI seat.</summary>
    long MaxStake { get; }

    bool IsRunning { get; }

    int? SeatToAct { get; }

    /// <summary>What each seat is credited back once the hand is over; <c>null</c> while it runs or before the first one.</summary>
    IReadOnlyDictionary<int, long>? Winnings { get; }

    /// <summary>Deals a hand. Returns what each seat must pay up front (the blinds); the table takes it and calls <see cref="Cancel"/> if it cannot.</summary>
    IReadOnlyDictionary<int, long> Deal(IReadOnlyList<DealtSeat> seats);

    /// <summary>Undoes the last <see cref="Deal"/>.</summary>
    void Cancel();

    /// <summary>What the action would cost, or why the rules refuse it. Changes nothing.</summary>
    ActionQuote Price(int seat, TableAction action);

    /// <summary>Applies an action that <see cref="Price"/> allowed.</summary>
    bool Act(int seat, TableAction action);

    /// <summary>What happens for a player who let their turn run out: check, or fold when they cannot.</summary>
    TableAction TimeoutAction(int seat);

    /// <summary>What an AI seat plays: check, or call when it cannot.</summary>
    TableAction AiAction(int seat);

    /// <summary>A player leaves: their hand folds, even out of turn.</summary>
    bool Forfeit(int seat);

    /// <summary>The player can put in only <paramref name="tokensLeft"/> more tokens (their balance changed under them).</summary>
    void LimitStake(int seat, long tokensLeft);

    /// <summary>The hand as the viewer at <paramref name="viewerSeat"/> (or a watcher, when <c>null</c>) may see it.</summary>
    HandView? View(int? viewerSeat);
}

/// <summary>Where the table system tells connected clients what changed. The hub implements it.</summary>
public interface ITableNotifier
{
    /// <summary>The whole lobby, after a table opened, closed or changed its seats or hand state.</summary>
    Task TablesChangedAsync(IReadOnlyList<TableSummary> tables);

    /// <summary>A table changed: each seated user and watcher gets their own view, <c>null</c> when the table closed or they are no longer at it.</summary>
    Task TableUpdatedAsync(int tableId, IReadOnlyDictionary<ulong, TableView?> views);
}

public sealed class NullTableNotifier : ITableNotifier
{
    public Task TablesChangedAsync(IReadOnlyList<TableSummary> tables) => Task.CompletedTask;

    public Task TableUpdatedAsync(int tableId, IReadOnlyDictionary<ulong, TableView?> views) => Task.CompletedTask;
}
