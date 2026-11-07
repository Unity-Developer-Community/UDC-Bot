using DiscordBot.Settings;
using Insight.Database;
using DiscordBot.Services.Server;
using Npgsql;

namespace DiscordBot.Services.Profiles;

/// <summary>
/// Replaces MySQL EVENT scheduler — resets weekly/monthly/yearly karma columns on schedule.
/// Tracks last-reset timestamps so missed resets are caught up on startup.
/// </summary>
public class KarmaResetService
{
    private const string MetaTable = "karma_reset_meta";

    private readonly ILoggingService _logging;
    private readonly string _connectionString;
    private readonly CancellationToken _shutdownToken;

    private const string ServiceName = nameof(KarmaResetService);

    public KarmaResetService(ILoggingService logging, BotSettings settings, CancellationTokenSource cts, StartupReport startupReport)
    {
        _logging = logging;
        _connectionString = settings.DbConnectionString;
        _shutdownToken = cts.Token;

        var startupStep = startupReport.BeginStep("Karma resets");
        Task.Run(() => RunLoop(startupStep));
    }

    private async Task RunLoop(IDisposable startupStep)
    {
        using (startupStep)
        {
            try
            {
                // Wait for DatabaseService to finish table creation
                await Task.Delay(TimeSpan.FromSeconds(10), _shutdownToken);
                await EnsureMetaTable();
                await CatchUpMissedResets();
                await _logging.LogAction($"[{ServiceName}] {LastResetsLine(await GetLastReset("weekly"), await GetLastReset("monthly"), await GetLastReset("yearly"))}",
                    ExtendedLogSeverity.Info);
            }
            catch (OperationCanceledException) { return; }
            catch (Exception e)
            {
                await _logging.LogChannelAndFile($"[{ServiceName}] Failed during startup: {e.Message}", ExtendedLogSeverity.Warning);
            }
        }

        try
        {
            while (!_shutdownToken.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromHours(1), _shutdownToken);

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
        }
        catch (OperationCanceledException) { }
        catch (Exception e)
        {
            await _logging.LogChannelAndFile($"[{ServiceName}] Error during reset check: {e.Message}", ExtendedLogSeverity.Warning);
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
        await _logging.LogChannelAndFile($"[{ServiceName}] Reset {period} karma ({column}).", ExtendedLogSeverity.Positive);
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

    /// <summary><c>Last resets: weekly &lt;t:…:R&gt;, monthly …, yearly never.</c> Times are UTC.</summary>
    internal static string LastResetsLine(DateTime weekly, DateTime monthly, DateTime yearly)
    {
        static string When(DateTime utc) => utc == DateTime.MinValue
            ? "never"
            : AuditLogCards.Timestamp(new DateTimeOffset(DateTime.SpecifyKind(utc, DateTimeKind.Utc)), 'R');
        return $"Last resets: weekly {When(weekly)}, monthly {When(monthly)}, yearly {When(yearly)}.";
    }

    private static int WeekNumber(DateTime date) =>
        System.Globalization.CultureInfo.InvariantCulture.Calendar
            .GetWeekOfYear(date, System.Globalization.CalendarWeekRule.FirstFourDayWeek, DayOfWeek.Monday);
}