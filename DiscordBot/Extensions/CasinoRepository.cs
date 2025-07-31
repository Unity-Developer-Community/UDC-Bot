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

    [Sql($"SELECT * FROM {CasinoProps.TransactionTableName} WHERE {CasinoProps.TransactionType} = @transactionType ORDER BY {CasinoProps.TransactionCreatedAt} DESC")]
    Task<IList<TokenTransaction>> GetTransactionsOfType(TransactionType transactionType);

    // Test connection
    [Sql($"SELECT COUNT(*) FROM {CasinoProps.CasinoTableName}")]
    Task<long> TestCasinoConnection();

    // Shop Item Operations
    [Sql($@"
    INSERT INTO {CasinoProps.ShopItemsTableName} ({CasinoProps.ShopItemTitle}, {CasinoProps.ShopItemDescription}, {CasinoProps.ShopItemPrice}, {CasinoProps.ShopItemCreatedAt}) 
    VALUES (@{CasinoProps.ShopItemTitle}, @{CasinoProps.ShopItemDescription}, @{CasinoProps.ShopItemPrice}, @{CasinoProps.ShopItemCreatedAt});
    SELECT * FROM {CasinoProps.ShopItemsTableName} WHERE {CasinoProps.ShopItemId} = LAST_INSERT_ID()")]
    Task<ShopItem> InsertShopItem(ShopItem item);

    [Sql($"SELECT * FROM {CasinoProps.ShopItemsTableName} ORDER BY {CasinoProps.ShopItemPrice} ASC")]
    Task<IList<ShopItem>> GetAllShopItems();

    [Sql($"SELECT * FROM {CasinoProps.ShopItemsTableName} WHERE {CasinoProps.ShopItemId} = @itemId")]
    Task<ShopItem> GetShopItem(int itemId);

    [Sql($"DELETE FROM {CasinoProps.ShopItemsTableName}")]
    Task ClearAllShopItems();

    // Shop Purchase Operations
    [Sql($@"
    INSERT INTO {CasinoProps.ShopPurchasesTableName} ({CasinoProps.ShopPurchaseUserID}, {CasinoProps.ShopPurchaseItemId}, {CasinoProps.ShopPurchaseDate}) 
    VALUES (@{CasinoProps.ShopPurchaseUserID}, @{CasinoProps.ShopPurchaseItemId}, @{CasinoProps.ShopPurchaseDate});
    SELECT * FROM {CasinoProps.ShopPurchasesTableName} WHERE {CasinoProps.ShopPurchaseId} = LAST_INSERT_ID()")]
    Task<ShopPurchase> InsertShopPurchase(ShopPurchase purchase);

    [Sql($"SELECT * FROM {CasinoProps.ShopPurchasesTableName} WHERE {CasinoProps.ShopPurchaseUserID} = @userId")]
    Task<IList<ShopPurchase>> GetUserShopPurchases(string userId);

    [Sql($"SELECT COUNT(*) FROM {CasinoProps.ShopPurchasesTableName} WHERE {CasinoProps.ShopPurchaseUserID} = @userId AND {CasinoProps.ShopPurchaseItemId} = @itemId")]
    Task<long> CheckUserHasItem(string userId, int itemId);

    [Sql($"DELETE FROM {CasinoProps.ShopPurchasesTableName}")]
    Task ClearAllShopPurchases();
}