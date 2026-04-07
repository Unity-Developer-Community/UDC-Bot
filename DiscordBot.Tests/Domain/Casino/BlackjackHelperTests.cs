using DiscordBot.Domain;

namespace DiscordBot.Tests.Domain.Casino;

public class BlackjackHelperTests
{
    private static Card C(int value, CardSuit suit = CardSuit.Hearts) => new(value, suit);

    [Fact]
    public void CalculateHandValue_SimpleHand()
    {
        var cards = new List<Card> { C(5), C(6) };
        Assert.Equal(11, BlackjackHelper.CalculateHandValue(cards));
    }

    [Fact]
    public void CalculateHandValue_FaceCards_Worth10()
    {
        var cards = new List<Card> { C(11), C(12) };
        Assert.Equal(20, BlackjackHelper.CalculateHandValue(cards));
    }

    [Fact]
    public void CalculateHandValue_AceAs11()
    {
        var cards = new List<Card> { C(1), C(5) };
        Assert.Equal(16, BlackjackHelper.CalculateHandValue(cards));
    }

    [Fact]
    public void CalculateHandValue_AceReducedTo1WhenBusting()
    {
        var cards = new List<Card> { C(1), C(10), C(5) };
        Assert.Equal(16, BlackjackHelper.CalculateHandValue(cards));
    }

    [Fact]
    public void CalculateHandValue_TwoAces()
    {
        var cards = new List<Card> { C(1), C(1) };
        Assert.Equal(12, BlackjackHelper.CalculateHandValue(cards));
    }

    [Fact]
    public void CalculateHandValue_TwoAcesAndNine_Makes21()
    {
        var cards = new List<Card> { C(1), C(1), C(9) };
        Assert.Equal(21, BlackjackHelper.CalculateHandValue(cards));
    }

    [Fact]
    public void CalculateHandValue_EmptyHand_ReturnsZero()
    {
        Assert.Equal(0, BlackjackHelper.CalculateHandValue([]));
    }

    [Fact]
    public void IsBlackjack_AceAndKing_True()
    {
        var cards = new List<Card> { C(1), C(13) };
        Assert.True(BlackjackHelper.IsBlackjack(cards));
    }

    [Fact]
    public void IsBlackjack_AceAndTen_True()
    {
        var cards = new List<Card> { C(1), C(10) };
        Assert.True(BlackjackHelper.IsBlackjack(cards));
    }

    [Fact]
    public void IsBlackjack_ThreeCardsTotaling21_False()
    {
        var cards = new List<Card> { C(7), C(7), C(7) };
        Assert.False(BlackjackHelper.IsBlackjack(cards));
    }

    [Fact]
    public void IsBlackjack_TwoCardsNot21_False()
    {
        var cards = new List<Card> { C(10), C(9) };
        Assert.False(BlackjackHelper.IsBlackjack(cards));
    }

    [Fact]
    public void IsBusted_Over21_True()
    {
        var cards = new List<Card> { C(10), C(10), C(5) };
        Assert.True(BlackjackHelper.IsBusted(cards));
    }

    [Fact]
    public void IsBusted_Exactly21_False()
    {
        var cards = new List<Card> { C(10), C(10), C(1) };
        Assert.False(BlackjackHelper.IsBusted(cards));
    }

    [Fact]
    public void IsBusted_Under21_False()
    {
        var cards = new List<Card> { C(5), C(6) };
        Assert.False(BlackjackHelper.IsBusted(cards));
    }

    [Fact]
    public void IsSoft17_AceAnd6_True()
    {
        var cards = new List<Card> { C(1), C(6) };
        Assert.True(BlackjackHelper.IsSoft17(cards));
    }

    [Fact]
    public void IsSoft17_TenAnd7_Hard17_False()
    {
        var cards = new List<Card> { C(10), C(7) };
        Assert.False(BlackjackHelper.IsSoft17(cards));
    }

    [Fact]
    public void IsSoft17_Not17_False()
    {
        var cards = new List<Card> { C(1), C(5) };
        Assert.False(BlackjackHelper.IsSoft17(cards));
    }
}
