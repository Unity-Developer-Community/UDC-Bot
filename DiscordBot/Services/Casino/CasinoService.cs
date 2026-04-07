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
            var casinoQuery = _databaseService.CasinoQuery ?? throw new InvalidOperationException("Casino database is not available");
            var user = await casinoQuery.GetCasinoUser(userId);
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

            var createdUser = await casinoQuery.InsertCasinoUser(newUser);
            await RecordTransaction(userId, _settings.CasinoStartingTokens, TransactionKind.TokenInitialisation);
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

    public async Task<bool> TransferTokens(string fromUserId, string toUserId, long amount)
    {
        var casinoQuery = _databaseService.CasinoQuery ?? throw new InvalidOperationException("Casino database is not available");
        var fromUser = await GetOrCreateCasinoUser(fromUserId);
        var toUser = await GetOrCreateCasinoUser(toUserId);

        if (fromUser.Tokens < amount)
            return false;

        // Update balances
        await casinoQuery.UpdateTokens(fromUserId, fromUser.Tokens - amount, DateTime.UtcNow);
        await casinoQuery.UpdateTokens(toUserId, toUser.Tokens + amount, DateTime.UtcNow);

        // Record transactions
        await RecordTransaction(fromUserId, -amount, TransactionKind.Gift, new Dictionary<string, string>
        {
            ["to"] = toUserId,
        });
        await RecordTransaction(toUserId, amount, TransactionKind.Gift, new Dictionary<string, string>
        {
            ["from"] = fromUserId
        });

        return true;
    }

    public async Task UpdateUserTokens(string userId, long deltaTokens, TransactionKind transactionType, Dictionary<string, string>? details = null)
    {
        try
        {
            var casinoQuery = _databaseService.CasinoQuery ?? throw new InvalidOperationException("Casino database is not available");
            var user = await GetOrCreateCasinoUser(userId);
            var newBalance = user.Tokens + deltaTokens;
            // Prevent negative balance
            if (newBalance < 0) newBalance = 0;

            await casinoQuery.UpdateTokens(userId, newBalance, DateTime.UtcNow);
            await RecordTransaction(userId, deltaTokens, transactionType, details);
        }
        catch (Exception ex)
        {
            await _loggingService.LogChannelAndFile($"{ServiceName}: ERROR in UpdateUserTokens for userId {userId}: {ex.Message}", ExtendedLogSeverity.Error);
            await _loggingService.LogChannelAndFile($"{ServiceName}: UpdateUserTokens Exception Details: {ex}");
            throw new InvalidOperationException($"Failed to update user tokens: {ex.Message}", ex);
        }
    }

    public async Task SetUserTokens(string userId, long amount, string adminUserId)
    {
        var casinoQuery = _databaseService.CasinoQuery ?? throw new InvalidOperationException("Casino database is not available");
        await casinoQuery.UpdateTokens(userId, amount, DateTime.UtcNow);

        await RecordTransaction(userId, amount, TransactionKind.Admin, new Dictionary<string, string>
        {
            ["admin"] = adminUserId,
            ["action"] = "set"
        });
    }

    public async Task<List<CasinoUser>> GetLeaderboard(int limit = 10)
    {
        var casinoQuery = _databaseService.CasinoQuery ?? throw new InvalidOperationException("Casino database is not available");
        var topUsers = await casinoQuery.GetTopTokenHolders(limit);
        return topUsers.ToList();
    }

    public async Task<List<TokenTransaction>> GetUserTransactionHistory(string userId, int limit = 10)
    {
        await GetOrCreateCasinoUser(userId);
        var casinoQuery = _databaseService.CasinoQuery ?? throw new InvalidOperationException("Casino database is not available");
        var transactions = await casinoQuery.GetUserTransactionHistory(userId, limit);
        return transactions.ToList();
    }

    public async Task<List<TokenTransaction>> GetAllRecentTransactions(int limit = 10)
    {
        var casinoQuery = _databaseService.CasinoQuery ?? throw new InvalidOperationException("Casino database is not available");
        var transactions = await casinoQuery.GetRecentTransactions(limit);
        return transactions.ToList();
    }

    private async Task RecordTransaction(string userId, long amount, TransactionKind type, Dictionary<string, string>? details = null)
    {
        var casinoQuery = _databaseService.CasinoQuery ?? throw new InvalidOperationException("Casino database is not available");
        var transaction = new TokenTransaction
        {
            UserID = userId,
            Amount = amount,
            Kind = type,
            CreatedAt = DateTime.UtcNow,
            Details = details
        };

        await casinoQuery.InsertTransaction(transaction);
    }

    #endregion

    #region Game Statistics

    public async Task<List<GameStatistics>> GetGameStatistics(IUser user)
    {
        try
        {
            var casinoQuery = _databaseService.CasinoQuery ?? throw new InvalidOperationException("Casino database is not available");
            var gameTransactions = await casinoQuery.GetTransactionsOfType(nameof(TransactionKind.Game));

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

    public async Task<GameLeaderboardResult> GetGameLeaderboard(string? gameName = null, int limit = 10, string? currentUserId = null)
    {
        try
        {
            var casinoQuery = _databaseService.CasinoQuery ?? throw new InvalidOperationException("Casino database is not available");
            var gameTransactions = await casinoQuery.GetTransactionsOfType(nameof(TransactionKind.Game));

            // Filter by game if specified
            var filteredTransactions = gameTransactions
                .Where(t => t.Details != null && t.Details.ContainsKey("game"))
                .Where(t =>
                {
                    if (gameName == null) return true;
                    var details = t.Details;
                    if (details == null) return false;
                    return details.TryGetValue("game", out var g) && g != null && g.Equals(gameName, StringComparison.OrdinalIgnoreCase);
                })
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

            // Sort all by score descending for rank computation
            var sortedAll = leaderboardEntries
                .OrderByDescending(entry => entry.Score)
                .ThenBy(entry => entry.UserID, StringComparer.Ordinal)
                .ToList();

            var result = new GameLeaderboardResult
            {
                TotalPlayers = sortedAll.Count,
                Entries = sortedAll.Take(limit).ToList()
            };

            if (!string.IsNullOrEmpty(currentUserId))
            {
                var idx = sortedAll.FindIndex(e => e.UserID == currentUserId);
                if (idx >= 0)
                {
                    result.CurrentUserRank = idx + 1; // 1-based
                    result.CurrentUserEntry = sortedAll[idx];
                }
            }

            return result;
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

    public async Task<(bool success, long tokensAwarded, long newBalance, DateTime nextRewardTime)> TryClaimDailyReward(string userId)
    {
        try
        {
            var casinoQuery = _databaseService.CasinoQuery ?? throw new InvalidOperationException("Casino database is not available");
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
            await casinoQuery.UpdateTokensAndDailyReward(userId, newBalance, now, now);
            await RecordTransaction(userId, tokensAwarded, TransactionKind.DailyReward);

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
        var casinoQuery = _databaseService.CasinoQuery ?? throw new InvalidOperationException("Casino database is not available");
        await casinoQuery.ClearAllCasinoUsers();
        await casinoQuery.ClearAllTransactions();

        await _loggingService.LogChannelAndFile($"{ServiceName}: All casino data has been reset.");
    }

    #endregion
}