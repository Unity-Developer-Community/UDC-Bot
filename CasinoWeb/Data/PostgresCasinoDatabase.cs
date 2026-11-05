using DiscordBot.Extensions;
using DiscordBot.Services;
using Insight.Database;
using Insight.Database.Providers.PostgreSQL;
using Npgsql;

namespace CasinoWeb.Data;

/// <summary>
/// The casino tables from a connection string alone. Unlike the bot's <c>DatabaseService</c> it never creates or alters
/// tables: the bot owns the schema.
/// </summary>
public sealed class PostgresCasinoDatabase : ICasinoDatabase
{
    private readonly string _connectionString;

    public PostgresCasinoDatabase(string connectionString)
    {
        PostgreSQLInsightDbProvider.RegisterProvider();
        _connectionString = connectionString;
    }

    public ICasinoRepo? CasinoQuery => new NpgsqlConnection(_connectionString).As<ICasinoRepo>();
}
