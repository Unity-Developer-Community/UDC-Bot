using CasinoWeb.Games.Poker;
using DiscordBot.Domain;

namespace CasinoWeb.Tables;

/// <summary>No-limit Texas Hold'em at one table: <see cref="HoldemHand"/> plus the dealer button that moves between hands.</summary>
public sealed class PokerTableGame(TableStakes stakes, Func<IReadOnlyList<Card>>? shuffle = null) : ITableGame
{
    private readonly Func<IReadOnlyList<Card>> _shuffle = shuffle ?? HoldemDeck.Shuffled;
    private HoldemHand? _hand;
    private int _button = -1;
    private int _previousButton = -1;

    public string Name => "Texas Hold'em";

    public int MaxSeats => TableRules.Seats;

    public long MinBalance => stakes.BigBlind * TableRules.MinBigBlinds;

    public long MaxStake => stakes.BigBlind * TableRules.MaxBigBlinds;

    public bool IsRunning => _hand is { IsComplete: false };

    public int? SeatToAct => _hand?.CurrentSeat;

    public IReadOnlyDictionary<int, long>? Winnings => _hand?.Result?.Winnings;

    public IReadOnlyDictionary<int, long> Deal(IReadOnlyList<DealtSeat> seats)
    {
        var ordered = seats.OrderBy(s => s.Seat).ToList();
        // The button moves clockwise to the next seat dealt in.
        var button = ordered.Find(s => s.Seat > _button)?.Seat ?? ordered[0].Seat;
        _hand = new HoldemHand([.. ordered.Select(s => new HoldemSeat(s.Seat, s.Stake))], button, stakes.SmallBlind, stakes.BigBlind, _shuffle());
        _previousButton = _button;
        _button = button;
        return _hand.Players.Where(p => p.Committed > 0).ToDictionary(p => p.Seat, p => p.Committed);
    }

    public void Cancel()
    {
        _hand = null;
        _button = _previousButton;
    }

    public ActionQuote Price(int seat, TableAction action)
    {
        if (_hand is not { IsComplete: false } hand)
        {
            return new ActionQuote(TableRefusal.HandOver, 0);
        }

        if (hand.CurrentSeat != seat)
        {
            return new ActionQuote(TableRefusal.NotYourTurn, 0);
        }

        var options = hand.Options!;
        if (Parse(action.Kind) is not { } kind || !options.Kinds.Contains(kind))
        {
            return new ActionQuote(TableRefusal.NotAllowed, 0);
        }

        var player = hand.PlayerAt(seat)!;
        switch (kind)
        {
            case HoldemActionKind.Call:
                return new ActionQuote(TableRefusal.None, options.CallAmount);
            case HoldemActionKind.AllIn:
                return new ActionQuote(TableRefusal.None, player.Remaining);
            case HoldemActionKind.Bet:
            case HoldemActionKind.Raise:
                if (action.Amount > options.MaxRaiseTo)
                {
                    return new ActionQuote(TableRefusal.AmountTooHigh, 0);
                }

                return action.Amount < options.MinRaiseTo
                    ? new ActionQuote(TableRefusal.AmountTooLow, 0)
                    : new ActionQuote(TableRefusal.None, action.Amount - player.RoundCommitted);
            default:
                return new ActionQuote(TableRefusal.None, 0);
        }
    }

    public bool Act(int seat, TableAction action) =>
        _hand is not null && Parse(action.Kind) is { } kind && _hand.Act(seat, kind, action.Amount).Accepted;

    public TableAction TimeoutAction(int seat) =>
        new(_hand!.Options!.Kinds.Contains(HoldemActionKind.Check) ? "check" : "fold");

    public TableAction AiAction(int seat) =>
        new(_hand!.Options!.Kinds.Contains(HoldemActionKind.Check) ? "check" : "call");

    public bool Forfeit(int seat) => _hand?.Forfeit(seat) ?? false;

    public void LimitStake(int seat, long tokensLeft)
    {
        var player = _hand?.PlayerAt(seat);
        if (player is null)
        {
            return;
        }

        var newStake = player.Committed + Math.Max(0, tokensLeft);
        if (newStake < player.Stake)
        {
            _hand!.CapStake(seat, newStake);
        }
    }

    public HandView? View(int? viewerSeat)
    {
        if (_hand is not { } hand)
        {
            return null;
        }

        var result = hand.Result;
        var seats = hand.Players.Select(p =>
        {
            // Other players' cards leave the server only at showdown, and only for hands still in.
            var visible = p.Seat == viewerSeat || (result is { Showdown: true } && !p.Folded);
            return new PokerSeatView(p.Seat, p.Committed, p.RoundCommitted, p.Folded, p.IsAllIn, visible ? p.HoleCards : null);
        }).ToList();
        var options = viewerSeat is { } seat && hand.CurrentSeat == seat ? hand.Options : null;
        return new PokerHandView(hand.Street, [.. hand.Board], hand.TotalPot, hand.Pots, hand.Button, hand.SmallBlindSeat, hand.BigBlindSeat, hand.CurrentBet, seats, options, result);
    }

    private static HoldemActionKind? Parse(string text)
    {
        foreach (var kind in Enum.GetValues<HoldemActionKind>())
        {
            if (string.Equals(kind.ToString(), text, StringComparison.OrdinalIgnoreCase))
            {
                return kind;
            }
        }

        return null;
    }
}
