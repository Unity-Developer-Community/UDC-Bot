using Newtonsoft.Json;

namespace DiscordBot.Domain;

public class CasinoUser
{
    public int Id { get; set; }
    public required string UserID { get; set; }
    public long Tokens { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public DateTime LastDailyReward { get; set; }
}

public class TokenTransaction
{
    public int Id { get; set; }
    public required string UserID { get; set; }
    public string? TargetUserID { get; set; }
    public long Amount { get; set; }
    public string TransactionType { get; set; } = "";

    // Computed from TransactionType string — not mapped to DB
    [JsonIgnore]
    public TransactionKind Kind
    {
        get => Enum.TryParse<TransactionKind>(TransactionType, true, out var result) ? result : TransactionKind.Admin;
        set => TransactionType = value.ToString();
    }

    private Dictionary<string, string>? _details;

    [JsonIgnore]
    public Dictionary<string, string>? Details
    {
        get => _details;
        set => _details = value;
    }

    // Maps to DB column "description" (text). Stores Details dict as JSON, deserializes with fallback for plain text (from MySQL migration)
    public string? Description
    {
        get => Details != null && Details.Any() ? JsonConvert.SerializeObject(Details) : null;
        set
        {
            if (string.IsNullOrEmpty(value))
            {
                _details = new Dictionary<string, string>();
                return;
            }
            try { _details = JsonConvert.DeserializeObject<Dictionary<string, string>>(value); }
            catch (JsonException) { _details = new Dictionary<string, string> { ["text"] = value }; }
        }
    }

    public DateTime CreatedAt { get; set; }
}

public enum TransactionKind
{
    TokenInitialisation,
    DailyReward,
    Gift,
    Game,
    Admin,
}

public class GameStatistics
{
    public string? GameName { get; set; }
    public int TotalGames { get; set; }
    public int Wins { get; set; }
    public int Losses { get; set; }
    public double WinPercentage { get; set; }
    public long TotalWinAmount { get; set; }
    public long TotalLossAmount { get; set; }
    public long NetProfit { get; set; }
    public double AverageProfit { get; set; }
}

public class GameLeaderboardEntry
{
    public required string UserID { get; set; }
    public int TotalGames { get; set; }
    public int Wins { get; set; }
    public int Losses { get; set; }
    public double WinPercentage { get; set; }
    public long NetProfit { get; set; }
    public double Score { get; set; }
    public string? GameName { get; set; } // null for global leaderboard
}

public class GameLeaderboardResult
{
    public List<GameLeaderboardEntry> Entries { get; set; } = [];
    public int TotalPlayers { get; set; }
    public int? CurrentUserRank { get; set; } // 1-based rank, null if user not present
    public GameLeaderboardEntry? CurrentUserEntry { get; set; }
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
    public const string TransactionType = nameof(TokenTransaction.TransactionType);
    public const string Details = nameof(TokenTransaction.Description);
    public const string TransactionCreatedAt = nameof(TokenTransaction.CreatedAt);
}