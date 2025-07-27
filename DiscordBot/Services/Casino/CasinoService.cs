using DiscordBot.Domain;
using DiscordBot.Settings;

namespace DiscordBot.Services;

public class CasinoService
{
    private const string ServiceName = "CasinoService";

    private readonly DatabaseService _databaseService;
    private readonly ILoggingService _loggingService;
    private readonly BotSettings _settings;

    public CasinoService(DatabaseService databaseService, ILoggingService loggingService, BotSettings settings)
    {
        _databaseService = databaseService;
        _loggingService = loggingService;
        _settings = settings;
    }

    #region Token Management

    public async Task<CasinoUser> GetOrCreateCasinoUser(string userId)
    {
        try
        {
            var user = await _databaseService.CasinoQuery.GetCasinoUser(userId);
            if (user != null)
                return user;

            // Create new casino user with starting tokens
            var newUser = new CasinoUser
            {
                UserID = userId,
                Tokens = _settings.CasinoStartingTokens,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
                LastDailyReward = DateTime.UtcNow.AddDays(-1) // Set to a past date so user can claim their first daily reward immediately
            };

            var createdUser = await _databaseService.CasinoQuery.InsertCasinoUser(newUser);
            await RecordTransaction(userId, (long)_settings.CasinoStartingTokens, TransactionType.TokenInitialisation);
            await _loggingService.LogChannelAndFile($"{ServiceName}: Created new casino user {userId} with {_settings.CasinoStartingTokens} starting tokens");
            return createdUser;
        }
        catch (Exception ex)
        {
            await _loggingService.LogChannelAndFile($"{ServiceName}: ERROR in GetOrCreateCasinoUser for userId {userId}: {ex.Message}", ExtendedLogSeverity.Error);
            await _loggingService.LogChannelAndFile($"{ServiceName}: GetOrCreateCasinoUser Exception Details: {ex}");
            throw new InvalidOperationException($"Failed to get or create casino user: {ex.Message}", ex);
        }
    }

    public async Task<bool> TransferTokens(string fromUserId, string toUserId, ulong amount)
    {
        var fromUser = await GetOrCreateCasinoUser(fromUserId);
        var toUser = await GetOrCreateCasinoUser(toUserId);

        if (fromUser.Tokens < amount)
            return false;

        // Update balances
        await _databaseService.CasinoQuery.UpdateTokens(fromUserId, fromUser.Tokens - amount, DateTime.UtcNow);
        await _databaseService.CasinoQuery.UpdateTokens(toUserId, toUser.Tokens + amount, DateTime.UtcNow);

        // Record transactions
        await RecordTransaction(fromUserId, -(long)amount, TransactionType.Gift, new Dictionary<string, string>
        {
            ["to"] = toUserId,
        });
        await RecordTransaction(toUserId, (long)amount, TransactionType.Gift, new Dictionary<string, string>
        {
            ["from"] = fromUserId
        });

        return true;
    }

    public async Task<bool> UpdateUserTokens(string userId, long deltaTokens, TransactionType transactionType, Dictionary<string, string>? details = null)
    {
        try
        {
            var user = await GetOrCreateCasinoUser(userId);
            var newBalance = (long)user.Tokens + deltaTokens;

            if (newBalance < 0)
            {
                return false;
            }

            await _databaseService.CasinoQuery.UpdateTokens(userId, (ulong)newBalance, DateTime.UtcNow);
            await RecordTransaction(userId, deltaTokens, transactionType, details);

            return true;
        }
        catch (Exception ex)
        {
            await _loggingService.LogChannelAndFile($"{ServiceName}: ERROR in UpdateUserTokens for userId {userId}: {ex.Message}", ExtendedLogSeverity.Error);
            await _loggingService.LogChannelAndFile($"{ServiceName}: UpdateUserTokens Exception Details: {ex}");
            throw new InvalidOperationException($"Failed to update user tokens: {ex.Message}", ex);
        }
    }

    public async Task SetUserTokens(string userId, ulong amount, string adminUserId)
    {
        await _databaseService.CasinoQuery.UpdateTokens(userId, amount, DateTime.UtcNow);

        await RecordTransaction(userId, (long)amount, TransactionType.Admin, new Dictionary<string, string>
        {
            ["admin"] = adminUserId,
            ["action"] = "set"
        });
    }

    public async Task<List<CasinoUser>> GetLeaderboard(int limit = 10)
    {
        var topUsers = await _databaseService.CasinoQuery.GetTopTokenHolders(limit);
        return topUsers.ToList();
    }

    public async Task<List<TokenTransaction>> GetUserTransactionHistory(string userId, int limit = 10)
    {
        await GetOrCreateCasinoUser(userId);
        var transactions = await _databaseService.CasinoQuery.GetUserTransactionHistory(userId, limit);
        return transactions.ToList();
    }

    public async Task<List<TokenTransaction>> GetAllRecentTransactions(int limit = 10)
    {
        var transactions = await _databaseService.CasinoQuery.GetRecentTransactions(limit);
        return transactions.ToList();
    }

    private async Task RecordTransaction(string userId, long amount, TransactionType type, Dictionary<string, string>? details = null)
    {
        var transaction = new TokenTransaction
        {
            UserID = userId,
            Amount = amount,
            Type = type,
            CreatedAt = DateTime.UtcNow,
            Details = details
        };

        await _databaseService.CasinoQuery.InsertTransaction(transaction);
    }

    #endregion

    #region Game Statistics

    public async Task<List<GameStatistics>> GetGameStatistics(IUser user)
    {
        try
        {
            var gameTransactions = await _databaseService.CasinoQuery.GetTransactionsOfType(TransactionType.Game);

            // Group transactions by game type
            var gameGroups = gameTransactions
                .Where(t => t.UserID == user.Id.ToString())
                .Where(t => t.Details != null && t.Details.ContainsKey("game"))
                .GroupBy(t => t.Details?["game"])
                .ToList();

            var statistics = new List<GameStatistics>();

            foreach (var group in gameGroups)
            {
                var gameName = group.Key;
                var transactions = group.ToList();

                var wins = transactions.Where(t => t.Amount > 0).ToList();
                var losses = transactions.Where(t => t.Amount < 0).ToList();

                var totalWins = wins.Count;
                var totalLosses = losses.Count;
                var totalGames = totalWins + totalLosses;

                var winPercentage = totalGames > 0 ? (double)totalWins / totalGames * 100 : 0;

                var totalWinAmount = wins.Sum(t => t.Amount);
                var totalLossAmount = losses.Sum(t => Math.Abs(t.Amount)); // Make positive for display
                var netProfit = totalWinAmount - totalLossAmount;
                var averageProfit = totalGames > 0 ? (double)netProfit / totalGames : 0;

                statistics.Add(new GameStatistics
                {
                    GameName = gameName,
                    TotalGames = totalGames,
                    Wins = totalWins,
                    Losses = totalLosses,
                    WinPercentage = winPercentage,
                    TotalWinAmount = totalWinAmount,
                    TotalLossAmount = totalLossAmount,
                    NetProfit = netProfit,
                    AverageProfit = averageProfit
                });
            }

            return statistics.OrderByDescending(s => s.TotalGames).ToList();
        }
        catch (Exception ex)
        {
            await _loggingService.LogChannelAndFile($"{ServiceName}: ERROR in GetGameStatistics: {ex.Message}", ExtendedLogSeverity.Error);
            await _loggingService.LogChannelAndFile($"{ServiceName}: GetGameStatistics Exception Details: {ex}");
            throw new InvalidOperationException($"Failed to get game statistics: {ex.Message}", ex);
        }
    }

    public async Task<List<GameLeaderboardEntry>> GetGameLeaderboard(string? gameName = null, int limit = 10)
    {
        try
        {
            var gameTransactions = await _databaseService.CasinoQuery.GetTransactionsOfType(TransactionType.Game);

            // Filter by game if specified
            var filteredTransactions = gameTransactions
                .Where(t => t.Details != null && t.Details.ContainsKey("game"))
                .Where(t => gameName == null || t.Details["game"].Equals(gameName, StringComparison.OrdinalIgnoreCase))
                .ToList();

            // Group by user and calculate statistics
            var userGroups = filteredTransactions
                .GroupBy(t => t.UserID)
                .ToList();

            var leaderboardEntries = new List<GameLeaderboardEntry>();

            foreach (var userGroup in userGroups)
            {
                var userId = userGroup.Key;
                var userTransactions = userGroup.ToList();

                // Calculate overall stats for this user (across all games if gameName is null)
                var wins = userTransactions.Where(t => t.Amount > 0).ToList();
                var losses = userTransactions.Where(t => t.Amount < 0).ToList();

                var totalWins = wins.Count;
                var totalLosses = losses.Count;
                var totalGames = totalWins + totalLosses;

                if (totalGames == 0) continue; // Skip users with no games

                var winPercentage = (double)totalWins / totalGames * 100;
                var totalWinAmount = wins.Sum(t => t.Amount);
                var totalLossAmount = losses.Sum(t => Math.Abs(t.Amount)); // Make positive for calculation
                var netProfit = totalWinAmount - totalLossAmount;

                // Calculate score using the suggested formula: winrate × log10(totalGames)
                // Use log10(totalGames + 1) to handle the case where totalGames = 1 (log10(1) = 0)
                var score = winPercentage * Math.Log10(totalGames + 1);

                leaderboardEntries.Add(new GameLeaderboardEntry
                {
                    UserID = userId,
                    TotalGames = totalGames,
                    Wins = totalWins,
                    Losses = totalLosses,
                    WinPercentage = winPercentage,
                    NetProfit = netProfit,
                    Score = score,
                    GameName = gameName // null for global leaderboard
                });
            }

            // Sort by score descending and take the top entries
            return leaderboardEntries
                .OrderByDescending(entry => entry.Score)
                .Take(limit)
                .ToList();
        }
        catch (Exception ex)
        {
            await _loggingService.LogChannelAndFile($"{ServiceName}: ERROR in GetGameLeaderboard: {ex.Message}", ExtendedLogSeverity.Error);
            await _loggingService.LogChannelAndFile($"{ServiceName}: GetGameLeaderboard Exception Details: {ex}");
            throw new InvalidOperationException($"Failed to get game leaderboard: {ex.Message}", ex);
        }
    }

    #endregion

    #region Channel Permissions

    public bool IsChannelAllowed(ulong channelId)
    {
        if (!_settings.CasinoEnabled)
            return false;

        if (_settings.CasinoAllowedChannels.Count == 0)
            return true; // If no restrictions, allow all channels

        return _settings.CasinoAllowedChannels.Contains(channelId);
    }

    #endregion

    #region Daily Rewards

    public async Task<(bool success, ulong tokensAwarded, ulong newBalance, DateTime nextRewardTime)> TryClaimDailyReward(string userId)
    {
        try
        {
            var user = await GetOrCreateCasinoUser(userId);
            var now = DateTime.UtcNow;
            var nextRewardTime = user.LastDailyReward.AddSeconds(_settings.CasinoDailyRewardIntervalSeconds);

            if (now < nextRewardTime)
            {
                return (false, 0, user.Tokens, nextRewardTime);
            }

            // User can claim daily reward
            var tokensAwarded = _settings.CasinoDailyRewardTokens;
            var newBalance = user.Tokens + tokensAwarded;
            await _databaseService.CasinoQuery.UpdateTokensAndDailyReward(userId, newBalance, now, now);
            await RecordTransaction(userId, (long)tokensAwarded, TransactionType.DailyReward);

            await _loggingService.LogChannelAndFile($"{ServiceName}: User {userId} claimed daily reward of {tokensAwarded} tokens");
            return (true, tokensAwarded, newBalance, now.AddSeconds(_settings.CasinoDailyRewardIntervalSeconds));
        }
        catch (Exception ex)
        {
            await _loggingService.LogChannelAndFile($"{ServiceName}: ERROR in TryClaimDailyReward for userId {userId}: {ex.Message}", ExtendedLogSeverity.Error);
            await _loggingService.LogChannelAndFile($"{ServiceName}: TryClaimDailyReward Exception Details: {ex}");
            throw new InvalidOperationException($"Failed to claim daily reward: {ex.Message}", ex);
        }
    }

    public async Task<DateTime> GetNextDailyRewardTime(string userId)
    {
        var user = await GetOrCreateCasinoUser(userId);
        return user.LastDailyReward.AddSeconds(_settings.CasinoDailyRewardIntervalSeconds);
    }

    #endregion

    #region Admin Functions

    public async Task ResetAllCasinoData()
    {
        await _databaseService.CasinoQuery.ClearAllCasinoUsers();
        await _databaseService.CasinoQuery.ClearAllTransactions();

        await _loggingService.LogChannelAndFile($"{ServiceName}: All casino data has been reset.");
    }

    #endregion
}