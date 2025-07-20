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
    private readonly ConcurrentDictionary<ulong, ActiveGame<BlackjackGame>> _activeGames;
    private readonly Timer _gameCleanupTimer;

    public BlackjackService(CasinoService casinoService, ILoggingService loggingService, BotSettings settings)
    {
        _casinoService = casinoService;
        _loggingService = loggingService;
        _settings = settings;
        _activeGames = new ConcurrentDictionary<ulong, ActiveGame<BlackjackGame>>();

        // Setup cleanup timer for expired games (run every minute)
        _gameCleanupTimer = new Timer(CleanupExpiredGames, null, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));
    }

    #region Game Management

    public bool HasActiveGame(ulong userId)
    {
        return _activeGames.ContainsKey(userId) && !_activeGames[userId].IsCompleted;
    }

    public ActiveGame<BlackjackGame> GetActiveGame(ulong userId)
    {
        if (_activeGames.TryGetValue(userId, out var game) && !game.IsCompleted)
        {
            return game;
        }
        return null;
    }

    public async Task<ActiveGame<BlackjackGame>> StartBlackjackGame(ulong userId, ulong bet, IUserMessage message)
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

            var game = new ActiveGame<BlackjackGame>
            {
                UserId = userId,
                Bet = bet,
                StartTime = DateTime.UtcNow,
                ExpiryTime = DateTime.UtcNow.AddMinutes(_settings.CasinoGameTimeoutMinutes),
                Message = message,
                Game = new BlackjackGame(),
            };

            game.Game.StartGame();

            // Replace any existing game (completed or not) with the new one
            _activeGames.AddOrUpdate(userId, game, (key, oldGame) => game);

            // Note: We don't deduct the bet upfront anymore - only when the player loses

            return game;
        }
        catch (Exception ex)
        {
            await _loggingService.LogChannelAndFile($"{ServiceName}: ERROR in StartBlackjackGame for userId {userId}: {ex.Message}", ExtendedLogSeverity.Error);
            await _loggingService.LogChannelAndFile($"{ServiceName}: StartBlackjackGame Exception Details: {ex}");
            throw;
        }
    }

    public async Task<(BlackjackGameState result, long payout)> CompleteBlackjackGame(ActiveGame<BlackjackGame> game, BlackjackGameState finalState)
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
                return (finalState, CalculateBlackjackPayout(game.Bet, finalState));
            }

            game.Game.State = finalState switch
            {
                BlackjackGameState.PlayerBusted => GameState.Lost,
                BlackjackGameState.DealerBusted => GameState.Won,
                BlackjackGameState.PlayerWins => GameState.Won,
                BlackjackGameState.DealerWins => GameState.Lost,
                BlackjackGameState.Tie => GameState.Tie,
                _ => GameState.InProgress
            };

            long payout = CalculateBlackjackPayout(game.Bet, finalState);

            // Handle token transactions based on game outcome
            if (finalState == BlackjackGameState.PlayerWins || finalState == BlackjackGameState.DealerBusted)
            {
                // Player wins - give them the winnings (bet amount)
                await _casinoService.UpdateUserTokens(game.UserId.ToString(), (long)game.Bet, "blackjack_win",
                    $"Blackjack win: {game.Bet} tokens");
            }
            else if (finalState == BlackjackGameState.Tie)
            {
                // Tie - no money changes hands, payout is 0
                payout = 0;
            }
            else
            {
                // Player loses - deduct the bet
                await _casinoService.UpdateUserTokens(game.UserId.ToString(), -(long)game.Bet, "blackjack_loss",
                    $"Blackjack loss: {game.Bet} tokens");
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
            BlackjackGameState.PlayerWins => (long)bet, // Win the bet amount
            BlackjackGameState.DealerBusted => (long)bet, // Win the bet amount
            BlackjackGameState.Tie => 0, // No payout on tie
            _ => -(long)bet // Loss cases: player busted, dealer wins
        };
    }

    public async Task ExpireGame(ulong userId)
    {
        if (_activeGames.TryRemove(userId, out var game))
        {
            if (!game.IsCompleted)
            {
                // Update the message to show expiry
                if (game.Message != null)
                {
                    var embed = new EmbedBuilder()
                        .WithTitle("🎰 Game Expired")
                        .WithDescription("This game has expired after 5 minutes of inactivity.")
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
