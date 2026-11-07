using System.Data.Common;
using Discord.WebSocket;
using DiscordBot.Domain;
using DiscordBot.Settings;
using Insight.Database;
using Insight.Database.Providers.PostgreSQL;
using Npgsql;

namespace DiscordBot.Services;

public class DatabaseService : ICasinoDatabase
{
    private const string ServiceName = "DatabaseService";

    private readonly ILoggingService _logging;
    private string ConnectionString { get; }

    private ICasinoRepo? CreateCasinoQuery()
    {
        try
        {
            var c = new NpgsqlConnection(ConnectionString);
            return c.As<ICasinoRepo>();
        }
        catch (Exception e)
        {
            _logging.LogChannelAndFile($"SQL Exception: Failed to create casino query.\nMessage: {e}", ExtendedLogSeverity.Critical);
            return null;
        }
    }

    private IServerUserRepo? CreateQuery()
    {
        try
        {
            var c = new NpgsqlConnection(ConnectionString);
            return c.As<IServerUserRepo>();
        }
        catch (Exception e)
        {
            _logging.LogChannelAndFile($"SQL Exception: Failed to create query.\nMessage: {e}", ExtendedLogSeverity.Critical);
            return null;
        }
    }

    public IServerUserRepo? Query => CreateQuery();
    public ICasinoRepo? CasinoQuery => CreateCasinoQuery();

    private IBadgeRepo? CreateBadgeQuery()
    {
        try
        {
            var c = new NpgsqlConnection(ConnectionString);
            return c.As<IBadgeRepo>();
        }
        catch (Exception e)
        {
            _logging.LogChannelAndFile($"SQL Exception: Failed to create badge query.\nMessage: {e}", ExtendedLogSeverity.Critical);
            return null;
        }
    }

    public IBadgeRepo? BadgeQuery => CreateBadgeQuery();

    public DatabaseService(ILoggingService logging, BotSettings settings, StartupReport? startupReport = null)
    {
        PostgreSQLInsightDbProvider.RegisterProvider();

        ConnectionString = settings.DbConnectionString;
        _logging = logging;

        DbConnection? c = null;
        try
        {
            c = new NpgsqlConnection(ConnectionString);
        }
        catch (Exception e)
        {
            LoggingService.LogToConsole($"SQL Exception: Failed to start DatabaseService.\nMessage: {e}",
                LogSeverity.Critical);
            return;
        }

        var startupStep = startupReport?.BeginStep("Database");
        Task.Run(async () =>
        {
            using var _ = startupStep;
            // Counts for the one "Connected" line; null when the table was just created.
            long? userCount = null, badgeCount = null, casinoUserCount = null;
            // Test connection, if it fails we create the table and set keys
            try
            {
                var query = Query;
                if (query == null) return;
                userCount = await query.TestConnection();

                var defaultCityExists = await c.ColumnExists(UserProps.TableName, UserProps.DefaultCity);
                if (!defaultCityExists)
                {
                    c.ExecuteSql($"ALTER TABLE {UserProps.TableName} ADD COLUMN {UserProps.DefaultCity} varchar(64) DEFAULT NULL");
                    await _logging.LogAction($"[{ServiceName}] Added column '{UserProps.DefaultCity}' to '{UserProps.TableName}'.",
                        ExtendedLogSeverity.Positive);
                }
                var birthdayExists = await c.ColumnExists(UserProps.TableName, UserProps.Birthday);
                if (!birthdayExists)
                {
                    c.ExecuteSql($"ALTER TABLE {UserProps.TableName} ADD COLUMN {UserProps.Birthday} timestamp DEFAULT NULL");
                    await _logging.LogAction($"[{ServiceName}] Added column '{UserProps.Birthday}' to '{UserProps.TableName}'.",
                        ExtendedLogSeverity.Positive);
                }

                // Initialize badge tables
                badgeCount = await InitializeBadgeTables(c);
            }
            catch
            {
                await _logging.LogAction($"[{ServiceName}] Table '{UserProps.TableName}' is missing, creating it.",
                    ExtendedLogSeverity.LowWarning);
                try
                {
                    c.ExecuteSql(
                        $"CREATE TABLE {UserProps.TableName} (" +
                        $"id SERIAL PRIMARY KEY, " +
                        $"{UserProps.UserID} varchar(32) NOT NULL UNIQUE, " +
                        $"{UserProps.Karma} integer NOT NULL DEFAULT 0, " +
                        $"{UserProps.KarmaWeekly} integer NOT NULL DEFAULT 0, " +
                        $"{UserProps.KarmaMonthly} integer NOT NULL DEFAULT 0, " +
                        $"{UserProps.KarmaYearly} integer NOT NULL DEFAULT 0, " +
                        $"{UserProps.KarmaGiven} integer NOT NULL DEFAULT 0, " +
                        $"{UserProps.Exp} bigint NOT NULL DEFAULT 0, " +
                        $"{UserProps.Level} integer NOT NULL DEFAULT 0, " +
                        $"{UserProps.DefaultCity} varchar(64) DEFAULT NULL, " +
                        $"{UserProps.Birthday} timestamp DEFAULT NULL)");
                }
                catch (Exception e)
                {
                    await _logging.LogAction(
                        $"[{ServiceName}] Couldn't create table '{UserProps.TableName}'.\n{e}",
                        ExtendedLogSeverity.Critical);
                    c.Close();
                    return;
                }
                await _logging.LogAction($"[{ServiceName}] Created table '{UserProps.TableName}'.",
                    ExtendedLogSeverity.Positive);
                c.Close();
            }

            // Create casino tables if they don't exist
            try
            {
                var casinoQuery = CasinoQuery;
                if (casinoQuery == null) return;
                casinoUserCount = await casinoQuery.TestCasinoConnection();
            }
            catch
            {
                await _logging.LogAction($"[{ServiceName}] Casino tables are missing, creating them.",
                    ExtendedLogSeverity.LowWarning);
                try
                {
                    c.ExecuteSql(
                        $"CREATE TABLE {CasinoProps.CasinoTableName} (" +
                        $"{CasinoProps.Id} SERIAL PRIMARY KEY, " +
                        $"{CasinoProps.UserID} varchar(32) NOT NULL UNIQUE, " +
                        $"{CasinoProps.Tokens} bigint NOT NULL DEFAULT 1000, " +
                        $"{CasinoProps.CreatedAt} timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP, " +
                        $"{CasinoProps.UpdatedAt} timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP, " +
                        $"{CasinoProps.LastDailyReward} timestamptz NOT NULL DEFAULT '1970-01-01 00:00:01+00')");

                    c.ExecuteSql(
                        $"CREATE TABLE {CasinoProps.TransactionTableName} (" +
                        $"{CasinoProps.TransactionId} SERIAL PRIMARY KEY, " +
                        $"{CasinoProps.TransactionUserID} varchar(32) NOT NULL, " +
                        $"{CasinoProps.TargetUserID} varchar(32) DEFAULT NULL, " +
                        $"{CasinoProps.Amount} bigint NOT NULL, " +
                        $"{CasinoProps.TransactionType} varchar(50) NOT NULL, " +
                        $"{CasinoProps.Details} text DEFAULT NULL, " +
                        $"{CasinoProps.TransactionCreatedAt} timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP)");

                    c.ExecuteSql(
                        $"CREATE INDEX idx_user_created ON {CasinoProps.TransactionTableName} " +
                        $"({CasinoProps.TransactionUserID}, {CasinoProps.TransactionCreatedAt})");
                }
                catch (Exception e)
                {
                    await _logging.LogAction(
                        $"[{ServiceName}] Couldn't create the casino tables.\n{e}",
                        ExtendedLogSeverity.Critical);
                    c.Close();
                    return;
                }
                await _logging.LogAction($"[{ServiceName}] Created the casino tables.",
                    ExtendedLogSeverity.Positive);
                c.Close();
            }

            await _logging.LogAction($"[{ServiceName}] {ConnectedSummary(userCount, badgeCount, casinoUserCount)}", ExtendedLogSeverity.Positive);

            // Not part of the block above, which only runs when casino_users is missing
            try
            {
                foreach (var statement in JackpotProps.EnsureStatements)
                    c.ExecuteSql(statement);
            }
            catch (Exception e)
            {
                await _logging.LogAction(
                    $"[{ServiceName}] Couldn't create the slots jackpot table.\n{e}",
                    ExtendedLogSeverity.Critical);
            }
        });
    }

    public async Task FullDbSync(IGuild guild, IUserMessage message)
    {
        string messageContent = message.Content + " ";
        var userList = await guild.GetUsersAsync(CacheMode.AllowDownload, RequestOptions.Default);
        await message.ModifyAsync(msg =>
        {
            if (msg != null) msg.Content = $"{messageContent}0/{userList.Count.ToString()}";
        });

        int counter = 0, newAdd = 0;
        var updater = Task.Run(function: async () =>
        {
            foreach (var user in userList)
            {
                var member = await guild.GetUserAsync(user.Id);
                if (!user.IsBot)
                {
                    var userIdString = user.Id.ToString();
                    var q = Query;
                    if (q == null) continue;
                    var serverUser = await q.GetUser(userIdString);
                    if (serverUser == null)
                    {
                        await GetOrAddUser(user as SocketGuildUser);
                        newAdd++;
                    }
                }
                counter++;
            }
        });

        while (!updater.IsCompleted && !updater.IsCanceled)
        {
            await Task.Delay(1000);
            await message.ModifyAsync(properties =>
            {
                if (properties != null)
                    properties.Content = $"{messageContent}{counter.ToString()}/{userList.Count.ToString()}";
            });
        }

        await _logging.LogChannelAndFile(
            $"Database Synchronized {counter.ToString()} Users Successfully.\n{newAdd.ToString()} missing users added.");
    }

    /// <summary>
    /// Adds a new user to the database if they don't already exist.
    /// </summary>
    /// <returns>Existing or newly created user. Null on database error.</returns>
    public async Task<ServerUser?> GetOrAddUser(SocketGuildUser? socketUser)
    {
        if (socketUser == null)
        {
            await _logging.Log(LogBehaviour.ConsoleChannelAndFile,
                $"SocketUser is null", ExtendedLogSeverity.Warning);
            return null;
        }

        if (Query == null)
        {
            await _logging.Log(LogBehaviour.ConsoleChannelAndFile,
                $"Query is null", ExtendedLogSeverity.Warning);
            return null;
        }

        try
        {
            var user = await Query.GetUser(socketUser.Id.ToString());
            if (user != null)
                return user;

            user = new ServerUser
            {
                UserID = socketUser.Id.ToString(),
            };

            user = await Query.InsertUser(user);

            if (user == null)
            {
                await _logging.Log(LogBehaviour.ConsoleChannelAndFile,
                    $"User is null after InsertUser", ExtendedLogSeverity.Warning);
                return null;
            }

            await _logging.Log(LogBehaviour.File,
                $"User {socketUser.GetPreferredAndUsername()} successfully added to the database.");
            return user;
        }
        catch (Exception e)
        {
            await _logging.Log(LogBehaviour.ConsoleChannelAndFile,
                $"Error when trying to add user {socketUser.Id.ToString()} to the database : {e}", ExtendedLogSeverity.Warning);
            return null;
        }
    }

    public async Task DeleteUser(ulong id)
    {
        try
        {
            var query = Query;
            if (query == null) return;
            var user = await query.GetUser(id.ToString());
            if (user != null)
                await query.RemoveUser(user.UserID);
        }
        catch (Exception e)
        {
            await _logging.Log(LogBehaviour.Console | LogBehaviour.File,
                $"Error when trying to delete user {id.ToString()} from the database : {e}", ExtendedLogSeverity.Warning);
        }
    }

    public async Task<bool> UserExists(ulong id)
    {
        var query = Query;
        if (query == null) return false;
        return (await query.GetUser(id.ToString()) != null);
    }

    /// <summary>Creates or upgrades the badge tables. Returns the badge count, or null when the tables were just created.</summary>
    private async Task<long?> InitializeBadgeTables(DbConnection c)
    {
        try
        {
            // Test badge connection, if it fails we create the tables
            var badgeQuery = BadgeQuery;
            if (badgeQuery == null) return null;

            var badgeCount = await badgeQuery.TestBadgeConnection();

            var isPublicExists = await c.ColumnExists(BadgeProps.TableName, BadgeProps.IsPublic);
            if (!isPublicExists)
            {
                c.ExecuteSql($"ALTER TABLE {BadgeProps.TableName} ADD COLUMN {BadgeProps.IsPublic} boolean NOT NULL DEFAULT TRUE");
                await _logging.LogAction($"[{ServiceName}] Added column '{BadgeProps.IsPublic}' to '{BadgeProps.TableName}'.",
                    ExtendedLogSeverity.Positive);
            }

            var groupKeyExists = await c.ColumnExists(BadgeProps.TableName, BadgeProps.GroupKey);
            if (!groupKeyExists)
            {
                c.ExecuteSql($"ALTER TABLE {BadgeProps.TableName} ADD COLUMN {BadgeProps.GroupKey} varchar(64) DEFAULT NULL");
                await _logging.LogAction($"[{ServiceName}] Added column '{BadgeProps.GroupKey}' to '{BadgeProps.TableName}'.",
                    ExtendedLogSeverity.Positive);
            }
            return badgeCount;
        }
        catch
        {
            await _logging.LogAction($"[{ServiceName}] Badge tables are missing, creating them.",
                ExtendedLogSeverity.LowWarning);
            try
            {
                // Create badges table
                c.ExecuteSql(
                    $"CREATE TABLE {BadgeProps.TableName} (" +
                    $"{BadgeProps.Id} SERIAL PRIMARY KEY, " +
                    $"{BadgeProps.Title} varchar(100) NOT NULL UNIQUE, " +
                    $"{BadgeProps.Description} text NOT NULL, " +
                    $"{BadgeProps.GroupKey} varchar(64) DEFAULT NULL, " +
                    $"{BadgeProps.IsPublic} boolean NOT NULL DEFAULT TRUE, " +
                    $"{BadgeProps.CreatedAt} timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP)");

                // Create user_badges table
                c.ExecuteSql(
                    $"CREATE TABLE {UserBadgeProps.TableName} (" +
                    $"{UserBadgeProps.Id} SERIAL PRIMARY KEY, " +
                    $"{UserBadgeProps.UserID} varchar(32) NOT NULL, " +
                    $"{UserBadgeProps.BadgeId} integer NOT NULL REFERENCES {BadgeProps.TableName} ({BadgeProps.Id}) ON DELETE CASCADE, " +
                    $"{UserBadgeProps.AwardedAt} timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP, " +
                    $"{UserBadgeProps.AwardedBy} varchar(32) NOT NULL, " +
                    $"UNIQUE ({UserBadgeProps.UserID}, {UserBadgeProps.BadgeId}))");

                await _logging.LogAction($"[{ServiceName}] Created the badge tables.",
                    ExtendedLogSeverity.Positive);
            }
            catch (Exception e)
            {
                await _logging.LogAction(
                    $"[{ServiceName}] Couldn't create the badge tables.\n{e}",
                    ExtendedLogSeverity.Critical);
            }
            return null;
        }
    }

    /// <summary><c>Connected: 5 users, 12 badges, 3 casino users.</c> Tables just created are left out.</summary>
    internal static string ConnectedSummary(long? users, long? badges, long? casinoUsers)
    {
        static string Count(long n, string noun) => $"{n} {noun}{(n == 1 ? "" : "s")}";
        var parts = new List<string>();
        if (users is { } u) parts.Add(Count(u, "user"));
        if (badges is { } b) parts.Add(Count(b, "badge"));
        if (casinoUsers is { } cu) parts.Add(Count(cu, "casino user"));
        return parts.Count == 0 ? "Connected." : $"Connected: {string.Join(", ", parts)}.";
    }
}