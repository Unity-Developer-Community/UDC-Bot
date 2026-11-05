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

    // The statements below change a balance relative to its current value inside one UPDATE, so PostgreSQL's row lock
    // serializes concurrent changes. Never read a balance and write back a total: the second writer would erase the first.

    /// <summary>Adds <paramref name="delta"/> (negative to subtract), stopping at 0.</summary>
    /// <returns>The new balance, <c>null</c> when the user has no row.</returns>
    [Sql($"UPDATE {CasinoProps.CasinoTableName} SET {CasinoProps.Tokens} = GREATEST({CasinoProps.Tokens} + @delta, 0), {CasinoProps.UpdatedAt} = @updatedAt WHERE {CasinoProps.UserID} = @userId RETURNING {CasinoProps.Tokens}")]
    Task<long?> AdjustTokens(string userId, long delta, DateTime updatedAt);

    /// <summary>Subtracts <paramref name="amount"/> only when the balance covers it.</summary>
    /// <returns>The new balance, <c>null</c> when the balance is too low or the user has no row.</returns>
    [Sql($"UPDATE {CasinoProps.CasinoTableName} SET {CasinoProps.Tokens} = {CasinoProps.Tokens} - @amount, {CasinoProps.UpdatedAt} = @updatedAt WHERE {CasinoProps.UserID} = @userId AND {CasinoProps.Tokens} >= @amount RETURNING {CasinoProps.Tokens}")]
    Task<long?> TryDebitTokens(string userId, long amount, DateTime updatedAt);

    /// <summary>
    /// Moves tokens between two different users in one statement: the receiver is credited only if the sender's debit happened,
    /// and the debit only happens when both rows exist. Both rows are locked first, in user ID order, so two gifts in opposite
    /// directions cannot each hold one row and wait for the other (a deadlock PostgreSQL would resolve by failing one of them).
    /// </summary>
    /// <returns>The receiver's new balance, <c>null</c> when nothing moved.</returns>
    [Sql($@"
    WITH locked AS (
        SELECT {CasinoProps.UserID} FROM {CasinoProps.CasinoTableName}
        WHERE {CasinoProps.UserID} IN (@fromUserId, @toUserId)
        ORDER BY {CasinoProps.UserID}
        FOR UPDATE),
    debit AS (
        UPDATE {CasinoProps.CasinoTableName}
        SET {CasinoProps.Tokens} = {CasinoProps.Tokens} - @amount, {CasinoProps.UpdatedAt} = @updatedAt
        WHERE {CasinoProps.UserID} = @fromUserId AND {CasinoProps.Tokens} >= @amount
          AND (SELECT COUNT(*) FROM locked) = 2
        RETURNING 1)
    UPDATE {CasinoProps.CasinoTableName}
    SET {CasinoProps.Tokens} = {CasinoProps.Tokens} + @amount, {CasinoProps.UpdatedAt} = @updatedAt
    WHERE {CasinoProps.UserID} = @toUserId AND EXISTS (SELECT 1 FROM debit)
    RETURNING {CasinoProps.Tokens}")]
    Task<long?> TransferTokens(string fromUserId, string toUserId, long amount, DateTime updatedAt);

    /// <summary>Pays the reward only when the last claim is at or before <paramref name="eligibleBefore"/>, so a double claim pays once.</summary>
    /// <returns>The new balance, <c>null</c> when the reward is not available yet.</returns>
    [Sql($"UPDATE {CasinoProps.CasinoTableName} SET {CasinoProps.Tokens} = {CasinoProps.Tokens} + @reward, {CasinoProps.UpdatedAt} = @now, {CasinoProps.LastDailyReward} = @now WHERE {CasinoProps.UserID} = @userId AND {CasinoProps.LastDailyReward} <= @eligibleBefore RETURNING {CasinoProps.Tokens}")]
    Task<long?> ClaimDailyReward(string userId, long reward, DateTime now, DateTime eligibleBefore);

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
    Task<CasinoJackpot?> GetJackpot();

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