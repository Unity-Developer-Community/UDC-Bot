using System.Collections.Concurrent;
using Discord;
using Discord.WebSocket;
using DiscordBot.Domain;
using DiscordBot.Extensions;
using DiscordBot.Settings;

namespace DiscordBot.Services;

public class CasinoService
{
    private const string ServiceName = "CasinoService";
    
    private readonly DatabaseService _databaseService;
    private readonly ILoggingService _loggingService;
    private readonly BotSettings _settings;
    private readonly ConcurrentDictionary<ulong, ActiveGame> _activeGames;
    private readonly Timer _gameCleanupTimer;

    public CasinoService(DatabaseService databaseService, ILoggingService loggingService, BotSettings settings)
    {
        _databaseService = databaseService;
        _loggingService = loggingService;
        _settings = settings;
        _activeGames = new ConcurrentDictionary<ulong, ActiveGame>();
        
        // Setup cleanup timer for expired games (run every minute)
        _gameCleanupTimer = new Timer(CleanupExpiredGames, null, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));
        
        Task.Run(async () =>
        {
            await _loggingService.LogAction($"{ServiceName}: Casino service initialized.", ExtendedLogSeverity.Positive);
        });
    }

    #region Token Management

    public async Task<CasinoUser> GetOrCreateCasinoUser(string userId)
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

        return await _databaseService.CasinoQuery.InsertCasinoUser(newUser);
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
        var user = await GetOrCreateCasinoUser(userId);
        var newBalance = (long)user.Tokens + deltaTokens;
        
        if (newBalance < 0)
            return false;

        await _databaseService.CasinoQuery.UpdateTokens(userId, (ulong)newBalance, DateTime.UtcNow);
        await RecordTransaction(userId, null, deltaTokens, transactionType, description);
        
        return true;
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

    #region Game Management

    public bool HasActiveGame(ulong userId)
    {
        return _activeGames.ContainsKey(userId) && !_activeGames[userId].IsCompleted;
    }

    public ActiveGame GetActiveGame(ulong userId)
    {
        _activeGames.TryGetValue(userId, out var game);
        return game;
    }

    public async Task<ActiveGame> StartBlackjackGame(ulong userId, ulong bet, IUserMessage message)
    {
        var user = await GetOrCreateCasinoUser(userId.ToString());
        
        if (user.Tokens < bet)
            throw new InvalidOperationException("Insufficient tokens for bet");

        if (HasActiveGame(userId))
            throw new InvalidOperationException("User already has an active game");

        var game = new ActiveGame
        {
            UserId = userId,
            GameType = "blackjack",
            Bet = bet,
            StartTime = DateTime.UtcNow,
            ExpiryTime = DateTime.UtcNow.AddMinutes(_settings.CasinoGameTimeoutMinutes),
            Message = message,
            BlackjackGame = new BlackjackGame(),
            IsCompleted = false
        };

        // Deal initial cards
        game.BlackjackGame.PlayerCards.Add(game.BlackjackGame.Deck.DrawCard());
        game.BlackjackGame.DealerCards.Add(game.BlackjackGame.Deck.DrawCard());
        game.BlackjackGame.PlayerCards.Add(game.BlackjackGame.Deck.DrawCard());

        _activeGames.TryAdd(userId, game);
        
        // Deduct bet from user's tokens
        await UpdateUserTokens(userId.ToString(), -(long)bet, "blackjack_bet", $"Blackjack bet: {bet} tokens");

        return game;
    }

    public async Task<(BlackjackGameState result, long payout)> CompleteBlackjackGame(ulong userId, BlackjackGameState finalState)
    {
        if (!_activeGames.TryGetValue(userId, out var activeGame))
            throw new InvalidOperationException("No active game found for user");

        activeGame.IsCompleted = true;
        activeGame.BlackjackGame.State = finalState;

        long payout = CalculateBlackjackPayout(activeGame.Bet, finalState);
        
        if (payout > 0)
        {
            await UpdateUserTokens(userId.ToString(), payout, "blackjack_win", 
                $"Blackjack win: {payout} tokens (bet: {activeGame.Bet})");
        }

        // Clean up the game after a delay to allow message updates
        _ = Task.Run(async () =>
        {
            await Task.Delay(TimeSpan.FromMinutes(2));
            _activeGames.TryRemove(userId, out _);
        });

        return (finalState, payout);
    }

    private long CalculateBlackjackPayout(ulong bet, BlackjackGameState result)
    {
        return result switch
        {
            BlackjackGameState.PlayerWins => (long)(bet * 2), // Return bet + winnings
            BlackjackGameState.Tie => (long)bet, // Return just the bet
            BlackjackGameState.DealerBusted => (long)(bet * 2), // Return bet + winnings
            _ => 0 // Loss cases: player busted, dealer wins
        };
    }

    public async Task ExpireGame(ulong userId)
    {
        if (_activeGames.TryRemove(userId, out var game))
        {
            if (!game.IsCompleted)
            {
                // Return the bet to the user since game expired
                await UpdateUserTokens(userId.ToString(), (long)game.Bet, "blackjack_expired", 
                    "Game expired - bet returned");

                // Update the message to show expiry
                if (game.Message != null)
                {
                    var embed = new EmbedBuilder()
                        .WithTitle("🎰 Game Expired")
                        .WithDescription("This game has expired after 5 minutes of inactivity. Your bet has been returned.")
                        .WithColor(Color.Orange)
                        .Build();

                    try
                    {
                        await game.Message.ModifyAsync(msg =>
                        {
                            msg.Embed = embed;
                            msg.Components = new ComponentBuilder().Build();
                        });
                    }
                    catch
                    {
                        // Message might have been deleted, ignore
                    }
                }
            }
        }
    }

    private async void CleanupExpiredGames(object state)
    {
        var expiredGames = _activeGames.Values.Where(g => DateTime.UtcNow > g.ExpiryTime && !g.IsCompleted).ToList();
        
        foreach (var game in expiredGames)
        {
            await ExpireGame(game.UserId);
        }
    }

    #endregion

    #region Admin Functions

    public async Task ResetAllCasinoData()
    {
        await _databaseService.CasinoQuery.ClearAllCasinoUsers();
        await _databaseService.CasinoQuery.ClearAllTransactions();
        
        // Clear all active games
        _activeGames.Clear();
        
        await _loggingService.LogChannelAndFile($"{ServiceName}: All casino data has been reset.");
    }

    #endregion
}