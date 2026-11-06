using DiscordBot.Domain;

namespace CasinoWeb.Games.Poker;

/// <summary>
/// The rules of one no-limit Texas Hold'em hand. It knows nothing of tokens, balances or time: the caller gives it
/// each seat's stake, plays the actions in and credits <see cref="Result"/> at the end.
/// </summary>
/// <remarks>
/// The deck is dealt from its first card, with no burn cards: one hole card at a time around the table starting left
/// of the button, twice, then the flop (3), the turn (1) and the river (1). Seats act clockwise, in seat-number order.
/// Not modeled: several short all-ins adding up to a full raise do not reopen the betting.
/// </remarks>
public sealed class HoldemHand
{
    private readonly List<HoldemPlayer> _players;
    private readonly List<Card> _board = [];
    private readonly IReadOnlyList<Card> _deck;
    private readonly int _buttonIndex;
    private readonly long _bigBlind;
    private int _deckPosition;
    private int _cursor;
    private HoldemPlayer? _current;

    public HoldemHand(IReadOnlyList<HoldemSeat> seats, int button, long smallBlind, long bigBlind, IReadOnlyList<Card> deck)
    {
        if (seats.Count < 2)
        {
            throw new ArgumentException("A hand needs at least 2 players", nameof(seats));
        }

        if (seats.Select(s => s.Seat).Distinct().Count() != seats.Count)
        {
            throw new ArgumentException("Seats must be unique", nameof(seats));
        }

        if (seats.Any(s => s.Stake <= 0))
        {
            throw new ArgumentException("Every stake must be positive", nameof(seats));
        }

        if (smallBlind <= 0 || bigBlind < smallBlind)
        {
            throw new ArgumentException("Blinds must be positive and the big blind at least the small blind");
        }

        if (deck.Count < seats.Count * 2 + 5)
        {
            throw new ArgumentException("The deck is too short for this many players", nameof(deck));
        }

        _players = [.. seats.OrderBy(s => s.Seat).Select(s => new HoldemPlayer(s.Seat, s.Stake))];
        _buttonIndex = _players.FindIndex(p => p.Seat == button);
        if (_buttonIndex < 0)
        {
            throw new ArgumentException("The button must sit on a dealt seat", nameof(button));
        }

        _deck = deck;
        _bigBlind = bigBlind;
        Button = button;
        MinRaise = bigBlind;
        CurrentBet = bigBlind;

        DealHoleCards();

        var headsUp = _players.Count == 2;
        var smallIndex = headsUp ? _buttonIndex : Next(_buttonIndex);
        var bigIndex = Next(smallIndex);
        SmallBlindSeat = _players[smallIndex].Seat;
        BigBlindSeat = _players[bigIndex].Seat;
        PutIn(_players[smallIndex], smallBlind);
        PutIn(_players[bigIndex], bigBlind);

        _cursor = bigIndex;
        Advance();
    }

    public int Button { get; }

    public int SmallBlindSeat { get; }

    public int BigBlindSeat { get; }

    public HoldemStreet Street { get; private set; } = HoldemStreet.Preflop;

    public IReadOnlyList<Card> Board => _board;

    public IReadOnlyList<HoldemPlayer> Players => _players;

    /// <summary>The bet to match in the current street.</summary>
    public long CurrentBet { get; private set; }

    /// <summary>The smallest increase a raise can make on the current bet.</summary>
    public long MinRaise { get; private set; }

    public bool IsComplete => Result is not null;

    /// <summary>Set once the hand is over.</summary>
    public HoldemResult? Result { get; private set; }

    /// <summary>The seat that must act, or null when the hand is over.</summary>
    public int? CurrentSeat => _current?.Seat;

    public long TotalPot => _players.Sum(p => p.Committed);

    /// <summary>
    /// The pots as they stand: the main pot first, then each side pot. A bet nobody has matched yet (or never
    /// can) is a pot only its owner can win, so it comes back to them.
    /// </summary>
    public IReadOnlyList<HoldemPot> Pots => BuildPots();

    /// <summary>What the player to act may do; null when the hand is over.</summary>
    public HoldemOptions? Options => _current is null ? null : OptionsFor(_current);

    public HoldemPlayer? PlayerAt(int seat) => _players.Find(p => p.Seat == seat);

    public HoldemActResult Act(int seat, HoldemActionKind kind, long amount = 0)
    {
        if (IsComplete)
        {
            return new HoldemActResult(HoldemRefusal.HandOver, null);
        }

        if (_current is null || _current.Seat != seat)
        {
            return Refuse(HoldemRefusal.NotYourTurn);
        }

        var player = _current;
        var options = OptionsFor(player);
        if (!options.Kinds.Contains(kind))
        {
            return Refuse(HoldemRefusal.NotAllowed);
        }

        switch (kind)
        {
            case HoldemActionKind.Fold:
                player.Folded = true;
                player.HasActed = true;
                break;
            case HoldemActionKind.Check:
                player.HasActed = true;
                break;
            case HoldemActionKind.Call:
                PutIn(player, options.CallAmount);
                player.HasActed = true;
                break;
            case HoldemActionKind.AllIn:
                PutIn(player, player.Remaining);
                player.HasActed = true;
                break;
            case HoldemActionKind.Bet:
            case HoldemActionKind.Raise:
                if (amount > options.MaxRaiseTo)
                {
                    return Refuse(HoldemRefusal.AmountTooHigh);
                }

                if (amount < options.MinRaiseTo)
                {
                    return Refuse(HoldemRefusal.AmountTooLow);
                }

                PutIn(player, amount - player.RoundCommitted);
                player.HasActed = true;
                break;
        }

        _cursor = _players.IndexOf(player);
        Advance();
        return new HoldemActResult(HoldemRefusal.None, Options);
    }

    /// <summary>
    /// A player leaves: their hand folds even out of turn, and whatever they put in stays in the pots.
    /// Returns false when the hand is over or the player already folded.
    /// </summary>
    public bool Forfeit(int seat)
    {
        var player = PlayerAt(seat);
        if (IsComplete || player is null || player.Folded)
        {
            return false;
        }

        player.Folded = true;
        player.HasActed = true;
        Advance();
        return true;
    }

    /// <summary>
    /// Lowers a player's stake to what their balance can still cover (<paramref name="newStake"/> counts what they
    /// already committed). A player left with nothing to put in is all in for what they committed. Read
    /// <see cref="Options"/> afterwards for the new choices.
    /// </summary>
    public void CapStake(int seat, long newStake)
    {
        var player = PlayerAt(seat) ?? throw new ArgumentException("No such seat in this hand", nameof(seat));
        if (newStake < player.Committed || newStake > player.Stake)
        {
            throw new ArgumentOutOfRangeException(nameof(newStake), "A stake can only be lowered, and not below what is committed");
        }

        if (IsComplete || newStake == player.Stake)
        {
            return;
        }

        player.Stake = newStake;
        Advance();
    }

    private HoldemActResult Refuse(HoldemRefusal refusal) => new(refusal, Options);

    private int Next(int index) => (index + 1) % _players.Count;

    private void DealHoleCards()
    {
        var cards = _players.Select(_ => new List<Card>(2)).ToList();
        for (var round = 0; round < 2; round++)
        {
            for (var i = 1; i <= _players.Count; i++)
            {
                cards[(_buttonIndex + i) % _players.Count].Add(_deck[_deckPosition++]);
            }
        }

        for (var i = 0; i < _players.Count; i++)
        {
            _players[i].HoleCards = cards[i];
        }
    }

    private int NonFoldedCount => _players.Count(p => !p.Folded);

    private int ActiveCount => _players.Count(p => !p.Folded && p.Remaining > 0);

    private bool NeedsToAct(HoldemPlayer player, int activeCount) =>
        !player.Folded && player.Remaining > 0
        && (player.RoundCommitted < CurrentBet || (!player.HasActed && activeCount >= 2));

    private HoldemOptions OptionsFor(HoldemPlayer player)
    {
        var toCall = Math.Max(0, CurrentBet - player.RoundCommitted);
        var callAmount = Math.Min(toCall, player.Remaining);
        var maxTo = player.RoundCommitted + player.Remaining;
        var minTo = CurrentBet + MinRaise;

        // After a short all-in a player who already acted may only call or fold.
        var canRaise = !player.HasActed;

        var kinds = new List<HoldemActionKind> { HoldemActionKind.Fold };
        kinds.Add(toCall == 0 ? HoldemActionKind.Check : HoldemActionKind.Call);
        var canBetOrRaise = canRaise && maxTo >= minTo;
        if (canBetOrRaise)
        {
            kinds.Add(CurrentBet == 0 ? HoldemActionKind.Bet : HoldemActionKind.Raise);
        }

        if (player.Remaining > 0 && (canRaise || maxTo <= CurrentBet))
        {
            kinds.Add(HoldemActionKind.AllIn);
        }

        return new HoldemOptions(player.Seat, kinds, callAmount, canBetOrRaise ? minTo : 0, maxTo, maxTo);
    }

    /// <summary>Moves tokens from a player's stake into the street's bet and updates the bet to match.</summary>
    private void PutIn(HoldemPlayer player, long amount)
    {
        amount = Math.Min(amount, player.Remaining);
        player.Committed += amount;
        player.RoundCommitted += amount;
        if (player.RoundCommitted <= CurrentBet)
        {
            return;
        }

        var increase = player.RoundCommitted - CurrentBet;
        CurrentBet = player.RoundCommitted;
        if (increase < MinRaise)
        {
            return;
        }

        // A full raise gives everyone who already acted another turn.
        MinRaise = increase;
        foreach (var other in _players)
        {
            if (other != player)
            {
                other.HasActed = false;
            }
        }
    }

    /// <summary>Finds who acts next, and moves on to the next street or the end of the hand when nobody does.</summary>
    private void Advance()
    {
        _current = null;
        while (!IsComplete)
        {
            if (NonFoldedCount == 1)
            {
                FinishWithoutShowdown();
                return;
            }

            var active = ActiveCount;
            for (var step = 1; step <= _players.Count; step++)
            {
                var candidate = _players[(_cursor + step) % _players.Count];
                if (NeedsToAct(candidate, active))
                {
                    _current = candidate;
                    return;
                }
            }

            if (Street == HoldemStreet.River)
            {
                FinishAtShowdown();
                return;
            }

            NextStreet();
        }
    }

    private void NextStreet()
    {
        foreach (var player in _players)
        {
            player.RoundCommitted = 0;
            player.HasActed = false;
        }

        CurrentBet = 0;
        MinRaise = _bigBlind;
        Street++;
        var cards = Street == HoldemStreet.Flop ? 3 : 1;
        for (var i = 0; i < cards; i++)
        {
            _board.Add(_deck[_deckPosition++]);
        }

        _cursor = _buttonIndex;
    }

    private List<HoldemPot> BuildPots()
    {
        var levels = _players.Where(p => !p.Folded).Select(p => p.Committed).Distinct().Order().ToList();
        var pots = new List<HoldemPot>();
        long previous = 0;
        for (var i = 0; i < levels.Count; i++)
        {
            var level = levels[i];
            var isTop = i == levels.Count - 1;

            // Folded players' tokens fill the pots they reach; the top pot also takes any excess they have.
            var cap = isTop ? long.MaxValue : level;
            var amount = _players.Sum(p => Math.Min(p.Committed, cap) - Math.Min(p.Committed, previous));
            var eligible = _players.Where(p => !p.Folded && p.Committed >= level).Select(p => p.Seat).ToList();
            previous = level;
            if (amount <= 0)
            {
                continue;
            }

            if (pots.Count > 0 && pots[^1].EligibleSeats.SequenceEqual(eligible))
            {
                pots[^1] = pots[^1] with { Amount = pots[^1].Amount + amount };
            }
            else
            {
                pots.Add(new HoldemPot(amount, eligible));
            }
        }

        return pots;
    }

    private void FinishWithoutShowdown()
    {
        var winner = _players.First(p => !p.Folded);
        var winnings = _players.ToDictionary(p => p.Seat, _ => 0L);
        winnings[winner.Seat] = TotalPot;
        Result = new HoldemResult(false, [new HoldemPotResult(TotalPot, [winner.Seat])], winnings, []);
        _current = null;
    }

    private void FinishAtShowdown()
    {
        Street = HoldemStreet.Showdown;
        var contenders = _players.Where(p => !p.Folded).ToList();
        var hands = contenders.ToDictionary(p => p.Seat, p => HoldemRanking.Best([.. p.HoleCards, .. _board]));
        var winnings = _players.ToDictionary(p => p.Seat, _ => 0L);
        var potResults = new List<HoldemPotResult>();

        foreach (var pot in BuildPots())
        {
            var best = pot.EligibleSeats.Select(s => hands[s]).MaxBy(h => h, new PokerHandComparer())!;
            var winners = LeftOfButton(pot.EligibleSeats.Where(s => PokerHelper.CompareHands(hands[s], best) == 0));

            // Every winner gets an equal share; the odd tokens go one each to the first winners left of the button.
            var share = pot.Amount / winners.Count;
            var odd = (int)(pot.Amount % winners.Count);
            for (var i = 0; i < winners.Count; i++)
            {
                winnings[winners[i]] += share + (i < odd ? 1 : 0);
            }

            potResults.Add(new HoldemPotResult(pot.Amount, winners));
        }

        var shown = contenders
            .Select(p => new HoldemShownHand(p.Seat, p.HoleCards, hands[p.Seat].Cards, hands[p.Seat].Rank, hands[p.Seat].Description))
            .ToList();
        Result = new HoldemResult(true, potResults, winnings, shown);
        _current = null;
    }

    private List<int> LeftOfButton(IEnumerable<int> seats)
    {
        var set = seats.ToHashSet();
        return [.. Enumerable.Range(1, _players.Count)
            .Select(i => _players[(_buttonIndex + i) % _players.Count].Seat)
            .Where(set.Contains)];
    }
}
