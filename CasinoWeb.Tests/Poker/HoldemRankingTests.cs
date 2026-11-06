using CasinoWeb.Games.Poker;
using DiscordBot.Domain;
using static CasinoWeb.Tests.Poker.HoldemTestKit;

namespace CasinoWeb.Tests.Poker;

public sealed class HoldemRankingTests
{
    private static PokerHand Best(string cards) => HoldemRanking.Best(Cards(cards));

    private static int Compare(string first, string second) => Math.Sign(PokerHelper.CompareHands(Best(first), Best(second)));

    [Theory]
    [InlineData("As Kd 9c 7h 5s 3d 2c", PokerHandRank.HighCard)]
    [InlineData("As Ad 9c 7h 5s 3d 2c", PokerHandRank.OnePair)]
    [InlineData("As Ad 9c 9h 5s 3d 2c", PokerHandRank.TwoPair)]
    [InlineData("As Ad Ac 7h 5s 3d 2c", PokerHandRank.ThreeOfAKind)]
    [InlineData("6s 7d 8c 9h Ts 3d 2c", PokerHandRank.Straight)]
    [InlineData("As Ks 9s 7s 5s 3d 2c", PokerHandRank.Flush)]
    [InlineData("As Ad Ac 7h 7s 3d 2c", PokerHandRank.FullHouse)]
    [InlineData("As Ad Ac Ah 5s 3d 2c", PokerHandRank.FourOfAKind)]
    [InlineData("5s 6s 7s 8s 9s 3d 2c", PokerHandRank.StraightFlush)]
    [InlineData("As Ks Qs Js Ts 3d 2c", PokerHandRank.RoyalFlush)]
    public void The_best_of_seven_cards_has_the_expected_rank(string cards, PokerHandRank rank)
    {
        Assert.Equal(rank, Best(cards).Rank);
    }

    [Fact]
    public void A_flush_and_a_straight_among_seven_cards_are_each_found_but_not_mixed_up()
    {
        // The heart flush and the 5-9 straight share cards without making a straight flush.
        var hand = Best("5h 6h 7h 8h 9d Kh 2c");

        Assert.Equal(PokerHandRank.Flush, hand.Rank);
    }

    [Fact]
    public void The_wheel_is_a_straight_and_the_lowest_one()
    {
        var wheel = Best("As 2d 3c 4h 5s 9d Kc");

        Assert.Equal(PokerHandRank.Straight, wheel.Rank);
        Assert.Equal(1, Compare("2d 3c 4h 5s 6d Ac Kc", "As 2d 3c 4h 5s 9d Kc"));
        Assert.Equal(-1, Compare("As 2d 3c 4h 5s 9d Kc", "2d 3c 4h 5s 6d 9c Kc"));
    }

    [Fact]
    public void The_wheel_beats_three_of_a_kind_and_loses_to_a_flush()
    {
        Assert.Equal(1, Compare("As 2d 3c 4h 5s 9d Kc", "Kd Kh Ks 4c 7d 2h 9s"));
        Assert.Equal(-1, Compare("As 2d 3c 4h 5s 9d Kc", "Ah Kh 9h 4h 7h 2d 3s"));
    }

    [Fact]
    public void A_steel_wheel_is_a_straight_flush_below_a_six_high_one()
    {
        var wheel = Best("As 2s 3s 4s 5s Kd Kc");

        Assert.Equal(PokerHandRank.StraightFlush, wheel.Rank);
        Assert.Equal(1, Compare("2s 3s 4s 5s 6s Kd Kc", "As 2s 3s 4s 5s Kd Kc"));
    }

    [Fact]
    public void Kickers_decide_between_equal_pairs()
    {
        Assert.Equal(1, Compare("As Ad Kc Qh 5s 3d 2c", "Ah Ac Kd Jh 5d 3s 2s"));
        Assert.Equal(1, Compare("As Ad Kc Qh 5s 3d 2c", "Ah Ac Kd Qh 4d 3s 2s"));
        Assert.Equal(0, Compare("As Ad Kc Qh 5s 3d 2c", "Ah Ac Kd Qd 5d 3s 2s"));
    }

    [Fact]
    public void Two_pair_uses_the_best_two_pairs_and_the_best_kicker_of_seven_cards()
    {
        // Three pairs among seven cards: the lowest pair's card is a kicker candidate against the lone ace.
        var hand = Best("Ks Kd 9c 9h 4s 4d As");

        Assert.Equal(PokerHandRank.TwoPair, hand.Rank);
        Assert.Equal(new[] { 13, 9, 14 }, hand.KickerValues);
    }

    [Fact]
    public void Playing_the_board_ties()
    {
        Assert.Equal(0, Compare("Ah Kh Qh Jh Th 2c 3d", "Ah Kh Qh Jh Th 4c 5d"));
    }

    [Fact]
    public void A_hand_needs_five_cards()
    {
        Assert.Throws<ArgumentException>(() => HoldemRanking.Best(Cards("As Kd 9c 7h")));
    }
}
