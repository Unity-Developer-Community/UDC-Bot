using System.Data.Common;
using Discord.WebSocket;
using DiscordBot.Domain;
using DiscordBot.Settings;
using Insight.Database;
using Insight.Database.Providers.PostgreSQL;
using Npgsql;

namespace DiscordBot.Services;

public class DatabaseService
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

    public DatabaseService(ILoggingService logging, BotSettings settings)
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

        Task.Run(async () =>
        {
            // Test connection, if it fails we create the table and set keys
            try
            {
                var userCount = await Query.TestConnection();
                await _logging.LogAction(
                    $"{ServiceName}: Connected to database successfully. {userCount} users in database.",
                    ExtendedLogSeverity.Positive);

                var defaultCityExists = await c.ColumnExists(UserProps.TableName, UserProps.DefaultCity);
                if (!defaultCityExists)
                {
                    c.ExecuteSql($"ALTER TABLE {UserProps.TableName} ADD COLUMN {UserProps.DefaultCity} varchar(64) DEFAULT NULL");
                    await _logging.LogAction($"DatabaseService: Added missing column '{UserProps.DefaultCity}' to table '{UserProps.TableName}'.",
                        ExtendedLogSeverity.Positive);
                }
            }
            catch
            {
                await _logging.LogAction($"DatabaseService: Table '{UserProps.TableName}' does not exist, attempting to generate table.",
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
                        $"{UserProps.DefaultCity} varchar(64) DEFAULT NULL)");
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
                        $"SQL Exception: Failed to generate casino tables.\nMessage: {e}",
                        ExtendedLogSeverity.Critical);
                    c.Close();
                    return;
                }
                await _logging.LogAction($"DatabaseService: Casino tables generated without errors.",
                    ExtendedLogSeverity.Positive);
                c.Close();
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
}