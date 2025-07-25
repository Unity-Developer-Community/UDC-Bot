using DiscordBot.Domain;

/// <summary>
/// Blackjack-specific card value calculations
/// </summary>
public static class BlackjackHelper
{
    public static int CalculateHandValue(List<Card> cards)
    {
        var (value, _) = CalculateHandValueWithAceInfo(cards);
        return value;
    }

    /// <summary>
    /// Checks if the hand is a soft 17 (a 17 with an Ace counted as 11).
    /// </summary>
    /// <returns>
    /// True if the hand is a soft 17 (i.e., an Ace and a 6), false otherwise.
    /// </returns>
    public static bool IsSoft17(List<Card> cards)
    {
        var (value, acesAs11) = CalculateHandValueWithAceInfo(cards);
        return value == 17 && acesAs11 > 0;
    }

    /// <summary>
    /// Calculates the total value of a hand of cards, taking into account Aces as either 1 or 11.
    /// </summary>
    /// <returns>
    /// value: The total value of the hand.
    /// acesAs11: The number of Aces counted as 11 in the total value.
    /// </returns>
    private static (int value, int acesAs11) CalculateHandValueWithAceInfo(List<Card> cards)
    {
        int value = 0;
        int acesAs11 = 0;

        foreach (var card in cards)
        {
            if (card.Value == 1) // Ace
            {
                acesAs11++;
                value += 11;
            }
            else if (card.Value > 10) value += 10; // Face cards are worth 10
            else value += card.Value;
        }

        // Handle Aces (convert from 11 to 1 if needed)
        while (value > 21 && acesAs11 > 0)
        {
            value -= 10; // Convert ace from 11 to 1
            acesAs11--;
        }

        return (value, acesAs11);
    }

    public static bool IsBlackjack(List<Card> cards)
    {
        return cards.Count == 2 && CalculateHandValue(cards) == 21;
    }

    public static bool IsBusted(List<Card> cards)
    {
        return CalculateHandValue(cards) > 21;
    }
}