using System.Data.Common;
using Discord.WebSocket;
using DiscordBot.Domain;
using DiscordBot.Settings;
using Insight.Database;
using MySql.Data.MySqlClient;

namespace DiscordBot.Services;

public class DatabaseService
{
    private const string ServiceName = "DatabaseService";

    private readonly ILoggingService _logging;
    private string ConnectionString { get; }

    private ICasinoRepo CreateCasinoQuery()
    {
        try
        {
            var c = new MySqlConnection(ConnectionString);
            return c.As<ICasinoRepo>();
        }
        catch (Exception e)
        {
            _logging.LogChannelAndFile($"SQL Exception: Failed to create casino query.\nMessage: {e}", ExtendedLogSeverity.Critical);
            return null;
        }
    }

    private IServerUserRepo CreateQuery()
    {
        try
        {
            var c = new MySqlConnection(ConnectionString);
            return c.As<IServerUserRepo>();
        }
        catch (Exception e)
        {
            _logging.LogChannelAndFile($"SQL Exception: Failed to create query.\nMessage: {e}", ExtendedLogSeverity.Critical);
            return null;
        }
    }

    private IBadgeRepo CreateBadgeQuery()
    {
        try
        {
            var c = new MySqlConnection(ConnectionString);
            return c.As<IBadgeRepo>();
        }
        catch (Exception e)
        {
            _logging.LogChannelAndFile($"SQL Exception: Failed to create badge query.\nMessage: {e}", ExtendedLogSeverity.Critical);
            return null;
        }
    }

    public IServerUserRepo Query => CreateQuery();
    public IBadgeRepo BadgeQuery => CreateBadgeQuery();
    public ICasinoRepo CasinoQuery => CreateCasinoQuery();

    public DatabaseService(ILoggingService logging, BotSettings settings)
    {
        ConnectionString = settings.DbConnectionString;
        _logging = logging;

        DbConnection c = null;
        try
        {
            c = new MySqlConnection(ConnectionString);
        }
        catch (Exception e)
        {
            LoggingService.LogToConsole($"SQL Exception: Failed to start DatabaseService.\nMessage: {e}",
                LogSeverity.Critical);
            return;
        }

        Task.Run(async () =>
        {
            // Test connection, if it fails we create the table and set keys
            try
            {
                var userCount = await Query.TestConnection();
                await _logging.LogAction(
                    $"{ServiceName}: Connected to database successfully. {userCount} users in database.",
                    ExtendedLogSeverity.Positive);

                // Not sure on best practice for if column is missing, full blown migrations seem overkill
                var defaultCityExists = await c.ColumnExists(UserProps.TableName, UserProps.DefaultCity);
                if (!defaultCityExists)
                {
                    c.ExecuteSql($"ALTER TABLE `{UserProps.TableName}` ADD `{UserProps.DefaultCity}` varchar(64) COLLATE utf8mb4_unicode_ci DEFAULT NULL AFTER `{UserProps.Level}`");
                    await _logging.LogAction($"DatabaseService: Added missing column '{UserProps.DefaultCity}' to table '{UserProps.TableName}'.",
                        ExtendedLogSeverity.Positive);
                }

                // Initialize badge tables
                await InitializeBadgeTables(c);
            }
            catch
            {
                await _logging.LogAction($"DatabaseService: Table '{UserProps.TableName}' does not exist, attempting to generate table.",
                    ExtendedLogSeverity.LowWarning);
                try
                {
                    c.ExecuteSql(
                        $"CREATE TABLE `{UserProps.TableName}` (`ID` int(11) UNSIGNED  NOT NULL," +
                        $"`{UserProps.UserID}` varchar(32) COLLATE utf8mb4_unicode_ci NOT NULL, " +
                        $"`{UserProps.Karma}` int(11) UNSIGNED  NOT NULL DEFAULT 0, " +
                        $"`{UserProps.KarmaWeekly}` int(11) UNSIGNED  NOT NULL DEFAULT 0, " +
                        $"`{UserProps.KarmaMonthly}` int(11) UNSIGNED  NOT NULL DEFAULT 0, " +
                        $"`{UserProps.KarmaYearly}` int(11) UNSIGNED  NOT NULL DEFAULT 0, " +
                        $"`{UserProps.KarmaGiven}` int(11) UNSIGNED NOT NULL DEFAULT 0, " +
                        $"`{UserProps.Exp}` bigint(11) UNSIGNED  NOT NULL DEFAULT 0, " +
                        $"`{UserProps.Level}` int(11) UNSIGNED NOT NULL DEFAULT 0) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci");
                    c.ExecuteSql(
                        $"ALTER TABLE `{UserProps.TableName}` ADD PRIMARY KEY (`ID`,`{UserProps.UserID}`), ADD UNIQUE KEY `{UserProps.UserID}` (`{UserProps.UserID}`)");
                    c.ExecuteSql(
                        $"ALTER TABLE `{UserProps.TableName}` MODIFY `ID` int(11) UNSIGNED NOT NULL AUTO_INCREMENT, AUTO_INCREMENT=1");

                    // "DefaultCity" Nullable - Weather, BDay, Temp, Time, etc. Optional for users to set their own city (Added - Jan 2024)
                    c.ExecuteSql(
                        $"ALTER TABLE `{UserProps.TableName}` ADD `{UserProps.DefaultCity}` varchar(64) COLLATE utf8mb4_unicode_ci DEFAULT NULL AFTER `{UserProps.Level}`");
                }
                catch (Exception e)
                {
                    await _logging.LogAction(
                        $"SQL Exception: Failed to generate table '{UserProps.TableName}'.\nMessage: {e}",
                        ExtendedLogSeverity.Critical);
                    c.Close();
                    return;
                }
                await _logging.LogAction($"DatabaseService: Table '{UserProps.TableName}' generated without errors.",
                    ExtendedLogSeverity.Positive);
                c.Close();
            }

            // Create casino tables if they don't exist
            try
            {
                var casinoUserCount = await CasinoQuery.TestCasinoConnection();
                await _logging.LogAction(
                    $"DatabaseService: Connected to casino tables successfully. {casinoUserCount} casino users in database.",
                    ExtendedLogSeverity.Positive);
            }
            catch
            {
                await _logging.LogAction($"DatabaseService: Casino tables do not exist, attempting to generate tables.",
                    ExtendedLogSeverity.LowWarning);
                try
                {
                    // Create casino_users table
                    c.ExecuteSql(
                        $"CREATE TABLE `{CasinoProps.CasinoTableName}` (" +
                        $"`{CasinoProps.Id}` int(11) UNSIGNED NOT NULL AUTO_INCREMENT, " +
                        $"`{CasinoProps.UserID}` varchar(32) COLLATE utf8mb4_unicode_ci NOT NULL, " +
                        $"`{CasinoProps.Tokens}` bigint(20) UNSIGNED NOT NULL DEFAULT 1000, " +
                        $"`{CasinoProps.CreatedAt}` timestamp NOT NULL DEFAULT CURRENT_TIMESTAMP, " +
                        $"`{CasinoProps.UpdatedAt}` timestamp NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP, " +
                        $"`{CasinoProps.LastDailyReward}` timestamp NOT NULL DEFAULT '1970-01-01 00:00:01', " +
                        $"PRIMARY KEY (`{CasinoProps.Id}`), " +
                        $"UNIQUE KEY `{CasinoProps.UserID}` (`{CasinoProps.UserID}`) " +
                        $") ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci");

                    // Create token_transactions table  
                    c.ExecuteSql(
                        $"CREATE TABLE `{CasinoProps.TransactionTableName}` (" +
                        $"`{CasinoProps.TransactionId}` int(11) UNSIGNED NOT NULL AUTO_INCREMENT, " +
                        $"`{CasinoProps.TransactionUserID}` varchar(32) COLLATE utf8mb4_unicode_ci NOT NULL, " +
                        $"`{CasinoProps.Amount}` bigint(20) NOT NULL, " +
                        $"`{CasinoProps.TransactionType}` int(11) NOT NULL, " +
                        $"`{CasinoProps.Details}` json DEFAULT NULL, " + // JSON column for transaction details
                        $"`{CasinoProps.TransactionCreatedAt}` timestamp NOT NULL DEFAULT CURRENT_TIMESTAMP, " +
                        $"PRIMARY KEY (`{CasinoProps.TransactionId}`), " +
                        $"KEY `idx_user_created` (`{CasinoProps.TransactionUserID}`, `{CasinoProps.TransactionCreatedAt}`) " +
                        $") ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci");
                }
                catch (Exception e)
                {
                    await _logging.LogAction(
                        $"SQL Exception: Failed to generate casino tables.\nMessage: {e}",
                        ExtendedLogSeverity.Critical);
                    c.Close();
                    return;
                }
                await _logging.LogAction($"DatabaseService: Casino tables generated without errors.",
                    ExtendedLogSeverity.Positive);
                c.Close();
            }

            // Generate and add events if they don't exist
            try
            {
                c.ExecuteSql(
                    $"CREATE EVENT IF NOT EXISTS `ResetWeeklyLeaderboards` ON SCHEDULE EVERY 1 WEEK STARTS '2021-08-02 00:00:00' ON COMPLETION NOT PRESERVE ENABLE DO UPDATE {c.Database}.users SET {UserProps.KarmaWeekly} = 0");
                c.ExecuteSql(
                    $"CREATE EVENT IF NOT EXISTS `ResetMonthlyLeaderboards` ON SCHEDULE EVERY 1 MONTH STARTS '2021-08-01 00:00:00' ON COMPLETION NOT PRESERVE ENABLE DO UPDATE {c.Database}.users SET {UserProps.KarmaMonthly} = 0");
                c.ExecuteSql(
                    $"CREATE EVENT IF NOT EXISTS `ResetYearlyLeaderboards` ON SCHEDULE EVERY 1 YEAR STARTS '2022-01-01 00:00:00' ON COMPLETION NOT PRESERVE ENABLE DO UPDATE {c.Database}.users SET {UserProps.KarmaYearly} = 0");
                c.Close();
            }
            catch (Exception e)
            {
                await _logging.LogAction($"SQL Exception: Failed to generate leaderboard events.\nMessage: {e}",
                    ExtendedLogSeverity.Warning);
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
                    var serverUser = await Query.GetUser(userIdString);
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
    public async Task<ServerUser> GetOrAddUser(SocketGuildUser socketUser)
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
            var user = await Query.GetUser(id.ToString());
            if (user != null)
                await Query.RemoveUser(user.UserID);
        }
        catch (Exception e)
        {
            await _logging.Log(LogBehaviour.Console | LogBehaviour.File,
                $"Error when trying to delete user {id.ToString()} from the database : {e}", ExtendedLogSeverity.Warning);
        }
    }

    public async Task<bool> UserExists(ulong id)
    {
        return (await Query.GetUser(id.ToString()) != null);
    }

    private async Task InitializeBadgeTables(DbConnection c)
    {
        try
        {
            // Test badge connection, if it fails we create the tables
            var badgeCount = await BadgeQuery.TestBadgeConnection();
            await _logging.LogAction(
                $"DatabaseService: Connected to badge tables successfully. {badgeCount} badges in database.",
                ExtendedLogSeverity.Positive);

            // Check if IsPublic column exists, if not add it (for existing installations)
            try
            {
                c.ExecuteSql($"SELECT {BadgeProps.IsPublic} FROM {BadgeProps.TableName} LIMIT 1");
            }
            catch
            {
                // Column doesn't exist, add it
                await _logging.LogAction("DatabaseService: Adding IsPublic column to badges table.",
                    ExtendedLogSeverity.LowWarning);
                c.ExecuteSql($"ALTER TABLE `{BadgeProps.TableName}` ADD COLUMN `{BadgeProps.IsPublic}` tinyint(1) NOT NULL DEFAULT 1");
                await _logging.LogAction("DatabaseService: IsPublic column added successfully.",
                    ExtendedLogSeverity.Positive);
            }
        }
        catch
        {
            await _logging.LogAction($"DatabaseService: Badge tables do not exist, attempting to generate tables.",
                ExtendedLogSeverity.LowWarning);
            try
            {
                // Create badges table
                c.ExecuteSql(
                    $"CREATE TABLE `{BadgeProps.TableName}` (" +
                    $"`{BadgeProps.Id}` int(11) UNSIGNED NOT NULL AUTO_INCREMENT, " +
                    $"`{BadgeProps.Title}` varchar(100) COLLATE utf8mb4_unicode_ci NOT NULL, " +
                    $"`{BadgeProps.Description}` text COLLATE utf8mb4_unicode_ci NOT NULL, " +
                    $"`{BadgeProps.IsPublic}` tinyint(1) NOT NULL DEFAULT 1, " +
                    $"`{BadgeProps.CreatedAt}` datetime NOT NULL, " +
                    $"PRIMARY KEY (`{BadgeProps.Id}`), " +
                    $"UNIQUE KEY `{BadgeProps.Title}` (`{BadgeProps.Title}`) " +
                    $") ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci");

                // Create user_badges table
                c.ExecuteSql(
                    $"CREATE TABLE `{UserBadgeProps.TableName}` (" +
                    $"`{UserBadgeProps.Id}` int(11) UNSIGNED NOT NULL AUTO_INCREMENT, " +
                    $"`{UserBadgeProps.UserID}` varchar(32) COLLATE utf8mb4_unicode_ci NOT NULL, " +
                    $"`{UserBadgeProps.BadgeId}` int(11) UNSIGNED NOT NULL, " +
                    $"`{UserBadgeProps.AwardedAt}` datetime NOT NULL, " +
                    $"`{UserBadgeProps.AwardedBy}` varchar(32) COLLATE utf8mb4_unicode_ci NOT NULL, " +
                    $"PRIMARY KEY (`{UserBadgeProps.Id}`), " +
                    $"UNIQUE KEY `user_badge_unique` (`{UserBadgeProps.UserID}`, `{UserBadgeProps.BadgeId}`), " +
                    $"KEY `{UserBadgeProps.BadgeId}` (`{UserBadgeProps.BadgeId}`), " +
                    $"CONSTRAINT `user_badges_ibfk_1` FOREIGN KEY (`{UserBadgeProps.BadgeId}`) REFERENCES `{BadgeProps.TableName}` (`{BadgeProps.Id}`) ON DELETE CASCADE " +
                    $") ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci");

                await _logging.LogAction("DatabaseService: Badge tables generated without errors.",
                    ExtendedLogSeverity.Positive);
            }
            catch (Exception e)
            {
                await _logging.LogAction(
                    $"SQL Exception: Failed to generate badge tables.\nMessage: {e}",
                    ExtendedLogSeverity.Critical);
            }
        }
    }
}