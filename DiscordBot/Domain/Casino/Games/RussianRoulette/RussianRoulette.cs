namespace DiscordBot.Domain;

/// <summary>
/// Represents the different actions a player can take in Russian Roulette
/// </summary>
public enum RussianRoulettePlayerAction
{
    [ButtonMetadata(Label = "System 1 (Fixed Risk)", Style = ButtonStyle.Secondary)]
    SelectSystem1,
    [ButtonMetadata(Label = "System 2 (Escalating Risk)", Style = ButtonStyle.Secondary)]
    SelectSystem2,
    [ButtonMetadata(Emoji = "🔫", Label = "Pull Trigger", Style = ButtonStyle.Danger)]
    PullTrigger,
    [ButtonMetadata(Emoji = "💰", Label = "Cash Out", Style = ButtonStyle.Success)]
    CashOut
}

/// <summary>
/// Simulation system types for Russian Roulette
/// </summary>
public enum RussianRouletteSystem
{
    None,
    System1, // Fixed 1/6 chance each turn
    System2  // Escalating bullets (1/6, 2/6, 3/6, etc.)
}

public class RussianRoulettePlayerData : ICasinoGamePlayerData
{
    public RussianRouletteSystem SelectedSystem { get; set; } = RussianRouletteSystem.None;
    public int CurrentTurn { get; set; } = 0;
    public int BulletsSurvived { get; set; } = 0;
    public bool HasMadeAction { get; set; } = false;
    public bool GameEnded { get; set; } = false;
    public bool WonGame { get; set; } = false;
    
    // For System 2: chamber simulation
    public List<bool> Chamber { get; set; } = new();
    
    public bool HasSelectedSystem => SelectedSystem != RussianRouletteSystem.None;
}

public class RussianRoulette : ACasinoGame<RussianRoulettePlayerData, RussianRoulettePlayerAction>
{
    private static readonly Random _random = new();
    
    public override string Emoji => "🔫";
    public override string Name => "Russian Roulette";
    public override int MinPlayers => 1;
    public override int MaxPlayers => 1; // Single player vs house only
    public override bool HasPrivateHands => false;

    public override GamePlayer? CurrentPlayer => Players.FirstOrDefault(p => !GameData[p].GameEnded);

    #region Payout Multipliers

    private static readonly Dictionary<RussianRouletteSystem, double[]> PayoutMultipliers = new()
    {
        {
            RussianRouletteSystem.System1,
            new double[] { 1.0, 1.1, 1.4, 1.9, 2.9, 5.9 }
        },
        {
            RussianRouletteSystem.System2,
            new double[] { 1.0, 1.1, 1.65, 3.4, 10.4, 63.0 }
        }
    };

    #endregion

    #region Game Lifecycle

    protected override RussianRoulettePlayerData CreatePlayerData(GamePlayer player) => new();

    protected override void InitializeGame()
    {
        State = GameState.InProgress;
        
        // Reset all player data
        foreach (var player in Players)
        {
            var data = GameData[player];
            data.SelectedSystem = RussianRouletteSystem.None;
            data.CurrentTurn = 0;
            data.BulletsSurvived = 0;
            data.HasMadeAction = false;
            data.GameEnded = false;
            data.WonGame = false;
            data.Chamber.Clear();
        }
    }

    public override string ShowHand(GamePlayer player)
    {
        var data = GameData[player];
        if (!data.HasSelectedSystem)
        {
            return "Select your preferred game system to begin.";
        }
        
        return $"Turn {data.CurrentTurn + 1}/6 | Bullets Survived: {data.BulletsSurvived} | Current Payout: {GetCurrentPayoutMultiplier(player):F1}x";
    }

    #endregion

    #region Game Logic

    public override void DoPlayerAction(GamePlayer player, RussianRoulettePlayerAction action)
    {
        if (State != GameState.InProgress)
            throw new InvalidOperationException("Game is not in progress");

        var data = GameData[player];
        
        switch (action)
        {
            case RussianRoulettePlayerAction.SelectSystem1:
                if (data.HasSelectedSystem)
                    throw new InvalidOperationException("System already selected");
                data.SelectedSystem = RussianRouletteSystem.System1;
                SetupSystem1(data);
                break;
                
            case RussianRoulettePlayerAction.SelectSystem2:
                if (data.HasSelectedSystem)
                    throw new InvalidOperationException("System already selected");
                data.SelectedSystem = RussianRouletteSystem.System2;
                SetupSystem2(data);
                break;
                
            case RussianRoulettePlayerAction.PullTrigger:
                if (!data.HasSelectedSystem)
                    throw new InvalidOperationException("Must select system first");
                if (data.GameEnded)
                    throw new InvalidOperationException("Game has already ended");
                HandlePullTrigger(player);
                break;
                
            case RussianRoulettePlayerAction.CashOut:
                if (!data.HasSelectedSystem)
                    throw new InvalidOperationException("Must select system first");
                if (data.GameEnded)
                    throw new InvalidOperationException("Game has already ended");
                if (data.CurrentTurn == 0)
                    throw new InvalidOperationException("Cannot cash out before first turn");
                HandleCashOut(player);
                break;
        }
        
        data.HasMadeAction = true;
    }

    private void SetupSystem1(RussianRoulettePlayerData data)
    {
        // System 1: No chamber setup needed, just random chance each turn
        data.CurrentTurn = 0;
    }

    private void SetupSystem2(RussianRoulettePlayerData data)
    {
        // System 2: Setup initial chamber with 1 bullet out of 6 for turn 1
        data.Chamber = new List<bool> { true, false, false, false, false, false };
        ShuffleChamber(data.Chamber);
        data.CurrentTurn = 0;
    }

    private void SetupSystem2NextTurn(RussianRoulettePlayerData data)
    {
        // For System 2: Create chamber for next turn with (currentTurn + 1) bullets
        var bulletsCount = data.CurrentTurn + 1; // Turn 1=1 bullet, Turn 2=2 bullets, etc.
        data.Chamber = new List<bool>();
        
        for (int i = 0; i < bulletsCount; i++)
        {
            data.Chamber.Add(true);
        }
        for (int i = bulletsCount; i < 6; i++)
        {
            data.Chamber.Add(false);
        }
        
        ShuffleChamber(data.Chamber);
    }

    private void HandlePullTrigger(GamePlayer player)
    {
        var data = GameData[player];
        bool hitBullet = false;

        if (data.SelectedSystem == RussianRouletteSystem.System1)
        {
            // System 1: Fixed 1/6 chance each turn
            hitBullet = _random.Next(6) == 0;
        }
        else if (data.SelectedSystem == RussianRouletteSystem.System2)
        {
            // System 2: Check current chamber position (first position)
            hitBullet = data.Chamber[0];
        }

        if (hitBullet)
        {
            // Game over - player loses
            data.GameEnded = true;
            data.WonGame = false;
        }
        else
        {
            // Survived this turn
            data.BulletsSurvived++;
            data.CurrentTurn++;
            
            // Check if all 6 chambers survived (auto cash out)
            if (data.BulletsSurvived >= 6)
            {
                data.GameEnded = true;
                data.WonGame = true;
            }
            else if (data.SelectedSystem == RussianRouletteSystem.System2)
            {
                // For System 2: Setup next turn with more bullets
                SetupSystem2NextTurn(data);
            }
        }
    }

    private void HandleCashOut(GamePlayer player)
    {
        var data = GameData[player];
        data.GameEnded = true;
        data.WonGame = true;
    }

    private void ShuffleChamber(List<bool> chamber)
    {
        for (int i = chamber.Count - 1; i > 0; i--)
        {
            int j = _random.Next(i + 1);
            (chamber[i], chamber[j]) = (chamber[j], chamber[i]);
        }
    }

    #endregion

    #region Results and Payouts

    public override GamePlayerResult GetPlayerGameResult(GamePlayer player)
    {
        var data = GameData[player];
        
        if (!data.GameEnded)
            return GamePlayerResult.NoResult;
            
        return data.WonGame ? GamePlayerResult.Won : GamePlayerResult.Lost;
    }

    public override long CalculatePayout(GamePlayer player, ulong totalPot)
    {
        var result = GetPlayerGameResult(player);
        var data = GameData[player];
        
        switch (result)
        {
            case GamePlayerResult.Won:
                var multiplier = GetCurrentPayoutMultiplier(player);  
                var winnings = (long)(player.Bet * multiplier);
                return winnings - (long)player.Bet; // Net gain
                
            case GamePlayerResult.Lost:
                return -(long)player.Bet; // Lose entire bet
                
            default:
                return 0;
        }
    }

    public double GetCurrentPayoutMultiplier(GamePlayer player)
    {
        var data = GameData[player];
        if (!data.HasSelectedSystem) return 1.0;
        
        var multipliers = PayoutMultipliers[data.SelectedSystem];
        int index = Math.Min(data.BulletsSurvived, multipliers.Length - 1);
        return multipliers[index];
    }

    public double GetNextPayoutMultiplier(GamePlayer player)
    {
        var data = GameData[player];
        if (!data.HasSelectedSystem) return 1.0;
        
        var multipliers = PayoutMultipliers[data.SelectedSystem];
        int nextIndex = Math.Min(data.BulletsSurvived + 1, multipliers.Length - 1);
        return multipliers[nextIndex];
    }

    #endregion

    #region Action Validation

    public bool CanPlayerSelectSystem(GamePlayer player)
    {
        if (State != GameState.InProgress) return false;
        var data = GameData[player];
        return !data.HasSelectedSystem && !data.GameEnded;
    }

    public bool CanPlayerPullTrigger(GamePlayer player)
    {
        if (State != GameState.InProgress) return false;
        var data = GameData[player];
        return data.HasSelectedSystem && !data.GameEnded;
    }

    public bool CanPlayerCashOut(GamePlayer player)
    {
        if (State != GameState.InProgress) return false;
        var data = GameData[player];
        return data.HasSelectedSystem && !data.GameEnded && data.BulletsSurvived > 0;
    }

    #endregion

    #region AI Actions (Not used in single player game)

    protected override AIAction? GetNextAIAction()
    {
        // Russian Roulette is single player vs house, no AI actions needed
        return null;
    }

    public override bool ShouldFinish()
    {
        // Game should finish when player has ended the game (win or lose)
        if (Players.Count == 0) return false;
        
        var player = Players[0];
        var data = GameData[player];
        
        return data.GameEnded;
    }

    #endregion
}