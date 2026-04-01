using System.Data.Common;
using Insight.Database;

namespace DiscordBot.Extensions;

public static class DBConnectionExtension
{
    public static async Task<bool> ColumnExists(this DbConnection connection, string tableName, string columnName)
    {
        var query = $"SELECT 1 FROM information_schema.columns WHERE table_name = '{tableName}' AND column_name = '{columnName}'";
        var response = await connection.QuerySqlAsync(query);
        return response.Count > 0;
    }
}