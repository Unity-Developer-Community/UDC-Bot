
namespace DiscordBot.Domain;

/// <summary>
/// Generic game state enum for all casino games - kept minimal and generic
/// </summary>
public enum GameState
{
    NotStarted,
    InProgress,
    Finished,
    Abandoned,
}

/// <summary>
/// Interface for all casino games. Used for template methods and properties that all games should implement.
/// </summary>
public interface ICasinoGame
{
    string Emoji { get; }
    string Name { get; }
    /// <summary>
    /// Minimum players required for this type of game
    /// </summary>
    int MinPlayers { get; }
    /// <summary>
    /// Maximum players allowed for this type of game
    /// </summary>
    int MaxPlayers { get; }

    public abstract GamePlayer? CurrentPlayer { get; }

    GameState State { get; set; }

    bool IsCompleted { get; }
    List<GamePlayer> Players { get; }

    /// <summary>
    /// Starts the game with the given players.<br />
    /// This method should be called after all players are added and ready.
    /// </summary>
    public void StartGame(IEnumerable<GamePlayer> players);
    /// <summary>
    /// Finalizes the game, 
    /// </summary>
    public IReadOnlyList<(GamePlayer player, long payout)> EndGame();

    public abstract GamePlayerResult GetPlayerGameResult(GamePlayer player);
    public abstract long CalculatePayout(GamePlayer player);

    Type ActionType { get; }

    public void DoPlayerAction(GamePlayer player, Enum action);

    // Dealer stuff
    public bool HasNextDealerAction();
    public Task DoNextDealerAction();

    public bool HasNextAIAction();
    public Task DoNextAIAction();

    public bool ShouldFinish();
}

/// <summary>
/// Interface for player data in casino games. Each game will have its own implementation of this interface.
/// </summary>
public interface ICasinoGamePlayerData { }

/// <summary>
/// Base class for all casino games (card games, slot machines, roulette, etc.)
/// </summary>
public abstract class ACasinoGame<TPlayerData, TPlayerAction> : ICasinoGame
    where TPlayerData : ICasinoGamePlayerData
    where TPlayerAction : Enum
{
    public abstract string Emoji { get; }
    public abstract string Name { get; } // Display name like "Blackjack", "Slots", etc.
    public abstract int MinPlayers { get; } // Minimum players required to start the game
    public abstract int MaxPlayers { get; } // Maximum players allowed in the game

    public abstract GamePlayer? CurrentPlayer { get; }

    public Type ActionType => typeof(TPlayerAction); // Type of action enum used in this game

    public GameState State { get; set; } = GameState.NotStarted;

    public bool IsCompleted => State == GameState.Finished || State == GameState.Abandoned;
    public Dictionary<GamePlayer, TPlayerData> GameData { get; set; } = [];
    public List<GamePlayer> Players => GameData.Keys.ToList();

    #region Start Game

    public void StartGame(IEnumerable<GamePlayer> players)
    {
        if (State != GameState.NotStarted)
            throw new InvalidOperationException("Game has already started or is not in a valid state to start.");

        if (players.Count() < MinPlayers || players.Count() > MaxPlayers)
            throw new ArgumentOutOfRangeException(nameof(players), $"Player count must be between {MinPlayers} and {MaxPlayers} for game {Emoji}.");

        State = GameState.InProgress;
        GameData.Clear();
        foreach (var player in players) GameData[player] = CreatePlayerData(player);

        InitializeGame(); // hook for game-specific logic
    }
    protected abstract TPlayerData CreatePlayerData(GamePlayer player);
    protected abstract void InitializeGame();

    #endregion
    #region End Game

    /// <summary>
    /// Finalizes the game and sets the state to Finished.
    /// </summary>
    /// <returns>
    /// Returns the payout for each player.
    /// </returns>
    public IReadOnlyList<(GamePlayer player, long payout)> EndGame()
    {
        State = GameState.Finished;
        Players.ForEach(p => p.Result = GetPlayerGameResult(p));
        FinalizeGame(Players); // hook for game-specific logic
        return Players.Select(p => (p, CalculatePayout(p))).ToList();
    }
    // Default implementation does nothing, override in specific games if needed
    protected virtual void FinalizeGame(List<GamePlayer> players) { }
    public abstract GamePlayerResult GetPlayerGameResult(GamePlayer player);
    public abstract long CalculatePayout(GamePlayer player);

    /// <summary>
    /// Determines if the game should enter the FINISHED state. <br />
    /// The default implementation checks if the game is IN_PROGRESS and all players have finished playing.
    /// </summary>
    public virtual bool ShouldFinish() => State == GameState.InProgress && CurrentPlayer == null;

    #endregion
    #region Player Actions

    void ICasinoGame.DoPlayerAction(GamePlayer player, Enum action)
    {
        // Cast to your specific action type
        if (action is TPlayerAction typedAction)
            DoPlayerAction(player, typedAction);
        else
            throw new ArgumentException($"Invalid action type. Expected {typeof(TPlayerAction).Name}, got {action.GetType().Name}");
    }


    public abstract void DoPlayerAction(GamePlayer player, TPlayerAction action);

    #endregion
    #region Dealer Actions

    // Only override these in games that actually need a dealer
    protected virtual bool HasDealer => false;
    public virtual bool CanDealerAct() => false;

    // For games that need dealer actions in the AI flow
    protected virtual AIAction? GetNextDealerAction() => null;

    public bool HasNextDealerAction() => HasDealer && CanDealerAct() && GetNextDealerAction() != null;

    public async Task DoNextDealerAction()
    {
        var action = GetNextDealerAction();
        if (action != null) await action.Execute();
    }

    #endregion
    #region AI Actions

    // For games that need dealer actions in the AI flow
    protected virtual AIAction? GetNextAIAction() => null;

    public bool HasNextAIAction() => CurrentPlayer?.IsAI == true && GetNextAIAction() != null;

    public async Task DoNextAIAction()
    {
        var action = GetNextAIAction();
        if (action != null) await action.Execute();
    }

    #endregion
}