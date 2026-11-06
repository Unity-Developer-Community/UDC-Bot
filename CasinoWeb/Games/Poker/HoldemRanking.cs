using DiscordBot.Domain;

namespace CasinoWeb.Games.Poker;

/// <summary>Ranks the best five-card hand among the cards of a Hold'em player, with the bot's five-card evaluator.</summary>
public static class HoldemRanking
{
    public static PokerHand Best(IReadOnlyList<Card> cards)
    {
        if (cards.Count < 5)
        {
            throw new ArgumentException("A hand needs at least 5 cards", nameof(cards));
        }

        PokerHand? best = null;
        var five = new List<Card>(5);
        Search(0, five);
        return best!;

        void Search(int from, List<Card> chosen)
        {
            if (chosen.Count == 5)
            {
                var hand = PokerHelper.EvaluateHand([.. chosen]);
                if (best is null || PokerHelper.CompareHands(hand, best) > 0)
                {
                    best = hand;
                }

                return;
            }

            for (var i = from; i <= cards.Count - (5 - chosen.Count); i++)
            {
                chosen.Add(cards[i]);
                Search(i + 1, chosen);
                chosen.RemoveAt(chosen.Count - 1);
            }
        }
    }
}
