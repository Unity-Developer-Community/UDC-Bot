using CasinoWeb.Games.Poker;
using DiscordBot.Domain;

namespace CasinoWeb.Tests.Poker;

internal static class HoldemTestKit
{
    public const long Small = 5;
    public const long Big = 10;

    private static readonly string[] DefaultHoles = ["Ac Ad", "Kc Kd", "Qc Qd", "8c 8d", "5c 5d", "4c 4d"];

    /// <summary>Parses cards like "As Td 9h": A 2-9 T J Q K, then h d c s.</summary>
    public static List<Card> Cards(string text) =>
        [.. text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(Parse)];

    private static Card Parse(string text)
    {
        var value = text[0] switch
        {
            'A' => 1,
            'T' => 10,
            'J' => 11,
            'Q' => 12,
            'K' => 13,
            var digit => digit - '0',
        };
        var suit = text[1] switch
        {
            'h' => CardSuit.Hearts,
            'd' => CardSuit.Diamonds,
            'c' => CardSuit.Clubs,
            's' => CardSuit.Spades,
            _ => throw new ArgumentException($"Bad suit in {text}"),
        };
        return new Card(value, suit);
    }

    /// <summary>
    /// A deck that deals these hole cards to seats 0..n-1 (<paramref name="holes"/>[i] is seat i) and then this board,
    /// in the order <see cref="HoldemHand"/> deals them.
    /// </summary>
    public static List<Card> Deck(int button, int seatCount, string[]? holes, string board)
    {
        var hole = (holes ?? DefaultHoles).Take(seatCount).Select(Cards).ToArray();
        var deck = new List<Card>();
        for (var round = 0; round < 2; round++)
        {
            for (var i = 1; i <= seatCount; i++)
            {
                deck.Add(hole[(button + i) % seatCount][round]);
            }
        }

        deck.AddRange(Cards(board));
        return deck;
    }

    /// <summary>A hand for seats 0..n-1 with these stakes and the blinds 5/10.</summary>
    public static HoldemHand NewHand(int button, long[] stakes, string[]? holes = null, string board = "2h 7s 9h Js 3d") =>
        new([.. stakes.Select((stake, seat) => new HoldemSeat(seat, stake))], button, Small, Big, Deck(button, stakes.Length, holes, board));

    public static HoldemActResult Do(this HoldemHand hand, HoldemActionKind kind, long amount = 0) =>
        hand.Act(hand.CurrentSeat!.Value, kind, amount);

    /// <summary>Checks, or calls when there is a bet, until the hand ends.</summary>
    public static void CheckDown(this HoldemHand hand)
    {
        while (!hand.IsComplete)
        {
            var kinds = hand.Options!.Kinds;
            hand.Do(kinds.Contains(HoldemActionKind.Check) ? HoldemActionKind.Check : HoldemActionKind.Call);
        }
    }
}
