using Newtonsoft.Json;

namespace DiscordBot.Domain;

public class CasinoUser
{
    public int Id { get; set; }
    public required string UserID { get; set; }
    public ulong Tokens { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public DateTime LastDailyReward { get; set; }
}

public class TokenTransaction
{
    public int Id { get; set; }
    public required string UserID { get; set; }
    public long Amount { get; set; } // Can be negative for spending
    public TransactionType Type { get; set; } // Enum for transaction types

    private Dictionary<string, string>? _details;

    [JsonIgnore]
    public Dictionary<string, string>? Details
    {
        get => _details;
        set => _details = value;
    }

    // This property will be mapped to the database JSON column
    public string? DetailsJson
    {
        get => Details != null && Details.Any() ? JsonConvert.SerializeObject(Details) : null;
        set => Details = !string.IsNullOrEmpty(value) ? JsonConvert.DeserializeObject<Dictionary<string, string>>(value) : new Dictionary<string, string>();
    }

    public DateTime CreatedAt { get; set; }
}

public enum TransactionType
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
    public const string Amount = nameof(TokenTransaction.Amount);
    public const string TransactionType = nameof(TokenTransaction.Type);
    public const string Details = nameof(TokenTransaction.DetailsJson);
    public const string TransactionCreatedAt = nameof(TokenTransaction.CreatedAt);
}