using System.Collections.Concurrent;
using Discord;
using DiscordBot.Domain;
using DiscordBot.Settings;

namespace DiscordBot.Services;

public class BlackjackService
{
    private const string ServiceName = "BlackjackService";

    private readonly CasinoService _casinoService;
    private readonly ILoggingService _loggingService;
    private readonly BotSettings _settings;
    private readonly ConcurrentDictionary<ulong, ActiveGame> _activeGames;
    private readonly Timer _gameCleanupTimer;

    public BlackjackService(CasinoService casinoService, ILoggingService loggingService, BotSettings settings)
    {
        _casinoService = casinoService;
        _loggingService = loggingService;
        _settings = settings;
        _activeGames = new ConcurrentDictionary<ulong, ActiveGame>();

        // Setup cleanup timer for expired games (run every minute)
        _gameCleanupTimer = new Timer(CleanupExpiredGames, null, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));

        Task.Run(async () =>
        {
            await _loggingService.LogAction($"{ServiceName}: Blackjack service initialized.", ExtendedLogSeverity.Positive);
        });
    }

    #region Game Management

    public bool HasActiveGame(ulong userId)
    {
        return _activeGames.ContainsKey(userId) && !_activeGames[userId].IsCompleted;
    }

    public ActiveGame GetActiveGame(ulong userId)
    {
        if (_activeGames.TryGetValue(userId, out var game) && !game.IsCompleted)
        {
            return game;
        }
        return null;
    }

    public async Task<ActiveGame> StartBlackjackGame(ulong userId, ulong bet, IUserMessage message)
    {
        try
        {
            var user = await _casinoService.GetOrCreateCasinoUser(userId.ToString());

            if (user.Tokens < bet)
            {
                await _loggingService.LogChannelAndFile($"{ServiceName}: StartBlackjackGame failed - insufficient tokens for userId {userId}. Available: {user.Tokens}, Bet: {bet}");
                throw new InvalidOperationException("Insufficient tokens for bet");
            }

            if (HasActiveGame(userId))
            {
                await _loggingService.LogChannelAndFile($"{ServiceName}: StartBlackjackGame failed - user {userId} already has an active game");
                throw new InvalidOperationException("User already has an active game");
            }

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

            // Deal initial cards (standard blackjack: player gets 2, dealer gets 2)
            game.BlackjackGame.PlayerCards.Add(game.BlackjackGame.Deck.DrawCard());
            game.BlackjackGame.DealerCards.Add(game.BlackjackGame.Deck.DrawCard()); // Dealer's up card (visible)
            game.BlackjackGame.PlayerCards.Add(game.BlackjackGame.Deck.DrawCard());
            game.BlackjackGame.DealerCards.Add(game.BlackjackGame.Deck.DrawCard()); // Dealer's hole card (hidden)

            // Replace any existing game (completed or not) with the new one
            _activeGames.AddOrUpdate(userId, game, (key, oldGame) => game);

            // Deduct bet from user's tokens
            await _casinoService.UpdateUserTokens(userId.ToString(), -(long)bet, "blackjack_bet", $"Blackjack bet: {bet} tokens");

            return game;
        }
        catch (Exception ex)
        {
            await _loggingService.LogChannelAndFile($"{ServiceName}: ERROR in StartBlackjackGame for userId {userId}: {ex.Message}", ExtendedLogSeverity.Error);
            await _loggingService.LogChannelAndFile($"{ServiceName}: StartBlackjackGame Exception Details: {ex}");
            throw;
        }
    }

    public async Task<(BlackjackGameState result, long payout)> CompleteBlackjackGame(ActiveGame game, BlackjackGameState finalState)
    {
        try
        {
            if (game == null)
            {
                await _loggingService.LogChannelAndFile($"{ServiceName}: CompleteBlackjackGame failed - game parameter is null");
                throw new InvalidOperationException("Game parameter cannot be null");
            }

            if (game.IsCompleted)
            {
                // Return the existing result rather than throwing an error
                return (game.BlackjackGame.State, CalculateBlackjackPayout(game.Bet, game.BlackjackGame.State));
            }

            game.IsCompleted = true;
            game.BlackjackGame.State = finalState;

            long payout = CalculateBlackjackPayout(game.Bet, finalState);

            if (payout > 0)
            {
                await _casinoService.UpdateUserTokens(game.UserId.ToString(), payout, "blackjack_win",
                    $"Blackjack win: {payout} tokens (bet: {game.Bet})");
            }

            // Clean up the game after a delay to allow message updates
            _ = Task.Run(async () =>
            {
                await Task.Delay(TimeSpan.FromMinutes(1));

                // Remove the completed game
                if (_activeGames.TryGetValue(game.UserId, out var gameToRemove) && gameToRemove.IsCompleted)
                {
                    _activeGames.TryRemove(game.UserId, out _);
                }
            });

            return (finalState, payout);
        }
        catch (Exception ex)
        {
            await _loggingService.LogChannelAndFile($"{ServiceName}: ERROR in CompleteBlackjackGame for userId {game?.UserId}: {ex.Message}", ExtendedLogSeverity.Error);
            await _loggingService.LogChannelAndFile($"{ServiceName}: CompleteBlackjackGame Exception Details: {ex}");
            throw;
        }
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
                await _casinoService.UpdateUserTokens(userId.ToString(), (long)game.Bet, "blackjack_expired",
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

    public void ClearAllGames()
    {
        _activeGames.Clear();
    }

    #endregion
}
