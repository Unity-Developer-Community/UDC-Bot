namespace DiscordBot.Domain;

/// <summary>
/// Represents an active game session for any card game
/// </summary>
public class ActiveGame<TGame> where TGame : CasinoGame
{
    public ulong UserId { get; set; }
    public ulong Bet { get; set; }
    public DateTime StartTime { get; set; }
    public DateTime ExpiryTime { get; set; }
    public IUserMessage Message { get; set; }

    // Game instance - strongly typed
    public TGame Game { get; set; }

    // Convenience property
    public string GameName => Game?.GameName ?? "Unknown";
    public bool IsCompleted => Game?.IsCompleted ?? false;
}

/// <summary>
/// Base class for all casino games (card games, slot machines, roulette, etc.)
/// </summary>
public abstract class CasinoGame
{
    public abstract string GameName { get; } // Display name like "Blackjack", "Slots", etc.
    public GameState State { get; set; } = GameState.NotStarted;

    // Shared implementation for IsCompleted
    public bool IsCompleted => State == GameState.Won || State == GameState.Lost || State == GameState.Tie || State == GameState.Abandoned;

    public abstract void StartGame();
}

/// <summary>
/// Generic game state enum for all casino games - kept minimal and generic
/// </summary>
public enum GameState
{
    NotStarted,
    InProgress,
    Won,        // Player won
    Lost,       // Player lost  
    Tie,        // Draw/Push
    Abandoned,  // Game was abandoned/cancelled
    Error       // Something went wrong
}
