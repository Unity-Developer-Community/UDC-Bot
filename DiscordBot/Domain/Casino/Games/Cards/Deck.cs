
using DiscordBot.Domain;

/// <summary>
/// Generic deck of cards that can be used for any card game
/// </summary>
public class Deck
{
    private readonly Random _random;
    private readonly IReadOnlyList<Card> _initialCards = []; // Store initial cards for reset
    private List<Card> _cards = []; // Current deck of cards

    public int CardsRemaining => _cards.Count;
    public bool IsEmpty => _cards.Count == 0;

    /// <summary>
    /// Create an unshuffled standard deck of cards with optional jokers and optional repetition
    /// </summary>
    /// <param name="jokerCount">Number of jokers to include in the deck</param>
    /// <param name="times">Number of times to repeat the standard deck</param>
    public Deck(int jokerCount = 0, int times = 1)
    {
        _random = new Random();
        _cards = InitializeStandardDeck(jokerCount, times);
        _initialCards = [.. _cards]; // Store initial cards for reset
    }

    /// <summary>
    /// Create an unshuffled deck with custom cards
    /// </summary>
    public Deck(IEnumerable<Card> cards)
    {
        _random = new Random();
        _cards = [.. cards];
        _initialCards = [.. _cards]; // Store initial cards for reset
    }

    /// <summary>
    /// Initializes a standard deck of 52 cards with optional jokers.
    /// The deck is unshuffled.
    /// </summary>
    /// <param name="jokerCount">Number of jokers to include in the deck.</param>
    private static List<Card> InitializeStandardDeck(int jokerCount = 0)
    {
        var cards = new List<Card>();
        var suits = Enum.GetValues<CardSuit>().Where(s => s != CardSuit.Joker);

        foreach (CardSuit suit in suits)
            for (int value = 1; value <= 13; value++)
                cards.Add(new Card(value, suit));

        // Add specified number of jokers
        for (int i = 1; i <= jokerCount; i++)
            cards.Add(new Card(i, CardSuit.Joker)); // Joker with unique value

        return cards;
    }

    /// <summary>
    /// Initializes a standard deck of 52 cards with optional jokers, repeated multiple times.
    /// </summary>
    /// <param name="jokerCount">Number of jokers to include in each deck.</param>
    /// <param name="times">Number of times to repeat the standard deck.</param>
    private static List<Card> InitializeStandardDeck(int jokerCount = 0, int times = 1)
    {
        var cards = new List<Card>();
        for (int i = 0; i < times; i++)
            cards.AddRange(InitializeStandardDeck(jokerCount));
        return cards;
    }

    /// <summary>
    /// Shuffles the deck using Fisher-Yates algorithm.
    /// </summary>
    public void Shuffle()
    {
        for (int i = 0; i < _cards.Count; i++)
        {
            int k = _random.Next(i, _cards.Count);
            (_cards[i], _cards[k]) = (_cards[k], _cards[i]);
        }
    }

    public Card? DrawCard()
    {
        if (_cards.Count == 0) return null;

        var card = _cards[0];
        _cards.RemoveAt(0);
        return card;
    }

    public List<Card> DrawCards(int count)
    {
        // If count exceeds remaining cards, adjust to draw only available cards
        if (count > _cards.Count) count = _cards.Count;

        var drawnCards = new List<Card>();
        for (int i = 0; i < count; i++)
        {
            var card = DrawCard();
            if (card != null) drawnCards.Add(card);
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
    /// Reset deck with initial cards and shuffle
    /// </summary>
    public void Reset(bool shuffle = true, int jokerCount = 0)
    {
        _cards = [.. _initialCards];
        if (shuffle) Shuffle();
    }

    public List<Card> PeekTop(int count)
    {
        return [.. _cards.Take(count)];
    }
}