namespace DiscordBot.Domain;

/// <summary>
/// Poker hand rankings for five card draw poker
/// </summary>
public enum PokerHandRank
{
    HighCard = 0,
    OnePair = 1,
    TwoPair = 2,
    ThreeOfAKind = 3,
    Straight = 4,
    Flush = 5,
    FullHouse = 6,
    FourOfAKind = 7,
    StraightFlush = 8,
    RoyalFlush = 9
}

/// <summary>
/// Represents a poker hand with its ranking and description
/// </summary>
public class PokerHand
{
    public PokerHandRank Rank { get; set; }
    public string Description { get; set; } = "";
    public List<Card> Cards { get; set; } = [];
    public List<int> KickerValues { get; set; } = []; // For tie-breaking
}

/// <summary>
/// Helper class for poker-specific functionality
/// </summary>
public static class PokerHelper
{
    /// <summary>
    /// Evaluates a poker hand and returns its ranking
    /// </summary>
    public static PokerHand EvaluateHand(List<Card> cards)
    {
        if (cards.Count != 5)
            throw new ArgumentException("Poker hand must contain exactly 5 cards");

        var sortedCards = cards.OrderByDescending(c => GetPokerValue(c.Value)).ToList();
        var hand = new PokerHand { Cards = sortedCards };

        // Check for flush
        bool isFlush = cards.All(c => c.Suit == cards[0].Suit);

        // Check for straight
        bool isStraight = IsStraight(sortedCards);

        // Special case: A-2-3-4-5 straight (wheel)
        bool isWheelStraight = IsWheelStraight(sortedCards);
        if (isWheelStraight)
        {
            isStraight = true;
            // Reorder for wheel straight (A should be low)
            var ace = sortedCards.First(c => c.Value == 1);
            sortedCards.Remove(ace);
            sortedCards.Add(ace);
        }

        // Group cards by value for pair detection
        var valueGroups = sortedCards.GroupBy(c => GetPokerValue(c.Value))
                                   .OrderByDescending(g => g.Count())
                                   .ThenByDescending(g => g.Key)
                                   .ToList();

        // Determine hand ranking
        if (isFlush && isStraight)
        {
            if (sortedCards.All(c => GetPokerValue(c.Value) >= 10) && !isWheelStraight)
            {
                hand.Rank = PokerHandRank.RoyalFlush;
                hand.Description = "Royal Flush";
            }
            else
            {
                hand.Rank = PokerHandRank.StraightFlush;
                hand.Description = isWheelStraight ? "Straight Flush (Wheel)" : "Straight Flush";
                hand.KickerValues = [isWheelStraight ? 5 : GetPokerValue(sortedCards[0].Value)]; // High card of straight (5 for wheel)
            }
        }
        else if (valueGroups[0].Count() == 4)
        {
            hand.Rank = PokerHandRank.FourOfAKind;
            hand.Description = $"Four of a Kind ({GetCardName(valueGroups[0].Key)}s)";
            hand.KickerValues = [valueGroups[0].Key, valueGroups[1].Key];
        }
        else if (valueGroups[0].Count() == 3 && valueGroups[1].Count() == 2)
        {
            hand.Rank = PokerHandRank.FullHouse;
            hand.Description = $"Full House ({GetCardName(valueGroups[0].Key)}s over {GetCardName(valueGroups[1].Key)}s)";
            hand.KickerValues = [valueGroups[0].Key, valueGroups[1].Key];
        }
        else if (isFlush)
        {
            hand.Rank = PokerHandRank.Flush;
            hand.Description = "Flush";
            hand.KickerValues = sortedCards.Select(c => GetPokerValue(c.Value)).ToList();
        }
        else if (isStraight)
        {
            hand.Rank = PokerHandRank.Straight;
            hand.Description = isWheelStraight ? "Straight (Wheel)" : "Straight";
            hand.KickerValues = [isWheelStraight ? 5 : GetPokerValue(sortedCards[0].Value)]; // High card of straight (5 for wheel)
        }
        else if (valueGroups[0].Count() == 3)
        {
            hand.Rank = PokerHandRank.ThreeOfAKind;
            hand.Description = $"Three of a Kind ({GetCardName(valueGroups[0].Key)}s)";
            hand.KickerValues = [valueGroups[0].Key, valueGroups[1].Key, valueGroups[2].Key];
        }
        else if (valueGroups[0].Count() == 2 && valueGroups[1].Count() == 2)
        {
            hand.Rank = PokerHandRank.TwoPair;
            var highPair = Math.Max(valueGroups[0].Key, valueGroups[1].Key);
            var lowPair = Math.Min(valueGroups[0].Key, valueGroups[1].Key);
            hand.Description = $"Two Pair ({GetCardName(highPair)}s and {GetCardName(lowPair)}s)";
            hand.KickerValues = [highPair, lowPair, valueGroups[2].Key];
        }
        else if (valueGroups[0].Count() == 2)
        {
            hand.Rank = PokerHandRank.OnePair;
            hand.Description = $"Pair of {GetCardName(valueGroups[0].Key)}s";
            hand.KickerValues = [valueGroups[0].Key, valueGroups[1].Key, valueGroups[2].Key, valueGroups[3].Key];
        }
        else
        {
            hand.Rank = PokerHandRank.HighCard;
            hand.Description = $"{GetCardName(GetPokerValue(sortedCards[0].Value))} High";
            hand.KickerValues = sortedCards.Select(c => GetPokerValue(c.Value)).ToList();
        }

        return hand;
    }

    /// <summary>
    /// Compares two poker hands and returns the winner
    /// </summary>
    public static int CompareHands(PokerHand hand1, PokerHand hand2)
    {
        // Null checks
        if (hand1 == null && hand2 == null) return 0;
        if (hand1 == null) return -1;
        if (hand2 == null) return 1;

        // First compare by rank
        if (hand1.Rank != hand2.Rank)
            return hand1.Rank.CompareTo(hand2.Rank);

        // If ranks are equal, compare kicker values
        // Ensure KickerValues are not null
        var kickers1 = hand1.KickerValues ?? [];
        var kickers2 = hand2.KickerValues ?? [];

        for (int i = 0; i < Math.Min(kickers1.Count, kickers2.Count); i++)
        {
            if (kickers1[i] != kickers2[i])
                return kickers1[i].CompareTo(kickers2[i]);
        }

        return 0; // Hands are exactly equal
    }

    /// <summary>
    /// Determines which players win the pot and their share
    /// </summary>
    public static List<(GamePlayer player, PokerHand hand, decimal share)> DetermineWinners(
        List<(GamePlayer player, PokerHand hand)> playerHands)
    {
        if (!playerHands.Any()) return [];

        // Filter out any null hands first
        var validHands = playerHands.Where(ph => ph.hand != null).ToList();
        if (!validHands.Any()) return [];

        // Find the best hand(s)
        var bestHand = validHands.OrderByDescending(ph => ph.hand, new PokerHandComparer()).First().hand;
        var winners = validHands.Where(ph => CompareHands(ph.hand, bestHand) == 0).ToList();

        // Calculate each winner's share (equal split for ties)
        decimal share = winners.Count > 0 ? 1.0m / winners.Count : 0;

        return winners.Select(w => (w.player, w.hand, share)).ToList();
    }

    /// <summary>
    /// Gets the poker value for a card (Ace high = 14, King = 13, etc.)
    /// </summary>
    private static int GetPokerValue(int cardValue)
    {
        return cardValue == 1 ? 14 : cardValue; // Ace high
    }

    /// <summary>
    /// Gets the display name for a card value
    /// </summary>
    private static string GetCardName(int pokerValue)
    {
        return pokerValue switch
        {
            14 => "Ace",
            13 => "King",
            12 => "Queen",
            11 => "Jack",
            _ => pokerValue.ToString()
        };
    }

    /// <summary>
    /// Checks if the cards form a straight
    /// </summary>
    private static bool IsStraight(List<Card> sortedCards)
    {
        var values = sortedCards.Select(c => GetPokerValue(c.Value)).ToList();
        for (int i = 1; i < values.Count; i++)
        {
            if (values[i] != values[i - 1] - 1)
                return false;
        }
        return true;
    }

    /// <summary>
    /// Checks if the cards form a wheel straight (A-2-3-4-5)
    /// </summary>
    private static bool IsWheelStraight(List<Card> sortedCards)
    {
        var values = sortedCards.Select(c => c.Value).OrderBy(v => v).ToList();
        return values.SequenceEqual([1, 2, 3, 4, 5]);
    }
}

/// <summary>
/// Comparer for poker hands
/// </summary>
public class PokerHandComparer : IComparer<PokerHand>
{
    public int Compare(PokerHand? x, PokerHand? y)
    {
        if (x == null && y == null) return 0;
        if (x == null) return -1;
        if (y == null) return 1;

        return PokerHelper.CompareHands(x, y);
    }
}