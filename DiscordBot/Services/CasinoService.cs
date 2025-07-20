using System.Collections.Concurrent;
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

        Task.Run(async () =>
        {
            await _loggingService.LogAction($"{ServiceName}: Casino service initialized.", ExtendedLogSeverity.Positive);
        });
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
                UpdatedAt = DateTime.UtcNow
            };

            var createdUser = await _databaseService.CasinoQuery.InsertCasinoUser(newUser);
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

    public async Task<bool> TransferTokens(string fromUserId, string toUserId, ulong amount, string reason = "gift")
    {
        var fromUser = await GetOrCreateCasinoUser(fromUserId);
        var toUser = await GetOrCreateCasinoUser(toUserId);

        if (fromUser.Tokens < amount)
            return false;

        // Update balances
        await _databaseService.CasinoQuery.UpdateTokens(fromUserId, fromUser.Tokens - amount, DateTime.UtcNow);
        await _databaseService.CasinoQuery.UpdateTokens(toUserId, toUser.Tokens + amount, DateTime.UtcNow);

        // Record transactions
        await RecordTransaction(fromUserId, toUserId, -(long)amount, "gift", $"Gift to {toUserId}");
        await RecordTransaction(toUserId, fromUserId, (long)amount, "gift", $"Gift from {fromUserId}");

        return true;
    }

    public async Task<bool> UpdateUserTokens(string userId, long deltaTokens, string transactionType, string description)
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
            await RecordTransaction(userId, null, deltaTokens, transactionType, description);

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
        await RecordTransaction(userId, null, (long)amount, "admin_set", $"Set by admin {adminUserId}");
    }

    public async Task<List<CasinoUser>> GetLeaderboard(int limit = 10)
    {
        var topUsers = await _databaseService.CasinoQuery.GetTopTokenHolders(limit);
        return topUsers.ToList();
    }

    public async Task<List<TokenTransaction>> GetUserTransactionHistory(string userId, int limit = 10)
    {
        var transactions = await _databaseService.CasinoQuery.GetUserTransactionHistory(userId, limit);
        return transactions.ToList();
    }

    private async Task RecordTransaction(string userId, string targetUserId, long amount, string type, string description)
    {
        var transaction = new TokenTransaction
        {
            UserID = userId,
            TargetUserID = targetUserId,
            Amount = amount,
            TransactionType = type,
            Description = description,
            CreatedAt = DateTime.UtcNow
        };

        await _databaseService.CasinoQuery.InsertTransaction(transaction);
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

    #region Admin Functions

    public async Task ResetAllCasinoData()
    {
        await _databaseService.CasinoQuery.ClearAllCasinoUsers();
        await _databaseService.CasinoQuery.ClearAllTransactions();

        await _loggingService.LogChannelAndFile($"{ServiceName}: All casino data has been reset.");
    }

    #endregion
}