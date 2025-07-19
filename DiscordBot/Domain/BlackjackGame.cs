using Discord;

namespace DiscordBot.Domain;

public class ActiveGame
{
    public ulong UserId { get; set; }
    public string GameType { get; set; }
    public ulong Bet { get; set; }
    public DateTime StartTime { get; set; }
    public DateTime ExpiryTime { get; set; }
    public IUserMessage Message { get; set; }
    public BlackjackGame BlackjackGame { get; set; }
    public bool IsCompleted { get; set; }
}

public class BlackjackGame
{
    public List<Card> PlayerCards { get; set; } = new List<Card>();
    public List<Card> DealerCards { get; set; } = new List<Card>();
    public Deck Deck { get; set; }
    public BlackjackGameState State { get; set; }
    public bool PlayerTurn { get; set; } = true;
    public bool DoubleDown { get; set; } = false;

    public BlackjackGame()
    {
        Deck = new Deck();
        State = BlackjackGameState.InProgress;
    }

    public int GetPlayerValue()
    {
        return CalculateHandValue(PlayerCards);
    }

    public int GetDealerValue()
    {
        return CalculateHandValue(DealerCards);
    }

    private int CalculateHandValue(List<Card> cards)
    {
        int value = 0;
        int aces = 0;

        foreach (var card in cards)
        {
            if (card.Value == 1) // Ace
            {
                aces++;
                value += 11;
            }
            else if (card.Value > 10)
            {
                value += 10;
            }
            else
            {
                value += card.Value;
            }
        }

        // Handle Aces (convert from 11 to 1 if needed)
        while (value > 21 && aces > 0)
        {
            value -= 10;
            aces--;
        }

        return value;
    }

    public bool IsPlayerBusted() => GetPlayerValue() > 21;
    public bool IsDealerBusted() => GetDealerValue() > 21;
    public bool IsPlayerBlackjack() => PlayerCards.Count == 2 && GetPlayerValue() == 21;
    public bool IsDealerBlackjack() => DealerCards.Count == 2 && GetDealerValue() == 21;
}

public enum BlackjackGameState
{
    InProgress,
    PlayerWins,
    DealerWins,
    Tie,
    PlayerBusted,
    DealerBusted
}

public class Card
{
    public int Value { get; set; } // 1 = Ace, 11-13 = Jack/Queen/King
    public string Suit { get; set; } // Hearts, Diamonds, Clubs, Spades

    public string GetDisplayName()
    {
        string cardName = Value switch
        {
            1 => "A",
            11 => "J",
            12 => "Q",
            13 => "K",
            _ => Value.ToString()
        };
        
        string suitEmoji = Suit switch
        {
            "Hearts" => "♥️",
            "Diamonds" => "♦️",
            "Clubs" => "♣️",
            "Spades" => "♠️",
            _ => ""
        };

        return $"{cardName}{suitEmoji}";
    }
}

public class Deck
{
    private List<Card> _cards;
    private Random _random;

    public Deck()
    {
        _random = new Random();
        InitializeDeck();
        Shuffle();
    }

    private void InitializeDeck()
    {
        _cards = new List<Card>();
        string[] suits = { "Hearts", "Diamonds", "Clubs", "Spades" };

        foreach (string suit in suits)
        {
            for (int value = 1; value <= 13; value++)
            {
                _cards.Add(new Card { Value = value, Suit = suit });
            }
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
            // Reshuffle if deck is empty
            InitializeDeck();
            Shuffle();
        }

        var card = _cards[0];
        _cards.RemoveAt(0);
        return card;
    }
}