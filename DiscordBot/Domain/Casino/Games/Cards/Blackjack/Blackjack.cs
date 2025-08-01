namespace DiscordBot.Domain;

/// <summary>
/// Represents the different actions a player can take in a blackjack game
/// </summary>
public enum BlackjackPlayerAction
{
    [ButtonMetadata(Emoji = "🃏", Style = ButtonStyle.Primary)]
    Hit,
    [ButtonMetadata(Emoji = "✋", Style = ButtonStyle.Secondary)]
    Stand,
    [ButtonMetadata(Emoji = "💰", Style = ButtonStyle.Success)]
    DoubleDown
}

public class BlackjackPlayerData : ICasinoGamePlayerData
{
    public List<Card> PlayerCards { get; set; } = [];
    public List<BlackjackPlayerAction> Actions { get; set; } = [];
}

public class Blackjack : ACasinoGame<BlackjackPlayerData, BlackjackPlayerAction>
{
    public override string Emoji => "🃏";
    public override string Name => "Blackjack";
    public override int MinPlayers => 1; // Minimum 1 player that plays against the dealer
    public override int MaxPlayers => 7;

    public Deck Deck { get; set; } = new Deck();

    public List<Card> DealerCards { get; set; } = [];
    public List<BlackjackPlayerAction> DealerActions { get; } = [];

    /// <summary>
    /// The current player is the next player in the list that:
    /// <list type="bullet">
    /// <item>Has the least action</item>
    /// <item>Whose last action is not Stand or DoubleDown</item>
    /// <item>Is not busted</item>
    /// </list>
    /// </summary>
    public override GamePlayer? CurrentPlayer
    {
        get
        {
            return Players
                .Where(p => GameData[p].Actions.LastOrDefault() != BlackjackPlayerAction.Stand &&
                            GameData[p].Actions.LastOrDefault() != BlackjackPlayerAction.DoubleDown &&
                            !IsPlayerBusted(p) &&
                            !IsPlayerBlackjack(p))
                .OrderBy(p => GameData[p].Actions.Count)
                .FirstOrDefault();
        }
    }
    public int CurrentTurn => Players.Max(p => GameData[p].Actions.Count);

    public int GetPlayerValue(GamePlayer player) => BlackjackHelper.CalculateHandValue(GameData[player].PlayerCards);
    public int GetDealerValue() => BlackjackHelper.CalculateHandValue(DealerCards);
    public bool IsPlayerBusted(GamePlayer player) => BlackjackHelper.IsBusted(GameData[player].PlayerCards);
    public bool IsDealerBusted() => BlackjackHelper.IsBusted(DealerCards);
    public bool IsPlayerBlackjack(GamePlayer player) => BlackjackHelper.IsBlackjack(GameData[player].PlayerCards);
    public bool IsDealerBlackjack() => BlackjackHelper.IsBlackjack(DealerCards);
    public bool IsDealerSoft17() => BlackjackHelper.IsSoft17(DealerCards);

    #region Start Game

    protected override BlackjackPlayerData CreatePlayerData(GamePlayer player) => new();

    protected override void InitializeGame()
    {
        State = GameState.InProgress;

        // Create a new deck for the game. The deck contains one standard 52-card deck per player
        Deck = new Deck(times: Players.Count * 1);
        Deck.Shuffle();
        foreach (var player in Players)
        {
            GameData[player].PlayerCards.Clear();
            GameData[player].Actions.Clear();
        }
        DealerCards.Clear();
        DealerActions.Clear();

        Card? card = null;
        // Deal initial cards (2 cards each)
        for (int i = 0; i < 2; i++)
        {
            foreach (var player in Players)
                if ((card = Deck.DrawCard()) != null) GameData[player].PlayerCards.Add(card);
            if ((card = Deck.DrawCard()) != null) DealerCards.Add(card);
        }
    }

    #endregion
    #region End Game

    public override GamePlayerResult GetPlayerGameResult(GamePlayer player)
    {
        if (IsPlayerBlackjack(player)) return GamePlayerResult.Won;
        else if (IsPlayerBusted(player)) return GamePlayerResult.Lost;
        else if (IsDealerBusted()) return GamePlayerResult.Won;
        else if (GetPlayerValue(player) > GetDealerValue()) return GamePlayerResult.Won;
        else if (GetPlayerValue(player) < GetDealerValue()) return GamePlayerResult.Lost;
        else if (GetPlayerValue(player) == GetDealerValue()) return GamePlayerResult.Tie;
        return GamePlayerResult.NoResult;
    }

    public override long CalculatePayout(GamePlayer player, ulong _totalPot)
    {
        return player.Result switch
        {
            GamePlayerResult.Won => (long)player.Bet,
            GamePlayerResult.Lost => -(long)player.Bet,
            GamePlayerResult.Tie => 0,
            _ => 0
        };
    }

    public override bool ShouldFinish() => base.ShouldFinish() && !CanDealerAct();

    #endregion
    #region Player Actions

    /// <summary>
    /// Checks if the player can take action (hit, stand, double down).
    /// </summary>
    /// <list>
    /// The player can act if:
    /// <item>• It is their turn</item>
    /// <item>• They are not busted</item>
    /// <item>• They do not have blackjack</item>
    /// </list>
    public bool CanPlayerAct(GamePlayer player)
    {
        if (CurrentPlayer != player) return false;
        if (IsPlayerBusted(player)) return false;
        if (IsPlayerBlackjack(player)) return false;
        return true;
    }

    private void Hit(GamePlayer player)
    {
        if (!CanPlayerAct(player)) return;

        var card = Deck.DrawCard();
        if (card != null)
        {
            GameData[player].PlayerCards.Add(card);
            GameData[player].Actions.Add(BlackjackPlayerAction.Hit);
        }
    }

    private void Stand(GamePlayer player)
    {
        if (!CanPlayerAct(player)) return;
        GameData[player].Actions.Add(BlackjackPlayerAction.Stand);
    }

    private void DoubleDown(GamePlayer player)
    {
        if (!CanPlayerAct(player)) return;

        var card = Deck.DrawCard();
        if (card != null)
        {
            GameData[player].PlayerCards.Add(card);
            GameData[player].Actions.Add(BlackjackPlayerAction.DoubleDown);
            player.Bet *= 2; // Double the bet
        }
    }

    public override void DoPlayerAction(GamePlayer player, BlackjackPlayerAction action)
    {
        if (State != GameState.InProgress) throw new InvalidOperationException("Game is not in progress");
        if (!CanPlayerAct(player)) throw new InvalidOperationException("Player cannot take action at this time");

        // Perform the action based on the player's choice
        switch (action)
        {
            case BlackjackPlayerAction.Hit:
                Hit(player);
                break;
            case BlackjackPlayerAction.Stand:
                Stand(player);
                break;
            case BlackjackPlayerAction.DoubleDown:
                DoubleDown(player);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(action), action, null);
        }
    }

    #endregion
    #region Dealer

    protected override bool HasDealer => true;

    private void DealerHit()
    {
        if (!CanDealerAct()) return;

        var card = Deck.DrawCard();
        if (card != null)
        {
            DealerCards.Add(card);
            DealerActions.Add(BlackjackPlayerAction.Hit);
        }
    }

    private void DealerStand()
    {
        if (!CanDealerAct()) return;
        DealerActions.Add(BlackjackPlayerAction.Stand);
    }

    /// <summary>
    /// Checks if the dealer can take action.
    /// <list>
    /// The dealer can act if:
    /// <item>• There is no current player (all players have finished their actions)</item>
    /// <item>• The dealer is not busted</item>
    /// <item>• The dealer last action was not Stand</item>
    /// </list>
    /// </summary>
    public override bool CanDealerAct()
    {
        if (CurrentPlayer != null) return false; // Dealer acts only when all players have finished
        if (IsDealerBusted()) return false;
        if (DealerActions.LastOrDefault() == BlackjackPlayerAction.Stand) return false;
        return true;
    }

    protected override AIAction? GetNextDealerAction()
    {
        if (!CanDealerAct()) return null;

        if (GetDealerValue() < 17 || IsDealerSoft17())
        {
            return new AIAction
            {
                Execute = () => { DealerHit(); return Task.CompletedTask; },
            };
        }

        return new AIAction
        {
            Execute = () => { DealerStand(); return Task.CompletedTask; }
        };
    }

    #endregion
    #region AI Actions

    protected override AIAction? GetNextAIAction()
    {
        if (CurrentPlayer == null) return null;

        // Implement AI decision-making logic here
        return new AIAction
        {
            Execute = () => { DoPlayerAction(CurrentPlayer, BlackjackPlayerAction.Hit); return Task.CompletedTask; }
        };
    }

    #endregion
}