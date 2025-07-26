namespace DiscordBot.Domain;

/// <summary>
/// Represents the different actions a player can take in a poker game
/// </summary>
public enum PokerPlayerAction
{
    SelectCard1,
    SelectCard2,
    SelectCard3,
    SelectCard4,
    SelectCard5,
    ConfirmDiscard
}

public class PokerPlayerData : ICasinoGamePlayerData
{
    public List<Card> PlayerCards { get; set; } = [];
    public List<bool> SelectedForDiscard { get; set; } = [false, false, false, false, false];
    public bool HasDiscarded { get; set; } = false;
    public PokerHand? FinalHand { get; set; }
}

public class Poker : ACasinoGame<PokerPlayerData, PokerPlayerAction>
{
    public override string Emoji => "🃏";
    public override string Name => "Poker";
    public override int MinPlayers => 2;
    public override int MaxPlayers => 8; // 5 cards per player, max 8 players with 52 card deck

    public override bool HasPrivateHands => true;

    public Deck Deck { get; set; } = new Deck();

    /// <summary>
    /// The current player is the next player who hasn't discarded yet
    /// </summary>
    public override GamePlayer? CurrentPlayer
    {
        get
        {
            if (State != GameState.InProgress) return null;

            return Players.Where(p => !GameData[p].HasDiscarded).FirstOrDefault();
        }
    }

    #region Start Game

    protected override PokerPlayerData CreatePlayerData(GamePlayer player) => new();

    protected override void InitializeGame()
    {
        State = GameState.InProgress;

        // Create a new deck for the game
        Deck = new Deck();
        Deck.Shuffle();

        // Clear previous data
        foreach (var player in Players)
        {
            GameData[player].PlayerCards.Clear();
            GameData[player].SelectedForDiscard = [false, false, false, false, false];
            GameData[player].HasDiscarded = false;
            GameData[player].FinalHand = null;
        }

        // Deal 5 cards to each player
        for (int i = 0; i < 5; i++)
        {
            foreach (var player in Players)
            {
                var card = Deck.DrawCard();
                if (card != null) GameData[player].PlayerCards.Add(card);
            }
        }

    }

    #endregion

    #region Player Actions

    /// <summary>
    /// Checks if the player can take action (select cards or confirm discard).
    /// </summary>
    public bool CanPlayerAct(GamePlayer player)
    {
        if (CurrentPlayer != player) return false;
        if (GameData[player].HasDiscarded) return false;
        return true;
    }

    private void SelectCard(GamePlayer player, int cardIndex)
    {
        if (!CanPlayerAct(player)) return;
        if (cardIndex < 0 || cardIndex >= 5) return;

        // Toggle selection
        GameData[player].SelectedForDiscard[cardIndex] = !GameData[player].SelectedForDiscard[cardIndex];
    }

    private void ConfirmDiscard(GamePlayer player)
    {
        if (!CanPlayerAct(player)) return;

        var playerData = GameData[player];
        var selectedCards = new List<Card>();

        // Collect cards selected for discard
        for (int i = 0; i < 5; i++)
        {
            if (playerData.SelectedForDiscard[i])
            {
                selectedCards.Add(playerData.PlayerCards[i]);
            }
        }

        // Remove selected cards and draw new ones
        foreach (var card in selectedCards)
        {
            var index = playerData.PlayerCards.IndexOf(card);
            if (index >= 0)
            {
                playerData.PlayerCards.RemoveAt(index);
                var newCard = Deck.DrawCard();
                if (newCard != null)
                {
                    playerData.PlayerCards.Insert(index, newCard);
                }
            }
        }

        // Reset selection and mark as discarded
        playerData.SelectedForDiscard = [false, false, false, false, false];
        playerData.HasDiscarded = true;
    }

    public override void DoPlayerAction(GamePlayer player, PokerPlayerAction action)
    {
        if (State != GameState.InProgress) throw new InvalidOperationException("Game is not in progress");
        if (!CanPlayerAct(player)) throw new InvalidOperationException("Player cannot take action at this time");

        // Perform the action based on the player's choice
        switch (action)
        {
            case PokerPlayerAction.SelectCard1:
                SelectCard(player, 0);
                break;
            case PokerPlayerAction.SelectCard2:
                SelectCard(player, 1);
                break;
            case PokerPlayerAction.SelectCard3:
                SelectCard(player, 2);
                break;
            case PokerPlayerAction.SelectCard4:
                SelectCard(player, 3);
                break;
            case PokerPlayerAction.SelectCard5:
                SelectCard(player, 4);
                break;
            case PokerPlayerAction.ConfirmDiscard:
                ConfirmDiscard(player);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(action), action, null);
        }
    }

    #endregion

    #region Show Hand Implementation

    public override string ShowHand(GamePlayer player)
    {
        if (!GameData.ContainsKey(player)) return "No hand available.";

        var playerData = GameData[player];
        if (playerData.PlayerCards.Count != 5) return "Hand incomplete.";

        var hand = string.Join(" ", playerData.PlayerCards.OrderByDescending(c => c).Select((card, index) =>
        {
            var display = card.GetDisplayName();
            if (playerData.SelectedForDiscard[index])
                display = $"~~{display}~~"; // Strike through selected cards
            return display;
        }));

        var handEvaluation = PokerHelper.EvaluateHand(playerData.PlayerCards);

        return $"**Your Hand:** {hand}\n**Hand Rank:** {handEvaluation.Description}" +
               (playerData.SelectedForDiscard.Any(s => s) ? "\n*(Strikethrough cards will be discarded)*" : "");
    }

    #endregion

    #region End Game

    public override GamePlayerResult GetPlayerGameResult(GamePlayer player)
    {
        if (player.Result != GamePlayerResult.NoResult) return player.Result;

        // Evaluate all hands if not done yet
        foreach (var p in Players.Where(p => GameData[p].FinalHand == null))
        {
            GameData[p].FinalHand = PokerHelper.EvaluateHand(GameData[p].PlayerCards);
        }

        // Determine winners - only include players with valid hands
        var allHands = Players.Where(p => GameData[p].FinalHand != null)
                              .Select(p => (p, GameData[p].FinalHand!)).ToList();
        var winners = PokerHelper.DetermineWinners(allHands);

        return winners.Any(w => w.player == player) ? GamePlayerResult.Won : GamePlayerResult.Lost;
    }

    public override long CalculatePayout(GamePlayer player, ulong totalPot)
    {
        var result = GetPlayerGameResult(player);
        if (result == GamePlayerResult.Lost)
            return -(long)player.Bet;

        // Calculate winner's share
        var allHands = Players.Where(p => GameData[p].FinalHand != null)
                              .Select(p => (p, GameData[p].FinalHand!)).ToList();
        var winners = PokerHelper.DetermineWinners(allHands);
        var winner = winners.FirstOrDefault(w => w.player == player);

        if (winner.player != null)
        {
            // Winner gets their share of the total pot minus their original bet
            var winnings = (long)(totalPot * (ulong)winner.share);
            return winnings - (long)player.Bet;
        }

        return -(long)player.Bet;
    }

    public override bool ShouldFinish() => State == GameState.InProgress && Players.All(p => GameData[p].HasDiscarded);

    #endregion

    #region Dealer Actions (Not used in Poker)

    protected override bool HasDealer => false;

    #endregion

    #region AI Actions (Not implemented yet)

    protected override AIAction? GetNextAIAction()
    {
        return new AIAction
        {
            Execute = () => { DoPlayerAction(CurrentPlayer!, PokerPlayerAction.ConfirmDiscard); return Task.CompletedTask; },
        };
    }

    #endregion
}