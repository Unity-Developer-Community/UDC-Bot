using DiscordBot.Domain;

namespace DiscordBot.Tests.Domain.Casino;

public class PokerHelperTests
{
    private static Card C(int value, CardSuit suit = CardSuit.Hearts) => new(value, suit);

    [Fact]
    public void EvaluateHand_RoyalFlush()
    {
        var cards = new List<Card> { C(1, CardSuit.Spades), C(13, CardSuit.Spades), C(12, CardSuit.Spades), C(11, CardSuit.Spades), C(10, CardSuit.Spades) };
        var hand = PokerHelper.EvaluateHand(cards);
        Assert.Equal(PokerHandRank.RoyalFlush, hand.Rank);
    }

    [Fact]
    public void EvaluateHand_StraightFlush()
    {
        var cards = new List<Card> { C(9, CardSuit.Hearts), C(8, CardSuit.Hearts), C(7, CardSuit.Hearts), C(6, CardSuit.Hearts), C(5, CardSuit.Hearts) };
        var hand = PokerHelper.EvaluateHand(cards);
        Assert.Equal(PokerHandRank.StraightFlush, hand.Rank);
    }

    [Fact]
    public void EvaluateHand_WheelStraightFlush()
    {
        var cards = new List<Card> { C(1, CardSuit.Clubs), C(2, CardSuit.Clubs), C(3, CardSuit.Clubs), C(4, CardSuit.Clubs), C(5, CardSuit.Clubs) };
        var hand = PokerHelper.EvaluateHand(cards);
        Assert.Equal(PokerHandRank.StraightFlush, hand.Rank);
        Assert.Contains("Wheel", hand.Description);
    }

    [Fact]
    public void EvaluateHand_FourOfAKind()
    {
        var cards = new List<Card> { C(7), C(7, CardSuit.Diamonds), C(7, CardSuit.Clubs), C(7, CardSuit.Spades), C(2) };
        var hand = PokerHelper.EvaluateHand(cards);
        Assert.Equal(PokerHandRank.FourOfAKind, hand.Rank);
    }

    [Fact]
    public void EvaluateHand_FullHouse()
    {
        var cards = new List<Card> { C(10), C(10, CardSuit.Diamonds), C(10, CardSuit.Clubs), C(4), C(4, CardSuit.Spades) };
        var hand = PokerHelper.EvaluateHand(cards);
        Assert.Equal(PokerHandRank.FullHouse, hand.Rank);
    }

    [Fact]
    public void EvaluateHand_Flush()
    {
        var cards = new List<Card> { C(2, CardSuit.Diamonds), C(5, CardSuit.Diamonds), C(8, CardSuit.Diamonds), C(10, CardSuit.Diamonds), C(13, CardSuit.Diamonds) };
        var hand = PokerHelper.EvaluateHand(cards);
        Assert.Equal(PokerHandRank.Flush, hand.Rank);
    }

    [Fact]
    public void EvaluateHand_Straight()
    {
        var cards = new List<Card> { C(9, CardSuit.Hearts), C(8, CardSuit.Diamonds), C(7, CardSuit.Clubs), C(6, CardSuit.Spades), C(5, CardSuit.Hearts) };
        var hand = PokerHelper.EvaluateHand(cards);
        Assert.Equal(PokerHandRank.Straight, hand.Rank);
    }

    [Fact]
    public void EvaluateHand_WheelStraight()
    {
        var cards = new List<Card> { C(1, CardSuit.Hearts), C(2, CardSuit.Diamonds), C(3, CardSuit.Clubs), C(4, CardSuit.Spades), C(5, CardSuit.Hearts) };
        var hand = PokerHelper.EvaluateHand(cards);
        Assert.Equal(PokerHandRank.Straight, hand.Rank);
        Assert.Contains("Wheel", hand.Description);
    }

    [Fact]
    public void EvaluateHand_ThreeOfAKind()
    {
        var cards = new List<Card> { C(9), C(9, CardSuit.Diamonds), C(9, CardSuit.Clubs), C(3), C(7, CardSuit.Spades) };
        var hand = PokerHelper.EvaluateHand(cards);
        Assert.Equal(PokerHandRank.ThreeOfAKind, hand.Rank);
    }

    [Fact]
    public void EvaluateHand_TwoPair()
    {
        var cards = new List<Card> { C(8), C(8, CardSuit.Diamonds), C(5, CardSuit.Clubs), C(5, CardSuit.Spades), C(2) };
        var hand = PokerHelper.EvaluateHand(cards);
        Assert.Equal(PokerHandRank.TwoPair, hand.Rank);
    }

    [Fact]
    public void EvaluateHand_OnePair()
    {
        var cards = new List<Card> { C(11), C(11, CardSuit.Diamonds), C(3, CardSuit.Clubs), C(7, CardSuit.Spades), C(9) };
        var hand = PokerHelper.EvaluateHand(cards);
        Assert.Equal(PokerHandRank.OnePair, hand.Rank);
    }

    [Fact]
    public void EvaluateHand_HighCard()
    {
        var cards = new List<Card> { C(1), C(10, CardSuit.Diamonds), C(7, CardSuit.Clubs), C(4, CardSuit.Spades), C(2) };
        var hand = PokerHelper.EvaluateHand(cards);
        Assert.Equal(PokerHandRank.HighCard, hand.Rank);
    }

    [Fact]
    public void EvaluateHand_WrongCardCount_Throws()
    {
        var cards = new List<Card> { C(1), C(2), C(3), C(4) };
        Assert.Throws<ArgumentException>(() => PokerHelper.EvaluateHand(cards));
    }

    [Fact]
    public void CompareHands_HigherRankWins()
    {
        var flush = PokerHelper.EvaluateHand([C(2, CardSuit.Hearts), C(5, CardSuit.Hearts), C(8, CardSuit.Hearts), C(10, CardSuit.Hearts), C(13, CardSuit.Hearts)]);
        var pair = PokerHelper.EvaluateHand([C(3), C(3, CardSuit.Diamonds), C(7, CardSuit.Clubs), C(9, CardSuit.Spades), C(11)]);
        Assert.True(PokerHelper.CompareHands(flush, pair) > 0);
    }

    [Fact]
    public void CompareHands_KickerBreaksTie()
    {
        var pairHigh = PokerHelper.EvaluateHand([C(10), C(10, CardSuit.Diamonds), C(1, CardSuit.Clubs), C(7, CardSuit.Spades), C(3)]);
        var pairLow = PokerHelper.EvaluateHand([C(10, CardSuit.Clubs), C(10, CardSuit.Spades), C(9), C(7, CardSuit.Diamonds), C(3, CardSuit.Diamonds)]);
        Assert.True(PokerHelper.CompareHands(pairHigh, pairLow) > 0);
    }

    [Fact]
    public void CompareHands_IdenticalHands_ReturnsZero()
    {
        var hand1 = PokerHelper.EvaluateHand([C(1), C(13, CardSuit.Diamonds), C(10, CardSuit.Clubs), C(7, CardSuit.Spades), C(4)]);
        var hand2 = PokerHelper.EvaluateHand([C(1, CardSuit.Diamonds), C(13, CardSuit.Clubs), C(10, CardSuit.Spades), C(7), C(4, CardSuit.Diamonds)]);
        Assert.Equal(0, PokerHelper.CompareHands(hand1, hand2));
    }

    [Fact]
    public void CompareHands_NullHandling()
    {
        var hand = PokerHelper.EvaluateHand([C(1), C(13, CardSuit.Diamonds), C(10, CardSuit.Clubs), C(7, CardSuit.Spades), C(4)]);
        Assert.True(PokerHelper.CompareHands(hand, null!) > 0);
        Assert.True(PokerHelper.CompareHands(null!, hand) < 0);
        Assert.Equal(0, PokerHelper.CompareHands(null!, null!));
    }
}
