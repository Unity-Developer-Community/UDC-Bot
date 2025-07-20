namespace DiscordBot.Domain;

/// <summary>
/// Generic card representation that can be used for any card game
/// </summary>
public class Card
{
    public int Value { get; set; } // 1 = Ace, 11-13 = Jack/Queen/King
    public CardSuit Suit { get; set; }

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

    public override bool Equals(object obj)
    {
        if (obj is Card other)
        {
            return Value == other.Value && Suit == other.Suit;
        }
        return false;
    }

    public override int GetHashCode() => HashCode.Combine(Value, Suit);
}

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
/// Generic deck of cards that can be used for any card game
/// </summary>
public class Deck
{
    private List<Card> _cards;
    private Random _random;

    public int CardsRemaining => _cards.Count;
    public bool IsEmpty => _cards.Count == 0;

    public Deck(bool shuffle = true, int jokerCount = 0)
    {
        _random = new Random();
        InitializeStandardDeck(jokerCount);
        if (shuffle)
        {
            Shuffle();
        }
    }

    /// <summary>
    /// Create a deck with custom cards
    /// </summary>
    public Deck(IEnumerable<Card> cards, bool shuffle = true)
    {
        _random = new Random();
        _cards = new List<Card>(cards);
        if (shuffle)
        {
            Shuffle();
        }
    }

    /// <summary>
    /// Initializes a standard deck of 52 cards with optional jokers.
    /// </summary>
    /// <param name="jokerCount">Number of jokers to include in the deck.</param>
    private void InitializeStandardDeck(int jokerCount = 0)
    {
        _cards = new List<Card>();
        var suits = Enum.GetValues<CardSuit>().Where(s => s != CardSuit.Joker);

        foreach (CardSuit suit in suits)
        {
            for (int value = 1; value <= 13; value++)
            {
                _cards.Add(new Card(value, suit));
            }
        }

        // Add specified number of jokers
        for (int i = 1; i <= jokerCount; i++)
        {
            _cards.Add(new Card(i, CardSuit.Joker)); // Joker with unique value
        }
    }

    public void Shuffle()
    {
        for (int i = 0; i < _cards.Count; i++)
        {
            int randomIndex = _random.Next(i, _cards.Count);
            (_cards[i], _cards[randomIndex]) = (_cards[randomIndex], _cards[i]);
        }
    }

    public Card DrawCard()
    {
        if (_cards.Count == 0)
        {
            throw new InvalidOperationException("Cannot draw from an empty deck. Consider reshuffling or checking IsEmpty first.");
        }

        var card = _cards[0];
        _cards.RemoveAt(0);
        return card;
    }

    public List<Card> DrawCards(int count)
    {
        if (count > _cards.Count)
        {
            throw new InvalidOperationException($"Cannot draw {count} cards, only {_cards.Count} cards remaining.");
        }

        var drawnCards = new List<Card>();
        for (int i = 0; i < count; i++)
        {
            drawnCards.Add(DrawCard());
        }
        return drawnCards;
    }

    public void AddCard(Card card)
    {
        _cards.Add(card);
    }

    public void AddCards(IEnumerable<Card> cards)
    {
        _cards.AddRange(cards);
    }

    /// <summary>
    /// Reset deck to a full standard deck and shuffle
    /// </summary>
    public void Reset(bool shuffle = true, int jokerCount = 0)
    {
        InitializeStandardDeck(jokerCount);
        if (shuffle)
        {
            Shuffle();
        }
    }

    public List<Card> PeekTop(int count)
    {
        if (count > _cards.Count)
        {
            throw new InvalidOperationException($"Cannot peek {count} cards, only {_cards.Count} cards remaining.");
        }

        return _cards.Take(count).ToList();
    }
}
