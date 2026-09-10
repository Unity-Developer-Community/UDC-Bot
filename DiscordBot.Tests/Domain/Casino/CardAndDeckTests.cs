using DiscordBot.Domain;

namespace DiscordBot.Tests.Domain.Casino;

public class CardAndDeckTests
{
    [Theory]
    [InlineData(1, CardSuit.Hearts, "A♥️")]
    [InlineData(11, CardSuit.Spades, "J♠️")]
    [InlineData(12, CardSuit.Diamonds, "Q♦️")]
    [InlineData(13, CardSuit.Clubs, "K♣️")]
    [InlineData(10, CardSuit.Hearts, "10♥️")]
    [InlineData(5, CardSuit.Diamonds, "5♦️")]
    public void Card_GetDisplayName(int value, CardSuit suit, string expected)
    {
        var card = new Card(value, suit);
        Assert.Equal(expected, card.GetDisplayName());
    }

    [Fact]
    public void Card_Joker_DisplayName()
    {
        var card = new Card(0, CardSuit.Joker);
        Assert.Equal("🃏", card.GetDisplayName());
    }

    [Fact]
    public void Card_Equals_SameValueAndSuit()
    {
        var a = new Card(5, CardSuit.Hearts);
        var b = new Card(5, CardSuit.Hearts);
        Assert.Equal(a, b);
    }

    [Fact]
    public void Card_NotEquals_DifferentSuit()
    {
        var a = new Card(5, CardSuit.Hearts);
        var b = new Card(5, CardSuit.Spades);
        Assert.NotEqual(a, b);
    }

    [Fact]
    public void Card_CompareTo_ByValue()
    {
        var low = new Card(3, CardSuit.Hearts);
        var high = new Card(10, CardSuit.Hearts);
        Assert.True(low.CompareTo(high) < 0);
    }

    [Fact]
    public void Card_GetHashCode_EqualCards()
    {
        var a = new Card(7, CardSuit.Clubs);
        var b = new Card(7, CardSuit.Clubs);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }

    [Fact]
    public void Deck_StandardDeck_Has52Cards()
    {
        var deck = new Deck();
        Assert.Equal(52, deck.CardsRemaining);
    }

    [Fact]
    public void Deck_WithJokers_Has54Cards()
    {
        var deck = new Deck(jokerCount: 2);
        Assert.Equal(54, deck.CardsRemaining);
    }

    [Fact]
    public void Deck_DoubleDeck_Has104Cards()
    {
        var deck = new Deck(times: 2);
        Assert.Equal(104, deck.CardsRemaining);
    }

    [Fact]
    public void Deck_DrawCard_ReducesCount()
    {
        var deck = new Deck();
        deck.DrawCard();
        Assert.Equal(51, deck.CardsRemaining);
    }

    [Fact]
    public void Deck_DrawCard_EmptyDeck_ReturnsNull()
    {
        var deck = new Deck(new List<Card>());
        Assert.Null(deck.DrawCard());
    }

    [Fact]
    public void Deck_DrawCards_ReturnsExactCount()
    {
        var deck = new Deck();
        var cards = deck.DrawCards(5);
        Assert.Equal(5, cards.Count);
        Assert.Equal(47, deck.CardsRemaining);
    }

    [Fact]
    public void Deck_DrawCards_MoreThanRemaining_ReturnsAvailable()
    {
        var deck = new Deck(new List<Card> { new(1, CardSuit.Hearts), new(2, CardSuit.Hearts) });
        var cards = deck.DrawCards(5);
        Assert.Equal(2, cards.Count);
        Assert.True(deck.IsEmpty);
    }

    [Fact]
    public void Deck_Shuffle_CountUnchanged()
    {
        var deck = new Deck();
        deck.Shuffle();
        Assert.Equal(52, deck.CardsRemaining);
    }

    [Fact]
    public void Deck_Reset_RestoresAllCards()
    {
        var deck = new Deck();
        deck.DrawCards(10);
        Assert.Equal(42, deck.CardsRemaining);
        deck.Reset();
        Assert.Equal(52, deck.CardsRemaining);
    }

    [Fact]
    public void Deck_IsEmpty_ForEmptyDeck()
    {
        var deck = new Deck(new List<Card>());
        Assert.True(deck.IsEmpty);
    }

    [Fact]
    public void Deck_IsEmpty_FalseForNewDeck()
    {
        var deck = new Deck();
        Assert.False(deck.IsEmpty);
    }

    [Fact]
    public void Deck_PeekTop_DoesNotRemoveCards()
    {
        var deck = new Deck();
        var peeked = deck.PeekTop(3);
        Assert.Equal(3, peeked.Count);
        Assert.Equal(52, deck.CardsRemaining);
    }

    [Fact]
    public void Deck_StandardDeck_ContainsAllSuitsAndValues()
    {
        var deck = new Deck();
        var allCards = deck.DrawCards(52);
        var suits = new[] { CardSuit.Hearts, CardSuit.Diamonds, CardSuit.Clubs, CardSuit.Spades };
        foreach (var suit in suits)
        {
            for (int value = 1; value <= 13; value++)
            {
                Assert.Contains(allCards, c => c.Value == value && c.Suit == suit);
            }
        }
    }
}
