using DiscordBot.Domain;
using DiscordBot.Modules;
using Discord.WebSocket;

namespace DiscordBot.Services;

/// <summary>
/// Service responsible for handling player turn timeouts in games
/// </summary>
public class GameTimeoutService
{
    private readonly GameService _gameService;
    private readonly ILoggingService _loggingService;
    private readonly DiscordSocketClient _client;
    private readonly Timer _timer;
    private readonly TimeSpan _checkInterval = TimeSpan.FromSeconds(10); // Check every 10 seconds

    public GameTimeoutService(GameService gameService, ILoggingService loggingService, DiscordSocketClient client)
    {
        _gameService = gameService;
        _loggingService = loggingService;
        _client = client;
        
        // Start the timer to check for timeouts periodically
        _timer = new Timer(CheckForTimeouts, null, _checkInterval, _checkInterval);
    }

    private async void CheckForTimeouts(object? state)
    {
        try
        {
            // Get all active game sessions and check for timeouts
            var activeSessions = _gameService.GetActiveSessions();
            
            foreach (var session in activeSessions.Where(s => s.State == GameState.InProgress))
            {
                if (session.IsCurrentPlayerTimedOut)
                {
                    await HandleTimeout(session);
                }
            }
        }
        catch (Exception ex)
        {
            await _loggingService.LogAction($"Error checking for timeouts: {ex.Message}", ExtendedLogSeverity.Warning);
        }
    }

    private async Task HandleTimeout(IDiscordGameSession session)
    {
        try
        {
            var currentPlayer = session.CurrentPlayer;
            if (currentPlayer == null || currentPlayer.IsAI) return;

            await _loggingService.LogAction(
                $"Player {currentPlayer.UserId} timed out in game {session.GameName} ({session.Id}). Performing default action.", 
                ExtendedLogSeverity.Info);

            // Perform the timeout action
            await session.HandlePlayerTimeout();
            
            // Update turn start time for the next player  
            session.UpdateCurrentPlayerTurnStartTime();

            await _loggingService.LogAction(
                $"Default action performed for player {currentPlayer.UserId} in game {session.Id}",
                ExtendedLogSeverity.Info);
        }
        catch (Exception ex)
        {
            await _loggingService.LogAction(
                $"Error handling timeout for game {session.Id}: {ex.Message}", 
                ExtendedLogSeverity.Error);
        }
    }

    public void Dispose()
    {
        _timer?.Dispose();
    }
}