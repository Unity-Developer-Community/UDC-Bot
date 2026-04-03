using System.Data.Common;
using Insight.Database;
using Npgsql;

namespace DiscordBot.Extensions;

public static class DBConnectionExtension
{
    public static async Task<bool> ColumnExists(this DbConnection connection, string tableName, string columnName)
    {
        const string query = "SELECT 1 FROM information_schema.columns WHERE LOWER(table_name) = LOWER(@tableName) AND LOWER(column_name) = LOWER(@columnName)";
        var response = await connection.QuerySqlAsync(query, new { tableName, columnName });
        return response.Count > 0;
    }
}