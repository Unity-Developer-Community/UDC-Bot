using DiscordBot.Domain;
using Insight.Database;

namespace DiscordBot.Extensions;

public interface ICasinoRepo
{
    // Casino User Operations
    [Sql($@"
    INSERT INTO {CasinoProps.CasinoTableName} ({CasinoProps.UserID}, {CasinoProps.Tokens}, {CasinoProps.CreatedAt}, {CasinoProps.UpdatedAt}, {CasinoProps.LastDailyReward}) 
    VALUES (@{CasinoProps.UserID}, @{CasinoProps.Tokens}, @{CasinoProps.CreatedAt}, @{CasinoProps.UpdatedAt}, @{CasinoProps.LastDailyReward})
    RETURNING *")]
    Task<CasinoUser> InsertCasinoUser(CasinoUser user);

    [Sql($"SELECT * FROM {CasinoProps.CasinoTableName} WHERE {CasinoProps.UserID} = @userId")]
    Task<CasinoUser> GetCasinoUser(string userId);

    [Sql($"SELECT * FROM {CasinoProps.CasinoTableName} ORDER BY {CasinoProps.Tokens} DESC LIMIT @limit")]
    Task<IList<CasinoUser>> GetTopTokenHolders(int limit);

    [Sql($"UPDATE {CasinoProps.CasinoTableName} SET {CasinoProps.Tokens} = @tokens, {CasinoProps.UpdatedAt} = @updatedAt WHERE {CasinoProps.UserID} = @userId")]
    Task UpdateTokens(string userId, long tokens, DateTime updatedAt);

    [Sql($"UPDATE {CasinoProps.CasinoTableName} SET {CasinoProps.Tokens} = @tokens, {CasinoProps.UpdatedAt} = @updatedAt, {CasinoProps.LastDailyReward} = @lastDailyReward WHERE {CasinoProps.UserID} = @userId")]
    Task UpdateTokensAndDailyReward(string userId, long tokens, DateTime updatedAt, DateTime lastDailyReward);

    [Sql($"DELETE FROM {CasinoProps.CasinoTableName} WHERE {CasinoProps.UserID} = @userId")]
    Task DeleteCasinoUser(string userId);

    [Sql($"DELETE FROM {CasinoProps.CasinoTableName}")]
    Task ClearAllCasinoUsers();

    // Token Transaction Operations
    [Sql($@"
    INSERT INTO {CasinoProps.TransactionTableName} ({CasinoProps.TransactionUserID}, {CasinoProps.TargetUserID}, {CasinoProps.Amount}, {CasinoProps.TransactionType}, {CasinoProps.Details}, {CasinoProps.TransactionCreatedAt}) 
    VALUES (@{CasinoProps.TransactionUserID}, @{CasinoProps.TargetUserID}, @{CasinoProps.Amount}, @{CasinoProps.TransactionType}, @{CasinoProps.Details}, @{CasinoProps.TransactionCreatedAt})
    RETURNING *")]
    Task<TokenTransaction> InsertTransaction(TokenTransaction tokenTransaction);

    [Sql($"SELECT * FROM {CasinoProps.TransactionTableName} WHERE {CasinoProps.TransactionUserID} = @userId ORDER BY {CasinoProps.TransactionCreatedAt} DESC LIMIT @limit")]
    Task<IList<TokenTransaction>> GetUserTransactionHistory(string userId, int limit);

    [Sql($"SELECT * FROM {CasinoProps.TransactionTableName} ORDER BY {CasinoProps.TransactionCreatedAt} DESC LIMIT @limit")]
    Task<IList<TokenTransaction>> GetRecentTransactions(int limit);

    [Sql($"DELETE FROM {CasinoProps.TransactionTableName}")]
    Task ClearAllTransactions();

    [Sql($"SELECT * FROM {CasinoProps.TransactionTableName} WHERE {CasinoProps.TransactionType} = @transactionType ORDER BY {CasinoProps.TransactionCreatedAt} DESC")]
    Task<IList<TokenTransaction>> GetTransactionsOfType(string transactionType);

    // Slots jackpot (amounts in hundredths of a token)
    [Sql($"SELECT * FROM {JackpotProps.TableName} WHERE {JackpotProps.Id} = {JackpotProps.RowId}")]
    Task<CasinoJackpot> GetJackpot();

    /// <returns>The pot after the contribution.</returns>
    [Sql($@"
    UPDATE {JackpotProps.TableName}
    SET {JackpotProps.Amount} = {JackpotProps.Amount} + @contributionHundredths, {JackpotProps.UpdatedAt} = @updatedAt
    WHERE {JackpotProps.Id} = {JackpotProps.RowId}
    RETURNING {JackpotProps.Amount}")]
    Task<long> AddJackpotContribution(long contributionHundredths, DateTime updatedAt);

    /// <summary>
    /// Takes bet/100 of the pot, rounded down to whole tokens (same math as <c>SlotSettlementMath.JackpotShare</c>),
    /// and tops the rest up to the floor. One statement: the row is locked, so two claims never read the same pot.
    /// </summary>
    /// <returns>The share taken, in hundredths of a token.</returns>
    [Sql($@"
    UPDATE {JackpotProps.TableName} AS j
    SET {JackpotProps.Amount} = GREATEST(old.{JackpotProps.Amount} - (old.{JackpotProps.Amount} * @bet / 10000) * 100, @floorHundredths),
        {JackpotProps.UpdatedAt} = @updatedAt
    FROM (SELECT {JackpotProps.Amount} FROM {JackpotProps.TableName} WHERE {JackpotProps.Id} = {JackpotProps.RowId} FOR UPDATE) AS old
    WHERE j.{JackpotProps.Id} = {JackpotProps.RowId}
    RETURNING (old.{JackpotProps.Amount} * @bet / 10000) * 100")]
    Task<long> ClaimJackpotShare(int bet, long floorHundredths, DateTime updatedAt);

    // Test connection
    [Sql($"SELECT COUNT(*) FROM {CasinoProps.CasinoTableName}")]
    Task<long> TestCasinoConnection();
}