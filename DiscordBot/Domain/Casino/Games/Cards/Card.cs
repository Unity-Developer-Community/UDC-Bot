namespace DiscordBot.Domain;

/// <summary>
/// Card suits enumeration
/// </summary>
public enum CardSuit
{
    Hearts,
    Diamonds,
    Clubs,
    Spades,
    Joker  // Special suit for jokers
}

/// <summary>
/// Extension methods for CardSuit
/// </summary>
public static class CardSuitExtensions
{
    public static string GetEmoji(this CardSuit suit) => suit switch
    {
        CardSuit.Hearts => "♥️",
        CardSuit.Diamonds => "♦️",
        CardSuit.Clubs => "♣️",
        CardSuit.Spades => "♠️",
        CardSuit.Joker => "🃏",
        _ => ""
    };

    public static string GetName(this CardSuit suit) => suit.ToString();
}

/// <summary>
/// Generic card representation that can be used for any card game
/// </summary>
public class Card
{
    public int Value { get; init; } // 1 = Ace, 11-13 = Jack/Queen/King
    public CardSuit Suit { get; init; }

    public Card(int value, CardSuit suit)
    {
        Value = value;
        Suit = suit;
    }

    public string GetDisplayName()
    {
        // Handle Jokers specially
        if (Suit == CardSuit.Joker) return Suit.GetEmoji();

        string cardName = Value switch
        {
            1 => "A",
            11 => "J",
            12 => "Q",
            13 => "K",
            _ => Value.ToString()
        };

        return $"{cardName}{Suit.GetEmoji()}";
    }

    public override string ToString() => GetDisplayName();

    public override bool Equals(object? obj)
    {
        if (obj is Card other) return Value == other.Value && Suit == other.Suit;
        return false;
    }

    public override int GetHashCode() => HashCode.Combine(Value, Suit);
}