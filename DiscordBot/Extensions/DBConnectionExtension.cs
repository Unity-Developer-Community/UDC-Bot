using System.Data.Common;
using Insight.Database;

namespace DiscordBot.Extensions;

public static class DBConnectionExtension
{
    public static async Task<bool> ColumnExists(this DbConnection connection, string tableName, string columnName)
    {
        // Execute the query `SHOW COLUMNS FROM `{tableName}` LIKE '{columnName}'` and check if any rows are returned
        var query = $"SHOW COLUMNS FROM `{tableName}` LIKE '{columnName}'";
        var response = await connection.QuerySqlAsync(query);
        return response.Count > 0;
    }
}