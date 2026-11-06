using System.Collections.Concurrent;
using CasinoWeb.Configuration;
using DiscordBot.Services.Fun.Casino;
using DiscordBot.Services.Logging;
using DiscordBot.Settings;
using Microsoft.Extensions.Options;

namespace CasinoWeb.Tables;

/// <summary>
/// The registry of the open tables: lobby summaries, one seat per member, at most <see cref="TableRules.MaxTables"/> tables.
/// The rules of what happens at a table live in <see cref="Table"/>; this class finds the table of a user and tells clients
/// when the lobby changed.
/// </summary>
public sealed class TableService(
    TableLedger ledger,
    BotSettings settings,
    IOptions<CasinoHostOptions> options,
    TimeProvider time,
    ILoggingService logging,
    PresenceTracker presence,
    ITableNotifier notifier,
    Func<TableStakes, ITableGame> newGame)
{
    private readonly ConcurrentDictionary<int, Table> _tables = new();

    // One seat per member: claimed before the table is asked, so two calls at once cannot seat the same member twice.
    private readonly ConcurrentDictionary<ulong, int> _seatOf = new();
    private readonly ConcurrentDictionary<ulong, int> _watching = new();
    private readonly object _registry = new();
    private readonly SemaphoreSlim _listGate = new(1, 1);
    private int _nextId;
    private volatile bool _stopped;
    private bool _listDirty;

    public bool AllowAiSeats => options.Value.AllowAiSeats;

    public IReadOnlyList<TableSummary> List() => [.. _tables.Values.OrderBy(t => t.Id).Select(t => t.Summary)];

    public async Task<TableResult> CreateAsync(ulong userId, string name, long bigBlind)
    {
        if (_stopped)
        {
            return new TableResult(TableStatus.Closed);
        }

        if (TableStakes.FromBigBlind(bigBlind) is not { } stakes)
        {
            return new TableResult(TableStatus.BadStakes);
        }

        if (!_seatOf.TryAdd(userId, -1))
        {
            return new TableResult(TableStatus.AlreadySeated);
        }

        Table table;
        lock (_registry)
        {
            if (_tables.Count >= TableRules.MaxTables)
            {
                _seatOf.TryRemove(new KeyValuePair<ulong, int>(userId, -1));
                return new TableResult(TableStatus.TooManyTables);
            }

            var id = ++_nextId;
            table = new Table(id, stakes, newGame(stakes), ledger, time, notifier, logging, user => _seatOf.TryRemove(new KeyValuePair<ulong, int>(user, id)));
            _tables[id] = table;
            _seatOf[userId] = id;
        }

        var result = await table.SitAsync(userId, name, 0);
        if (!result.Ok)
        {
            lock (_registry)
            {
                _tables.TryRemove(table.Id, out _);
            }

            _seatOf.TryRemove(new KeyValuePair<ulong, int>(userId, table.Id));
            return result;
        }

        await PublishListAsync();
        return result;
    }

    public async Task<TableResult> SitAsync(ulong userId, string name, int tableId, int? seat)
    {
        if (_stopped)
        {
            return new TableResult(TableStatus.Closed);
        }

        if (!_tables.TryGetValue(tableId, out var table))
        {
            return new TableResult(TableStatus.NoSuchTable);
        }

        if (!_seatOf.TryAdd(userId, tableId))
        {
            return new TableResult(TableStatus.AlreadySeated);
        }

        var result = await table.SitAsync(userId, name, seat);
        if (!result.Ok)
        {
            _seatOf.TryRemove(new KeyValuePair<ulong, int>(userId, tableId));
        }

        await AfterAsync(table);
        return result;
    }

    public async Task<TableResult> LeaveAsync(ulong userId)
    {
        if (SeatedTable(userId) is not { } table)
        {
            return new TableResult(TableStatus.NotSeated);
        }

        var result = await table.LeaveAsync(userId);
        await AfterAsync(table);
        return result;
    }

    public async Task<TableResult> ActAsync(ulong userId, string kind, long amount = 0)
    {
        if (SeatedTable(userId) is not { } table)
        {
            return new TableResult(TableStatus.NotSeated);
        }

        var result = await table.ActAsync(userId, new TableAction(kind, amount));
        await AfterAsync(table);
        return result;
    }

    public async Task<TableResult> AddAiAsync(ulong userId)
    {
        if (!AllowAiSeats)
        {
            return new TableResult(TableStatus.AiNotAllowed);
        }

        if (SeatedTable(userId) is not { } table)
        {
            return new TableResult(TableStatus.NotSeated);
        }

        var result = await table.AddAiAsync(userId);
        await AfterAsync(table);
        return result;
    }

    public async Task<TableResult> RemoveAiAsync(ulong userId)
    {
        if (!AllowAiSeats)
        {
            return new TableResult(TableStatus.AiNotAllowed);
        }

        if (SeatedTable(userId) is not { } table)
        {
            return new TableResult(TableStatus.NotSeated);
        }

        var result = await table.RemoveAiAsync(userId);
        await AfterAsync(table);
        return result;
    }

    /// <summary>The table the user sits at, as they see it, so a reload goes back to it; <c>null</c> when they sit nowhere.</summary>
    public async Task<TableView?> GetSeatedViewAsync(ulong userId) =>
        SeatedTable(userId) is { } table ? await table.ViewAsync(userId) : null;

    /// <summary>Follows a table with public information only. A member who sits somewhere can only follow that table.</summary>
    public async Task<TableResult> WatchAsync(ulong userId, int tableId)
    {
        if (_seatOf.TryGetValue(userId, out var seatedAt) && seatedAt != tableId)
        {
            return new TableResult(TableStatus.AlreadySeated);
        }

        if (!_tables.TryGetValue(tableId, out var table))
        {
            return new TableResult(TableStatus.NoSuchTable);
        }

        await UnwatchAsync(userId);
        var result = await table.WatchAsync(userId);
        if (result.Ok)
        {
            _watching[userId] = tableId;
        }

        return result;
    }

    public async Task UnwatchAsync(ulong userId)
    {
        if (_watching.TryRemove(userId, out var tableId) && _tables.TryGetValue(tableId, out var table))
        {
            await table.UnwatchAsync(userId);
        }
    }

    #region Presence

    /// <summary>Call when a user opens a connection; the first one brings a seat back from away.</summary>
    public async Task UserConnectedAsync(ulong userId)
    {
        if (presence.Connected(userId) && SeatedTable(userId) is { } table)
        {
            await table.SetAwayAsync(userId, false);
            await AfterAsync(table);
        }
    }

    /// <summary>Call when a connection closes; the last one marks the seat away, kept for <see cref="TableRules.Grace"/>.</summary>
    public async Task UserDisconnectedAsync(ulong userId)
    {
        if (!presence.Disconnected(userId))
        {
            return;
        }

        await UnwatchAsync(userId);
        if (SeatedTable(userId) is { } table)
        {
            await table.SetAwayAsync(userId, true);
            await AfterAsync(table);
        }
    }

    #endregion

    #region Time and stopping

    /// <summary>Moves every table's clocks. Called every second by <see cref="TablesSweeper"/>.</summary>
    public async Task TickAsync()
    {
        var open = settings.Casino.Enabled && !_stopped;
        foreach (var table in _tables.Values.OrderBy(t => t.Id))
        {
            try
            {
                await table.TickAsync(open);
            }
            catch (Exception ex)
            {
                await logging.LogChannelAndFile($"WebPoker: ERROR on the tick of table {table.Id}: {ex}", ExtendedLogSeverity.Error);
            }

            await AfterAsync(table);
        }
    }

    /// <summary>Refuses new tables and seats, gives back the tokens committed to every unfinished hand and closes the tables. Called once, when the server stops.</summary>
    public async Task ReleaseAllAsync()
    {
        _stopped = true;
        foreach (var table in _tables.Values.OrderBy(t => t.Id))
        {
            try
            {
                await table.StopAsync();
            }
            catch (Exception ex)
            {
                await logging.LogChannelAndFile($"WebPoker: ERROR closing table {table.Id}: {ex}", ExtendedLogSeverity.Error);
            }

            await AfterAsync(table);
        }
    }

    #endregion

    private Table? SeatedTable(ulong userId) =>
        _seatOf.TryGetValue(userId, out var id) && _tables.TryGetValue(id, out var table) ? table : null;

    /// <summary>Drops a closed table from the registry, then tells the lobby if anything in it changed.</summary>
    private async Task AfterAsync(Table table)
    {
        if (table.Closed)
        {
            bool removed;
            lock (_registry)
            {
                removed = _tables.TryRemove(table.Id, out _);
            }

            if (removed)
            {
                foreach (var (user, watched) in _watching)
                {
                    if (watched == table.Id)
                    {
                        _watching.TryRemove(new KeyValuePair<ulong, int>(user, watched));
                    }
                }

                _listDirty = true;
            }
        }

        await PublishListAsync();
    }

    private async Task PublishListAsync()
    {
        await _listGate.WaitAsync();
        try
        {
            var tables = _tables.Values.OrderBy(t => t.Id).ToList();
            if (!_listDirty && tables.All(t => t.Summary == t.PublishedSummary))
            {
                return;
            }

            _listDirty = false;
            foreach (var table in tables)
            {
                table.PublishedSummary = table.Summary;
            }

            try
            {
                await notifier.TablesChangedAsync([.. tables.Select(t => t.Summary)]);
            }
            catch (Exception ex)
            {
                await logging.LogChannelAndFile($"WebPoker: ERROR pushing the tables list: {ex.Message}", ExtendedLogSeverity.Error);
            }
        }
        finally
        {
            _listGate.Release();
        }
    }
}
