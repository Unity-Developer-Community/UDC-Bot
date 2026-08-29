using DiscordBot.Components;
using DiscordBot.Settings.Options;
using Insight.Database;
using Microsoft.Extensions.Options;
using Npgsql;

namespace DiscordBot.Services;

/// <summary>
/// Replaces MySQL EVENT scheduler — resets weekly/monthly/yearly karma columns on schedule.
/// Tracks last-reset timestamps so missed resets are caught up on startup.
/// </summary>
public class KarmaResetService : IManagedBotService, IComponentHealthContributor
{
    private const string MetaTable = "karma_reset_meta";

    private readonly ILoggingService _logging;
    private readonly string _connectionString;
    private readonly SemaphoreSlim _lifecycleLock = new(1, 1);
    private CancellationTokenSource? _lifecycleCancellation;
    private Task? _loopTask;

    public string ComponentId => ComponentIds.KarmaReset;
    public bool IsRunning => _loopTask is { IsCompleted: false };

    public KarmaResetService(ILoggingService logging, IOptions<DatabaseOptions> options)
    {
        _logging = logging;
        _connectionString = options.Value.ConnectionString;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await _lifecycleLock.WaitAsync(cancellationToken);
        try
        {
            if (IsRunning)
                return;
            _lifecycleCancellation?.Dispose();
            _lifecycleCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _loopTask = RunLoop(_lifecycleCancellation.Token);
        }
        finally
        {
            _lifecycleLock.Release();
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        await _lifecycleLock.WaitAsync(cancellationToken);
        try
        {
            if (_loopTask is null)
                return;
            await _lifecycleCancellation!.CancelAsync();
            try
            {
                await _loopTask.WaitAsync(cancellationToken);
            }
            catch (OperationCanceledException) when (_lifecycleCancellation.IsCancellationRequested)
            {
            }
            _loopTask = null;
            _lifecycleCancellation.Dispose();
            _lifecycleCancellation = null;
        }
        finally
        {
            _lifecycleLock.Release();
        }
    }

    public Task<ComponentHealthSnapshot> GetHealthAsync(CancellationToken cancellationToken) =>
        Task.FromResult(new ComponentHealthSnapshot(
            IsRunning ? ComponentRuntimeState.Running : ComponentRuntimeState.Stopped,
            IsRunning ? "Karma reset loop is running." : "Karma reset loop is stopped.",
            DateTimeOffset.UtcNow));

    private async Task RunLoop(CancellationToken cancellationToken)
    {
        // Wait for DatabaseService to finish table creation
        await Task.Delay(TimeSpan.FromSeconds(10), cancellationToken);

        try
        {
            await EnsureMetaTable();
            await CatchUpMissedResets();
        }
        catch (Exception e)
        {
            await _logging.LogChannelAndFile($"KarmaResetService: Failed during startup: {e.Message}", ExtendedLogSeverity.Warning);
        }

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromHours(1), cancellationToken);

                var now = DateTime.UtcNow;

                if (now.DayOfWeek == DayOfWeek.Monday)
                    await TryReset("weekly", UserProps.KarmaWeekly);

                if (now.Day == 1)
                {
                    await TryReset("monthly", UserProps.KarmaMonthly);

                    if (now.Month == 1)
                        await TryReset("yearly", UserProps.KarmaYearly);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception e)
            {
                await _logging.LogChannelAndFile($"KarmaResetService: Error during reset check: {e.Message}", ExtendedLogSeverity.Warning);
            }
        }
    }

    private async Task EnsureMetaTable()
    {
        await using var c = new NpgsqlConnection(_connectionString);
        await c.OpenAsync();
        await c.ExecuteSqlAsync(
            $"CREATE TABLE IF NOT EXISTS {MetaTable} (" +
            $"period varchar(16) PRIMARY KEY, " +
            $"last_reset timestamptz NOT NULL DEFAULT '1970-01-01 00:00:00+00')");
        await c.ExecuteSqlAsync($"INSERT INTO {MetaTable} (period) VALUES ('weekly') ON CONFLICT DO NOTHING");
        await c.ExecuteSqlAsync($"INSERT INTO {MetaTable} (period) VALUES ('monthly') ON CONFLICT DO NOTHING");
        await c.ExecuteSqlAsync($"INSERT INTO {MetaTable} (period) VALUES ('yearly') ON CONFLICT DO NOTHING");
    }

    private async Task CatchUpMissedResets()
    {
        var now = DateTime.UtcNow;

        var weeklyLast = await GetLastReset("weekly");
        if (WeekNumber(now) != WeekNumber(weeklyLast) || now.Year != weeklyLast.Year)
            await ResetColumn("weekly", UserProps.KarmaWeekly);

        var monthlyLast = await GetLastReset("monthly");
        if (now.Month != monthlyLast.Month || now.Year != monthlyLast.Year)
            await ResetColumn("monthly", UserProps.KarmaMonthly);

        var yearlyLast = await GetLastReset("yearly");
        if (now.Year != yearlyLast.Year)
            await ResetColumn("yearly", UserProps.KarmaYearly);
    }

    private async Task TryReset(string period, string column)
    {
        var lastReset = await GetLastReset(period);
        var now = DateTime.UtcNow;

        var shouldReset = period switch
        {
            "weekly" => WeekNumber(now) != WeekNumber(lastReset) || now.Year != lastReset.Year,
            "monthly" => now.Month != lastReset.Month || now.Year != lastReset.Year,
            "yearly" => now.Year != lastReset.Year,
            _ => false
        };

        if (shouldReset)
            await ResetColumn(period, column);
    }

    private async Task ResetColumn(string period, string column)
    {
        await using var c = new NpgsqlConnection(_connectionString);
        await c.OpenAsync();
        await c.ExecuteSqlAsync($"UPDATE {UserProps.TableName} SET {column} = 0");
        await c.ExecuteSqlAsync($"UPDATE {MetaTable} SET last_reset = NOW() WHERE period = @period", new { period });
        await _logging.LogChannelAndFile($"KarmaResetService: Reset {period} karma ({column}).", ExtendedLogSeverity.Positive);
    }

    private async Task<DateTime> GetLastReset(string period)
    {
        await using var c = new NpgsqlConnection(_connectionString);
        await c.OpenAsync();
        var results = await c.QuerySqlAsync($"SELECT last_reset FROM {MetaTable} WHERE period = @period", new { period });
        if (results.Count > 0 && results[0] is IDictionary<string, object> row && row.TryGetValue("last_reset", out var val) && val is DateTime dt)
            return dt;
        return DateTime.MinValue;
    }

    private static int WeekNumber(DateTime date) =>
        System.Globalization.CultureInfo.InvariantCulture.Calendar
            .GetWeekOfYear(date, System.Globalization.CalendarWeekRule.FirstFourDayWeek, DayOfWeek.Monday);
}
