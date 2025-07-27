using DiscordBot.Domain;

public interface IGameSession
{
    public Guid Id { get; init; }
    public List<DiscordGamePlayer> Players { get; set; }
    public GameState State { get; }
    public DiscordGamePlayer? CurrentPlayer { get; }
    public string GameName { get; }
    public Type ActionType { get; }

    public DiscordGamePlayer? GetPlayer(ulong userId);
    public bool AddPlayer(ulong userId, ulong bet);
    public bool AddPlayerAI();
    public void RemovePlayer(ulong userId);
    public void RemovePlayerAI();
    public void SetPlayerReady(ulong userId, bool isReady);
    public void SetPlayerBet(ulong userId, ulong bet);
    public void DoPlayerAction(ulong userId, Enum action);

    public bool HasNextDealerAction();
    public Task DoNextDealerAction();

    public bool HasNextAIAction();
    public Task DoNextAIAction();

    public bool ShouldFinish();
    public IReadOnlyList<(DiscordGamePlayer player, long payout)> EndGame();

    public void Reset();
}

/// <summary>
/// Represents an active game session for any casino game
/// </summary>
public class GameSession<TGame> : IGameSession
    where TGame : ICasinoGame
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public List<DiscordGamePlayer> Players { get; set; } = [];
    public DateTime StartTime { get; init; } = DateTime.UtcNow;
    // public TimeSpan ExpiryTime { get; set; } = TimeSpan.FromMinutes(5);
    // public ulong UserId { get; set; } // The user who started the game
    public int MaxSeats { get; init; } // Max player cannot exceed the game's MaxPlayers and should be at least the game's MinPlayers

    // Game instance - strongly typed
    protected TGame Game { get; init; }
    public GameState State => Game.State;

    // Constructor
    public GameSession(TGame game, int maxSeats)
    {
        Game = game;

        if (maxSeats < game.MinPlayers || maxSeats > game.MaxPlayers)
            throw new ArgumentOutOfRangeException(nameof(maxSeats), $"Max players for {game.Name} must be between {game.MinPlayers} and {game.MaxPlayers}");

        MaxSeats = maxSeats;
    }

    // Convenience properties
    public string GameName => Game.Name;
    public Type ActionType => Game.ActionType;
    public bool IsCompleted => Game.IsCompleted;
    public int PlayerCount => Players.Count;
    public bool CanJoin => Game.State == GameState.NotStarted && PlayerCount < MaxSeats;
    public bool AllPlayersReady => Players.Count >= Game.MinPlayers && Players.All(p => p.IsReady);
    public DiscordGamePlayer? CurrentPlayer => (DiscordGamePlayer?)Game.CurrentPlayer;
    /// <summary>
    /// <list> Checks if the game can start:
    /// <item>• Game state is NotStarted</item>
    /// <item>• Player count is at least the minimum required</item>
    /// <item>• All players are ready</item>
    /// </list>
    /// </summary>
    public bool CanStart => Game.State == GameState.NotStarted && PlayerCount >= Game.MinPlayers && AllPlayersReady;

    public ulong GetTotalPot => (ulong)Players.Sum(p => (long)p.Bet);

    public bool ShouldFinish() => Game.ShouldFinish();

    public IReadOnlyList<(DiscordGamePlayer player, long payout)> EndGame()
        => Game.EndGame()
            .Select(p => ((DiscordGamePlayer)p.player, p.payout))
            .ToList();

    public void Reset()
    {
        Game.Reset();
        Players.Clear();
    }

    #region Player Management

    public DiscordGamePlayer? GetPlayer(ulong userId) => Players.FirstOrDefault(p => p.UserId == userId);

    public bool AddPlayer(ulong userId, ulong bet)
    {
        if (!CanJoin) return false;
        if (Players.Any(p => p.UserId == userId)) return false; // Player already in game

        var player = new DiscordGamePlayer { UserId = userId, Bet = bet };
        Players.Add(player);

        // Check if we can auto-start
        if (CanStart) Game.StartGame(Players);

        return true;
    }

    public bool AddPlayerAI()
    {
        if (!CanJoin) return false;
        var biggestAIID = Players.Where(p => p.IsAI).Select(p => p.UserId).DefaultIfEmpty<ulong>(0).Max();
        var player = new DiscordGamePlayer
        {
            IsAI = true,
            UserId = biggestAIID + 1,
            Bet = 0, // AI players don't have a bet
            IsReady = true, // AI players are always ready
        };
        Players.Add(player);
        // Check if we can auto-start
        if (CanStart) Game.StartGame(Players);
        return true;
    }

    public void RemovePlayer(ulong userId)
    {
        if (Game.State != GameState.NotStarted) return; // Cannot remove players after the game has started
        var player = Players.FirstOrDefault(p => p.UserId == userId);
        var playerRemoved = false;
        if (player != null) playerRemoved = Players.Remove(player);

        if (playerRemoved && CanStart) Game.StartGame(Players);
        else if (Players.Count == 0) Game.State = GameState.Abandoned;
    }

    public void RemovePlayerAI()
    {
        if (Game.State != GameState.NotStarted) return; // Cannot remove players after the game has started
        var player = Players.LastOrDefault(p => p.IsAI); // Find the last AI player
        if (player != null) Players.Remove(player);
    }

    public void SetPlayerReady(ulong userId, bool ready = true)
    {
        if (Game.State != GameState.NotStarted) return; // Cannot change readiness after the game has started
        var player = GetPlayer(userId);
        if (player == null) return;
        if (player.IsReady == ready) return;

        player.IsReady = ready;

        // Check if all players are now ready
        if (ready && CanStart) Game.StartGame(Players);
    }

    public void SetPlayerBet(ulong userId, ulong bet)
    {
        if (Game.State != GameState.NotStarted) return; // Cannot change bet after the game has started
        var player = GetPlayer(userId);
        if (player != null) player.Bet = bet;
    }

    #endregion
    #region Player Actions

    public void DoPlayerAction(ulong userId, Enum action)
    {
        var player = GetPlayer(userId);
        if (player == null) return;
        Game.DoPlayerAction(player, action);
    }


    #endregion
    #region Dealer Actions

    public bool HasNextDealerAction() => Game.HasNextDealerAction();

    public async Task DoNextDealerAction() => await Game.DoNextDealerAction();

    #endregion
    #region AI Actions

    public bool HasNextAIAction() => Game.HasNextAIAction();

    public async Task DoNextAIAction() => await Game.DoNextAIAction();

    #endregion
}