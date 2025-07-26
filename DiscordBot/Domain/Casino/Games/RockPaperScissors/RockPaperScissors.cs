namespace DiscordBot.Domain;

/// <summary>
/// Represents the different choices a player can make in a Rock Paper Scissors game
/// </summary>
public enum RockPaperScissorsPlayerAction
{
    Rock,
    Paper,
    Scissors
}

public class RockPaperScissorsPlayerData : ICasinoGamePlayerData
{
    public RockPaperScissorsPlayerAction? Choice { get; set; } = null;
    public bool HasMadeChoice => Choice.HasValue;
}

public class RockPaperScissors : ACasinoGame<RockPaperScissorsPlayerData, RockPaperScissorsPlayerAction>
{
    public override string Emoji => "✂️";
    public override string Name => "Rock Paper Scissors";
    public override int MinPlayers => 2; // Exactly 2 players required
    public override int MaxPlayers => 2; // Maximum 2 players allowed

    /// <summary>
    /// The current player is any player who hasn't made their choice yet
    /// </summary>
    public override GamePlayer? CurrentPlayer => Players.FirstOrDefault(p => !GameData[p].HasMadeChoice);

    #region Start Game

    protected override RockPaperScissorsPlayerData CreatePlayerData(GamePlayer player) => new();

    protected override void InitializeGame()
    {
        State = GameState.InProgress;

        // Clear any previous choices
        foreach (var player in Players)
        {
            GameData[player].Choice = null;
        }
    }

    #endregion

    #region End Game

    public override GamePlayerResult GetPlayerGameResult(GamePlayer player)
    {
        if (Players.Count != 2) return GamePlayerResult.NoResult;

        var opponent = Players.FirstOrDefault(p => p != player);
        if (opponent == null) return GamePlayerResult.NoResult;

        var choicePlayer = GameData[player].Choice;
        var choiceOpponent = GameData[opponent].Choice;

        if (!choicePlayer.HasValue || !choiceOpponent.HasValue) return GamePlayerResult.NoResult;

        // If it's a tie
        if (choicePlayer == choiceOpponent)
            return GamePlayerResult.Tie;

        // Determine winner based on Rock Paper Scissors rules
        bool playerWins = (choicePlayer == RockPaperScissorsPlayerAction.Rock && choiceOpponent == RockPaperScissorsPlayerAction.Scissors) ||
                         (choicePlayer == RockPaperScissorsPlayerAction.Paper && choiceOpponent == RockPaperScissorsPlayerAction.Rock) ||
                         (choicePlayer == RockPaperScissorsPlayerAction.Scissors && choiceOpponent == RockPaperScissorsPlayerAction.Paper);

        return playerWins ? GamePlayerResult.Won : GamePlayerResult.Lost;
    }

    public override long CalculatePayout(GamePlayer player)
    {
        return player.Result switch
        {
            GamePlayerResult.Won => (long)player.Bet, // Winner gets their bet back plus opponent's bet
            GamePlayerResult.Lost => -(long)player.Bet, // Loser loses their bet
            GamePlayerResult.Tie => 0, // Tie gets bet back (no loss, no gain)
            _ => 0
        };
    }

    #endregion

    #region Player Actions

    /// <summary>
    /// Checks if the player can make a choice (Rock, Paper, or Scissors)
    /// </summary>
    public bool CanPlayerAct(GamePlayer player)
    {
        if (State != GameState.InProgress) return false;
        if (GameData[player].HasMadeChoice) return false;
        return true;
    }

    public override void DoPlayerAction(GamePlayer player, RockPaperScissorsPlayerAction action)
    {
        if (State != GameState.InProgress)
            throw new InvalidOperationException("Game is not in progress");
        if (!CanPlayerAct(player))
            throw new InvalidOperationException("Player cannot take action at this time");

        // Record the player's choice
        GameData[player].Choice = action;
    }

    protected override AIAction? GetNextAIAction()
    {
        // AI can randomly choose Rock, Paper, or Scissors
        var choices = Enum.GetValues(typeof(RockPaperScissorsPlayerAction)).Cast<RockPaperScissorsPlayerAction>().ToList();
        var randomChoice = choices[new Random().Next(choices.Count)];

        return new AIAction
        {
            Execute = () => { DoPlayerAction(CurrentPlayer!, randomChoice); return Task.CompletedTask; },
        };
    }

    #endregion
    #region Helper Methods

    /// <summary>
    /// Gets the emoji representation of a choice
    /// </summary>
    public static string GetChoiceEmoji(RockPaperScissorsPlayerAction choice)
    {
        return choice switch
        {
            RockPaperScissorsPlayerAction.Rock => "🪨",
            RockPaperScissorsPlayerAction.Paper => "📄",
            RockPaperScissorsPlayerAction.Scissors => "✂️",
            _ => "❓"
        };
    }

    /// <summary>
    /// Gets a human-readable description of who beats what
    /// </summary>
    public static string GetBeatDescription(RockPaperScissorsPlayerAction winner, RockPaperScissorsPlayerAction loser)
    {
        return (winner, loser) switch
        {
            (RockPaperScissorsPlayerAction.Rock, RockPaperScissorsPlayerAction.Scissors) => "Rock crushes Scissors",
            (RockPaperScissorsPlayerAction.Paper, RockPaperScissorsPlayerAction.Rock) => "Paper covers Rock",
            (RockPaperScissorsPlayerAction.Scissors, RockPaperScissorsPlayerAction.Paper) => "Scissors cuts Paper",
            _ => "Unknown"
        };
    }

    #endregion
}