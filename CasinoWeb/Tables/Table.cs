using DiscordBot.Services.Logging;

namespace CasinoWeb.Tables;

/// <summary>
/// One table: its seats, watchers, the hand being played and the clocks around it. Every change runs through one gate, so
/// two calls can never act on the same hand at once, and the pushes to clients leave in the order the changes happened.
/// Time moves only when <see cref="TickAsync"/> is called, so tests drive it with a fake clock.
/// </summary>
/// <param name="seatReleased">Called, inside the gate, when a user's seat goes away for any reason.</param>
public sealed class Table(
    int id,
    TableStakes stakes,
    ITableGame game,
    TableLedger ledger,
    TimeProvider time,
    ITableNotifier notifier,
    ILoggingService logging,
    Action<ulong> seatReleased)
{
    private const string Log = "WebPoker";

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly TableSeat?[] _seats = new TableSeat?[game.MaxSeats];
    private readonly HashSet<ulong> _watchers = [];
    private readonly List<ulong> _removed = [];
    private ActiveHand? _hand;
    private int _handNumber;
    private int _aiCount;
    private int? _turnSeat;
    private DateTimeOffset? _turnDeadline;
    private DateTimeOffset? _nextHandAt;
    private volatile bool _closed;
    private volatile TableSummary _summary = new(id, stakes, 0, game.MaxSeats, false);

    public int Id => id;

    public TableStakes Stakes => stakes;

    public bool Closed => _closed;

    public TableSummary Summary => _summary;

    /// <summary>The summary last sent to the lobby; owned by <see cref="TableService"/>.</summary>
    internal TableSummary? PublishedSummary { get; set; }

    #region Seats

    public Task<TableResult> SitAsync(ulong userId, string name, int? seatNumber) => RunAsync(async () =>
    {
        if (Closed)
        {
            return new TableResult(TableStatus.NoSuchTable);
        }

        var free = Enumerable.Range(0, _seats.Length).Where(IsFree).ToList();
        if (free.Count == 0)
        {
            return new TableResult(TableStatus.TableFull);
        }

        var seat = free[0];
        if (seatNumber is { } wanted)
        {
            if (wanted < 0 || wanted >= _seats.Length)
            {
                return new TableResult(TableStatus.NoSuchSeat);
            }

            if (!free.Contains(wanted))
            {
                return new TableResult(TableStatus.SeatTaken);
            }

            seat = wanted;
        }

        try
        {
            if (await ledger.GetBalanceAsync(userId) < game.MinBalance)
            {
                return new TableResult(TableStatus.BelowMinimum);
            }
        }
        catch (Exception ex)
        {
            await logging.LogChannelAndFile($"{Log}: ERROR reading the balance of user {userId} to sit at table {id}: {ex.Message}", ExtendedLogSeverity.Error);
            return new TableResult(TableStatus.Failed);
        }

        _seats[seat] = new TableSeat(seat, userId, name);
        await PushAsync();
        return new TableResult(TableStatus.Ok, BuildView(userId));
    });

    /// <summary>The player leaves; a hand they are in folds for them and what they put in stays in the pot.</summary>
    public Task<TableResult> LeaveAsync(ulong userId) => RunAsync(async () =>
    {
        if (SeatOf(userId) is not { } seat)
        {
            return new TableResult(TableStatus.NotSeated);
        }

        RemoveSeat(seat);
        await AfterChangeAsync(resetClock: false);
        await PushAsync();
        return new TableResult(TableStatus.Ok);
    });

    public Task<TableResult> AddAiAsync(ulong userId) => RunAsync(async () =>
    {
        if (Closed)
        {
            return new TableResult(TableStatus.NoSuchTable);
        }

        var free = Enumerable.Range(0, _seats.Length).Where(IsFree).ToList();
        if (free.Count == 0)
        {
            return new TableResult(TableStatus.TableFull);
        }

        _aiCount++;
        _seats[free[0]] = new TableSeat(free[0], null, $"AI {_aiCount}");
        await PushAsync();
        return new TableResult(TableStatus.Ok, BuildView(userId));
    });

    public Task<TableResult> RemoveAiAsync(ulong userId) => RunAsync(async () =>
    {
        if (Closed || _seats.LastOrDefault(s => s is { IsAi: true }) is not { } seat)
        {
            return new TableResult(TableStatus.NoAiSeat);
        }

        RemoveSeat(seat);
        await AfterChangeAsync(resetClock: false);
        await PushAsync();
        return new TableResult(TableStatus.Ok, BuildView(userId));
    });

    /// <summary>A user's connections all dropped (<paramref name="away"/>) or one came back.</summary>
    public Task SetAwayAsync(ulong userId, bool away) => RunAsync(async () =>
    {
        if (SeatOf(userId) is not { } seat || (seat.AwayAt is not null) == away)
        {
            return;
        }

        seat.AwayAt = away ? time.GetUtcNow() : null;
        await PushAsync();
    });

    public Task<TableResult> WatchAsync(ulong userId) => RunAsync(() =>
    {
        if (Closed)
        {
            return Task.FromResult(new TableResult(TableStatus.NoSuchTable));
        }

        _watchers.Add(userId);
        return Task.FromResult(new TableResult(TableStatus.Ok, BuildView(userId)));
    });

    public Task UnwatchAsync(ulong userId) => RunAsync(() =>
    {
        _watchers.Remove(userId);
        return Task.CompletedTask;
    });

    public Task<TableView> ViewAsync(ulong userId) => RunAsync(() => Task.FromResult(BuildView(userId)));

    #endregion

    #region Playing

    public Task<TableResult> ActAsync(ulong userId, TableAction action) => RunAsync(async () =>
    {
        if (SeatOf(userId) is not { } seat)
        {
            return new TableResult(TableStatus.NotSeated);
        }

        var quote = game.Price(seat.Number, action);
        if (!quote.Allowed)
        {
            return new TableResult(TableStatus.Refused, BuildView(userId), quote.Refusal);
        }

        var hand = _hand!;
        if (quote.Cost > 0)
        {
            switch (await ledger.TryTakeAsync(userId, quote.Cost, Where(hand.Number)))
            {
                case TakeResult.Insufficient:
                    return await LowerStakeAsync(seat);
                case TakeResult.Failed:
                    return new TableResult(TableStatus.Failed, BuildView(userId));
            }
        }

        if (!game.Act(seat.Number, action))
        {
            // The rules allowed it a moment ago: the tokens are given back so nothing is lost to a bug.
            if (quote.Cost > 0)
            {
                await ledger.GiveBackAsync(userId, quote.Cost, Where(hand.Number));
            }

            await logging.LogChannelAndFile($"{Log}: ERROR {Where(hand.Number)}: the rules refused {action.Kind} for user {userId} after pricing it", ExtendedLogSeverity.Error);
            return new TableResult(TableStatus.Failed, BuildView(userId));
        }

        hand.Participants[seat.Number].Reserved += quote.Cost;
        seat.MissedTurns = 0;
        await AfterChangeAsync(resetClock: true);
        await PushAsync();
        return new TableResult(TableStatus.Ok, BuildView(userId));
    });

    /// <summary>The balance no longer covers the action: the player's stake shrinks to what is left and the new options go back to them.</summary>
    private async Task<TableResult> LowerStakeAsync(TableSeat seat)
    {
        var userId = seat.UserId!.Value;
        long balance;
        try
        {
            balance = await ledger.GetBalanceAsync(userId);
        }
        catch (Exception ex)
        {
            await logging.LogChannelAndFile($"{Log}: ERROR reading the balance of user {userId}, table {id}: {ex.Message}", ExtendedLogSeverity.Error);
            return new TableResult(TableStatus.Failed, BuildView(userId));
        }

        game.LimitStake(seat.Number, balance);
        await logging.LogChannelAndFile($"{Log}: user {userId} cannot cover the action, {Where(_hand!.Number)}: stake lowered to {balance} more tokens");
        await AfterChangeAsync(resetClock: false);
        await PushAsync();
        return new TableResult(TableStatus.Refused, BuildView(userId), TableRefusal.InsufficientBalance);
    }

    /// <summary>
    /// Moves time forward: stands up players whose grace ran out, plays an AI seat or a missed turn (one move per call),
    /// and deals the next hand once the pause after the last one is over.
    /// </summary>
    /// <param name="casinoOpen">False while the casino is closed: nothing new is dealt.</param>
    public Task TickAsync(bool casinoOpen) => RunAsync(async () =>
    {
        if (Closed)
        {
            return;
        }

        var now = time.GetUtcNow();
        var changed = false;
        foreach (var seat in _seats.ToArray())
        {
            if (seat is { UserId: not null, AwayAt: { } away } && now - away >= TableRules.Grace)
            {
                RemoveSeat(seat);
                changed = true;
            }
        }

        if (changed)
        {
            await AfterChangeAsync(resetClock: false);
            if (Closed)
            {
                await PushAsync();
                return;
            }
        }

        var acted = false;
        if (game.IsRunning && game.SeatToAct is { } turn)
        {
            var actor = _seats[turn];
            if (actor is null)
            {
                game.Forfeit(turn);
                acted = true;
            }
            else if (actor.IsAi)
            {
                game.Act(turn, game.AiAction(turn));
                acted = true;
            }
            else if (_turnDeadline <= now)
            {
                game.Act(turn, game.TimeoutAction(turn));
                actor.MissedTurns++;
                if (actor.MissedTurns >= TableRules.MaxMissedTurns)
                {
                    actor.StandUpAfterHand = true;
                }

                acted = true;
            }
        }
        else if (!game.IsRunning && casinoOpen && (_nextHandAt is null || now >= _nextHandAt))
        {
            var before = _handNumber;
            await StartHandAsync();
            acted = _handNumber != before;
        }

        if (changed || acted)
        {
            await AfterChangeAsync(resetClock: acted);
            await PushAsync();
        }
    });

    private async Task StartHandAsync()
    {
        var candidates = new List<(TableSeat Seat, long Stake)>();
        foreach (var seat in _seats)
        {
            if (seat is null)
            {
                continue;
            }

            if (seat.IsAi)
            {
                candidates.Add((seat, game.MaxStake));
            }
            else if (seat.AwayAt is null && !seat.StandUpAfterHand)
            {
                long balance;
                try
                {
                    balance = await ledger.GetBalanceAsync(seat.UserId!.Value);
                }
                catch (Exception ex)
                {
                    await logging.LogChannelAndFile($"{Log}: ERROR reading the balance of user {seat.UserId}, table {id}: {ex.Message}", ExtendedLogSeverity.Error);
                    return;
                }

                if (balance >= game.MinBalance)
                {
                    candidates.Add((seat, Math.Min(balance, game.MaxStake)));
                }
            }
        }

        var number = _handNumber + 1;
        var where = Where(number);
        while (candidates.Count >= 2)
        {
            var debits = game.Deal([.. candidates.Select(c => new DealtSeat(c.Seat.Number, c.Stake))]);
            var taken = new List<(ulong UserId, int Seat, long Amount)>();
            TableSeat? failed = null;
            foreach (var (seatNumber, amount) in debits)
            {
                var seat = _seats[seatNumber]!;
                if (seat.IsAi)
                {
                    continue;
                }

                if (await ledger.TryTakeAsync(seat.UserId!.Value, amount, where) != TakeResult.Taken)
                {
                    failed = seat;
                    break;
                }

                taken.Add((seat.UserId.Value, seatNumber, amount));
            }

            if (failed is not null)
            {
                // A blind the balance can no longer cover (spent in Discord meanwhile): that player sits this hand out.
                game.Cancel();
                foreach (var (userId, _, amount) in taken)
                {
                    await ledger.GiveBackAsync(userId, amount, where);
                }

                candidates.RemoveAll(c => c.Seat == failed);
                continue;
            }

            var hand = new ActiveHand(number);
            foreach (var (seat, _) in candidates)
            {
                hand.Participants[seat.Number] = new Participant(seat.Number, seat.UserId);
            }

            foreach (var (_, seatNumber, amount) in taken)
            {
                hand.Participants[seatNumber].Reserved = amount;
            }

            _hand = hand;
            _handNumber = number;
            _nextHandAt = null;
            _turnSeat = null;
            return;
        }
    }

    /// <summary>Runs after every change: settles a finished hand, keeps the turn clock and closes an abandoned table.</summary>
    private async Task AfterChangeAsync(bool resetClock)
    {
        if (!HasHuman && game.IsRunning)
        {
            FinishWithAiOnly();
        }

        if (_hand is { Settled: false } hand && !game.IsRunning)
        {
            await SettleAsync(hand);
        }

        if (game.IsRunning)
        {
            var seat = game.SeatToAct;
            if (resetClock || seat != _turnSeat)
            {
                _turnSeat = seat;
                _turnDeadline = time.GetUtcNow() + TableRules.TurnTime;
            }
        }

        if (!HasHuman && !Closed)
        {
            Close();
        }
    }

    /// <summary>Nobody human is left to play: the AI seats check and call the hand down, so what the leavers put in is settled.</summary>
    private void FinishWithAiOnly()
    {
        for (var guard = 0; guard < 1000 && game.IsRunning; guard++)
        {
            var seat = game.SeatToAct!.Value;
            if (_seats[seat] is not { IsAi: true })
            {
                game.Forfeit(seat);
            }
            else
            {
                game.Act(seat, game.AiAction(seat));
            }
        }
    }

    /// <summary>Credits each human player their winnings and records the net result of everyone who put tokens in.</summary>
    private async Task SettleAsync(ActiveHand hand)
    {
        hand.Settled = true;
        var winnings = game.Winnings;
        foreach (var participant in hand.Participants.Values)
        {
            if (participant.UserId is { } userId && participant.Reserved > 0)
            {
                await ledger.SettleAsync(userId, winnings?.GetValueOrDefault(participant.Seat) ?? 0, participant.Reserved, game.Name, Where(hand.Number));
            }
        }

        _turnSeat = null;
        _turnDeadline = null;
        _nextHandAt = time.GetUtcNow() + TableRules.BetweenHands;

        foreach (var seat in _seats.ToArray())
        {
            if (seat is { StandUpAfterHand: true })
            {
                RemoveSeat(seat);
            }
        }
    }

    #endregion

    /// <summary>The server is stopping: tokens committed to an unfinished hand go back and the table closes.</summary>
    public Task StopAsync() => RunAsync(async () =>
    {
        if (_hand is { Settled: false } hand && game.IsRunning)
        {
            hand.Settled = true;
            foreach (var participant in hand.Participants.Values)
            {
                if (participant.UserId is { } userId && participant.Reserved > 0)
                {
                    await ledger.GiveBackAsync(userId, participant.Reserved, Where(hand.Number));
                }
            }
        }

        if (!Closed)
        {
            Close();
        }

        await PushAsync();
    });

    #region Helpers

    private bool HasHuman => _seats.Any(s => s?.UserId is not null);

    // A seat of the running hand stays reserved after its player left, so its cards are never shown to someone who sits there.
    private bool IsFree(int seat) => _seats[seat] is null && !(game.IsRunning && _hand!.Participants.ContainsKey(seat));

    private TableSeat? SeatOf(ulong userId) => Array.Find(_seats, s => s?.UserId == userId);

    private string Where(int handNumber) => $"table {id}, hand {handNumber}";

    private void RemoveSeat(TableSeat seat)
    {
        _seats[seat.Number] = null;
        if (seat.UserId is { } userId)
        {
            _removed.Add(userId);
            seatReleased(userId);
        }

        if (game.IsRunning && _hand!.Participants.ContainsKey(seat.Number))
        {
            game.Forfeit(seat.Number);
        }
    }

    private void Close()
    {
        _closed = true;
        foreach (var seat in _seats.ToArray())
        {
            if (seat is null)
            {
                continue;
            }

            _seats[seat.Number] = null;
            if (seat.UserId is { } userId)
            {
                _removed.Add(userId);
                seatReleased(userId);
            }
        }
    }

    private async Task PushAsync()
    {
        _summary = new TableSummary(id, stakes, _seats.Count(s => s is not null), game.MaxSeats, game.IsRunning);

        var views = new Dictionary<ulong, TableView?>();
        foreach (var userId in _removed)
        {
            views[userId] = null;
        }

        _removed.Clear();
        if (Closed)
        {
            foreach (var watcher in _watchers)
            {
                views[watcher] = null;
            }

            _watchers.Clear();
        }
        else
        {
            foreach (var userId in _watchers)
            {
                views[userId] = BuildView(userId);
            }

            foreach (var seat in _seats)
            {
                if (seat?.UserId is { } userId)
                {
                    views[userId] = BuildView(userId);
                }
            }
        }

        if (views.Count == 0)
        {
            return;
        }

        try
        {
            await notifier.TableUpdatedAsync(id, views);
        }
        catch (Exception ex)
        {
            // A client that cannot be reached must not stop the game.
            await logging.LogChannelAndFile($"{Log}: ERROR pushing table {id}: {ex.Message}", ExtendedLogSeverity.Error);
        }
    }

    private TableView BuildView(ulong viewer)
    {
        var mine = SeatOf(viewer);
        // The cards are the viewer's own only when the same user was dealt that seat.
        int? handSeat = mine is not null && _hand is { } hand && hand.Participants.TryGetValue(mine.Number, out var participant) && participant.UserId == viewer
            ? mine.Number
            : null;
        var now = time.GetUtcNow();
        int? SecondsUntil(DateTimeOffset? at) => at is null ? null : (int)Math.Max(0, Math.Ceiling((at.Value - now).TotalSeconds));

        var seats = _seats.OfType<TableSeat>()
            .Select(s => new TableSeatView(s.Number, s.UserId, s.Name, s.IsAi, s.AwayAt is not null))
            .ToList();
        return new TableView(
            id,
            stakes,
            game.MaxSeats,
            seats,
            game.IsRunning,
            game.View(handSeat),
            game.IsRunning ? _turnSeat : null,
            game.IsRunning ? SecondsUntil(_turnDeadline) : null,
            game.IsRunning ? null : SecondsUntil(_nextHandAt),
            mine?.Number);
    }

    private async Task<T> RunAsync<T>(Func<Task<T>> work)
    {
        await _gate.WaitAsync();
        try
        {
            return await work();
        }
        finally
        {
            _gate.Release();
        }
    }

    private Task RunAsync(Func<Task> work) => RunAsync(async () =>
    {
        await work();
        return true;
    });

    #endregion

    private sealed class TableSeat(int number, ulong? userId, string name)
    {
        public int Number { get; } = number;

        /// <summary><c>null</c> for an AI seat.</summary>
        public ulong? UserId { get; } = userId;

        public string Name { get; } = name;

        public bool IsAi => UserId is null;

        public DateTimeOffset? AwayAt { get; set; }

        public int MissedTurns { get; set; }

        public bool StandUpAfterHand { get; set; }
    }

    private sealed class ActiveHand(int number)
    {
        public int Number { get; } = number;

        public Dictionary<int, Participant> Participants { get; } = [];

        public bool Settled { get; set; }
    }

    private sealed class Participant(int seat, ulong? userId)
    {
        public int Seat { get; } = seat;

        public ulong? UserId { get; } = userId;

        /// <summary>The tokens taken from the balance for this hand.</summary>
        public long Reserved { get; set; }
    }
}
