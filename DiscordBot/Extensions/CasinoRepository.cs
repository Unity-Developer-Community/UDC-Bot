using DiscordBot.Domain;
using Insight.Database;

namespace DiscordBot.Extensions;

public interface ICasinoRepo
{
    // Casino User Operations
    [Sql($@"
    INSERT INTO {CasinoProps.CasinoTableName} ({CasinoProps.UserID}, {CasinoProps.Tokens}, {CasinoProps.CreatedAt}, {CasinoProps.UpdatedAt}, {CasinoProps.LastDailyReward}) 
    VALUES (@{CasinoProps.UserID}, @{CasinoProps.Tokens}, @{CasinoProps.CreatedAt}, @{CasinoProps.UpdatedAt}, @{CasinoProps.LastDailyReward});
    SELECT * FROM {CasinoProps.CasinoTableName} WHERE {CasinoProps.UserID} = @{CasinoProps.UserID}")]
    Task<CasinoUser> InsertCasinoUser(CasinoUser user);

    [Sql($"SELECT * FROM {CasinoProps.CasinoTableName} WHERE {CasinoProps.UserID} = @userId")]
    Task<CasinoUser> GetCasinoUser(string userId);

    [Sql($"SELECT * FROM {CasinoProps.CasinoTableName} ORDER BY {CasinoProps.Tokens} DESC LIMIT @limit")]
    Task<IList<CasinoUser>> GetTopTokenHolders(int limit);

    [Sql($"UPDATE {CasinoProps.CasinoTableName} SET {CasinoProps.Tokens} = @tokens, {CasinoProps.UpdatedAt} = @updatedAt WHERE {CasinoProps.UserID} = @userId")]
    Task UpdateTokens(string userId, ulong tokens, DateTime updatedAt);

    [Sql($"UPDATE {CasinoProps.CasinoTableName} SET {CasinoProps.Tokens} = @tokens, {CasinoProps.UpdatedAt} = @updatedAt, {CasinoProps.LastDailyReward} = @lastDailyReward WHERE {CasinoProps.UserID} = @userId")]
    Task UpdateTokensAndDailyReward(string userId, ulong tokens, DateTime updatedAt, DateTime lastDailyReward);

    [Sql($"DELETE FROM {CasinoProps.CasinoTableName} WHERE {CasinoProps.UserID} = @userId")]
    Task DeleteCasinoUser(string userId);

    [Sql($"DELETE FROM {CasinoProps.CasinoTableName}")]
    Task ClearAllCasinoUsers();

    // Token Transaction Operations
    [Sql($@"
    INSERT INTO {CasinoProps.TransactionTableName} ({CasinoProps.TransactionUserID}, {CasinoProps.Amount}, {CasinoProps.TransactionType}, {CasinoProps.Details}, {CasinoProps.TransactionCreatedAt}) 
    VALUES (@{CasinoProps.TransactionUserID}, @{CasinoProps.Amount}, @{CasinoProps.TransactionType}, @{CasinoProps.Details}, @{CasinoProps.TransactionCreatedAt});
    SELECT * FROM {CasinoProps.TransactionTableName} WHERE {CasinoProps.TransactionId} = LAST_INSERT_ID()")]
    Task<TokenTransaction> InsertTransaction(TokenTransaction tokenTransaction);

    [Sql($"SELECT * FROM {CasinoProps.TransactionTableName} WHERE {CasinoProps.TransactionUserID} = @userId ORDER BY {CasinoProps.TransactionCreatedAt} DESC LIMIT @limit")]
    Task<IList<TokenTransaction>> GetUserTransactionHistory(string userId, int limit);

    [Sql($"SELECT * FROM {CasinoProps.TransactionTableName} ORDER BY {CasinoProps.TransactionCreatedAt} DESC LIMIT @limit")]
    Task<IList<TokenTransaction>> GetRecentTransactions(int limit);

    [Sql($"DELETE FROM {CasinoProps.TransactionTableName}")]
    Task ClearAllTransactions();

    [Sql($"SELECT * FROM {CasinoProps.TransactionTableName} WHERE {CasinoProps.TransactionType} = 'Game' ORDER BY {CasinoProps.TransactionCreatedAt} DESC")]
    Task<IList<TokenTransaction>> GetAllGameTransactions();

    // Test connection
    [Sql($"SELECT COUNT(*) FROM {CasinoProps.CasinoTableName}")]
    Task<long> TestCasinoConnection();
}