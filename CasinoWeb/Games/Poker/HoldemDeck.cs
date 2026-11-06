using System.Security.Cryptography;
using DiscordBot.Domain;

namespace CasinoWeb.Games.Poker;

public static class HoldemDeck
{
    /// <summary>
    /// A shuffled 52-card deck, top card first. Players compete against each other, so the order comes from a
    /// cryptographic generator and cannot be predicted from earlier hands.
    /// </summary>
    public static IReadOnlyList<Card> Shuffled()
    {
        var cards = new List<Card>(52);
        foreach (var suit in new[] { CardSuit.Hearts, CardSuit.Diamonds, CardSuit.Clubs, CardSuit.Spades })
        {
            for (var value = 1; value <= 13; value++)
            {
                cards.Add(new Card(value, suit));
            }
        }

        for (var i = cards.Count - 1; i > 0; i--)
        {
            var k = RandomNumberGenerator.GetInt32(i + 1);
            (cards[i], cards[k]) = (cards[k], cards[i]);
        }

        return cards;
    }
}
