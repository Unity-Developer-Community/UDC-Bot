namespace DiscordBot.Domain;

public enum GamePlayerResult
{
    NoResult,
    Won,
    Lost,
    Tie,
}

/// <summary>
/// Represents a player in a casino game
/// </summary>
public class GamePlayer
{
    /// <summary>
    /// The bet amount placed by the player
    /// </summary>
    public required ulong Bet { get; set; }
    /// <summary>
    /// The final result of the player in the game (won, lost, tie)
    /// </summary>
    public GamePlayerResult Result { get; set; } = GamePlayerResult.NoResult;
    /// <summary>
    /// Indicates if the player is an AI
    /// </summary>
    public bool IsAI { get; set; } = false;
}

public class DiscordGamePlayer : GamePlayer
{
    /// <summary>
    /// The Discord user ID of the player
    /// </summary>
    public required ulong UserId { get; init; }
    /// <summary>
    /// Indicates if the player is ready to start the game
    /// </summary>
    public bool IsReady { get; set; } = false;
    /// <summary>
    /// Date when the player joined the game
    /// </summary>
    public DateTime JoinedAt { get; init; } = DateTime.UtcNow;
}