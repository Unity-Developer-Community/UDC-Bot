namespace DiscordBot.Domain;

public class CasinoUser
{
    public int Id { get; set; }
    public string UserID { get; set; }
    public ulong Tokens { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public DateTime LastDailyReward { get; set; }
}

public class TokenTransaction
{
    public int Id { get; set; }
    public string UserID { get; set; }
    public string TargetUserID { get; set; } // For gifts, null for other transactions
    public long Amount { get; set; } // Can be negative for spending
    public TransactionType Type { get; set; } // Enum for transaction types
    public DateTime CreatedAt { get; set; }
}

public enum TransactionType
{
    TokenInitialisation,
    DailyReward,
    Gift,
    Game,
    AdminAdd,
    AdminSet
}

public static class CasinoProps
{
    public const string CasinoTableName = "casino_users";
    public const string TransactionTableName = "token_transactions";

    // CasinoUser properties
    public const string Id = nameof(CasinoUser.Id);
    public const string UserID = nameof(CasinoUser.UserID);
    public const string Tokens = nameof(CasinoUser.Tokens);
    public const string CreatedAt = nameof(CasinoUser.CreatedAt);
    public const string UpdatedAt = nameof(CasinoUser.UpdatedAt);
    public const string LastDailyReward = nameof(CasinoUser.LastDailyReward);

    // TokenTransaction properties
    public const string TransactionId = nameof(TokenTransaction.Id);
    public const string TransactionUserID = nameof(TokenTransaction.UserID);
    public const string TargetUserID = nameof(TokenTransaction.TargetUserID);
    public const string Amount = nameof(TokenTransaction.Amount);
    public const string TransactionType = nameof(TokenTransaction.Type);
    public const string TransactionCreatedAt = nameof(TokenTransaction.CreatedAt);
}