using CasinoWeb.Games.Poker;
using static CasinoWeb.Tests.Poker.HoldemTestKit;

namespace CasinoWeb.Tests.Poker;

public sealed class HoldemHandTests
{
    private const HoldemActionKind Fold = HoldemActionKind.Fold;
    private const HoldemActionKind Check = HoldemActionKind.Check;
    private const HoldemActionKind Call = HoldemActionKind.Call;
    private const HoldemActionKind Raise = HoldemActionKind.Raise;
    private const HoldemActionKind Bet = HoldemActionKind.Bet;
    private const HoldemActionKind AllIn = HoldemActionKind.AllIn;

    [Fact]
    public void Three_handed_blinds_sit_left_of_the_button_and_the_button_acts_first()
    {
        var hand = NewHand(0, [1000, 1000, 1000]);

        Assert.Equal(1, hand.SmallBlindSeat);
        Assert.Equal(2, hand.BigBlindSeat);
        Assert.Equal(Small, hand.PlayerAt(1)!.Committed);
        Assert.Equal(Big, hand.PlayerAt(2)!.Committed);
        Assert.Equal(0, hand.PlayerAt(0)!.Committed);
        Assert.Equal(0, hand.CurrentSeat);
        Assert.Equal(15, hand.TotalPot);
        Assert.Equal(HoldemStreet.Preflop, hand.Street);
    }

    [Fact]
    public void Blinds_follow_a_button_that_moved_and_skip_empty_seats()
    {
        var seats = new[] { new HoldemSeat(1, 1000), new HoldemSeat(3, 1000), new HoldemSeat(4, 1000) };
        var hand = new HoldemHand(seats, 4, Small, Big, Deck(0, 3, null, "2h 7s 9h Js 3d"));

        Assert.Equal(1, hand.SmallBlindSeat);
        Assert.Equal(3, hand.BigBlindSeat);
        Assert.Equal(4, hand.CurrentSeat);
    }

    [Fact]
    public void Hole_cards_are_dealt_one_at_a_time_starting_left_of_the_button()
    {
        var hand = NewHand(1, [1000, 1000, 1000], ["Ac Ad", "Kc Kd", "Qc Qd"]);

        Assert.Equal(Cards("Ac Ad"), hand.PlayerAt(0)!.HoleCards);
        Assert.Equal(Cards("Kc Kd"), hand.PlayerAt(1)!.HoleCards);
        Assert.Equal(Cards("Qc Qd"), hand.PlayerAt(2)!.HoleCards);
    }

    [Fact]
    public void Heads_up_the_button_posts_the_small_blind_and_acts_first_before_the_flop_but_last_after_it()
    {
        var hand = NewHand(0, [1000, 1000]);

        Assert.Equal(0, hand.SmallBlindSeat);
        Assert.Equal(1, hand.BigBlindSeat);
        Assert.Equal(0, hand.CurrentSeat);

        hand.Do(Call);
        Assert.Equal(1, hand.CurrentSeat);
        hand.Do(Check);

        Assert.Equal(HoldemStreet.Flop, hand.Street);
        Assert.Equal(3, hand.Board.Count);
        Assert.Equal(1, hand.CurrentSeat);
    }

    [Fact]
    public void A_full_round_of_calls_gives_the_big_blind_the_option_then_deals_every_street()
    {
        var hand = NewHand(0, [1000, 1000, 1000]);

        hand.Do(Call);
        hand.Do(Call);
        Assert.Equal(HoldemStreet.Preflop, hand.Street);
        Assert.Equal(2, hand.CurrentSeat);
        Assert.Contains(Check, hand.Options!.Kinds);

        hand.Do(Check);
        Assert.Equal(HoldemStreet.Flop, hand.Street);
        Assert.Equal(1, hand.CurrentSeat);
        Assert.Equal(30, hand.TotalPot);

        hand.Do(Check);
        hand.Do(Check);
        hand.Do(Check);
        Assert.Equal(HoldemStreet.Turn, hand.Street);
        Assert.Equal(4, hand.Board.Count);

        hand.Do(Check);
        hand.Do(Check);
        hand.Do(Check);
        Assert.Equal(HoldemStreet.River, hand.Street);
        Assert.Equal(5, hand.Board.Count);

        hand.Do(Check);
        hand.Do(Check);
        hand.Do(Check);
        Assert.True(hand.IsComplete);
        Assert.Equal(HoldemStreet.Showdown, hand.Street);
        Assert.Null(hand.CurrentSeat);
    }

    [Fact]
    public void A_raise_must_be_at_least_the_previous_raise_and_a_refusal_lists_the_options()
    {
        var hand = NewHand(0, [1000, 1000, 1000]);

        var tooLow = hand.Do(Raise, 15);
        Assert.Equal(HoldemRefusal.AmountTooLow, tooLow.Refusal);
        Assert.Equal(20, tooLow.Options!.MinRaiseTo);
        Assert.Equal(1000, tooLow.Options.MaxRaiseTo);
        Assert.Equal(new[] { Fold, Call, Raise, AllIn }, tooLow.Options.Kinds);

        Assert.Equal(HoldemRefusal.AmountTooHigh, hand.Do(Raise, 1001).Refusal);
        Assert.True(hand.Do(Raise, 40).Accepted);

        // The raise was 30 (10 to 40), so the next one must be at least 30 more.
        Assert.Equal(70, hand.Options!.MinRaiseTo);
        Assert.Equal(HoldemRefusal.AmountTooLow, hand.Do(Raise, 60).Refusal);
        Assert.True(hand.Do(Raise, 70).Accepted);
        Assert.Equal(100, hand.Options!.MinRaiseTo);
    }

    [Fact]
    public void A_bet_after_the_flop_is_at_least_the_big_blind()
    {
        var hand = NewHand(0, [1000, 1000]);
        hand.Do(Call);
        hand.Do(Check);

        Assert.DoesNotContain(Raise, hand.Options!.Kinds);
        Assert.Contains(Bet, hand.Options.Kinds);
        Assert.Equal(HoldemRefusal.AmountTooLow, hand.Do(Bet, 9).Refusal);
        Assert.True(hand.Do(Bet, 10).Accepted);
        Assert.Equal(10, hand.CurrentBet);
        Assert.Equal(20, hand.Options!.MinRaiseTo);
    }

    [Fact]
    public void An_action_that_does_not_fit_the_situation_is_refused()
    {
        var hand = NewHand(0, [1000, 1000, 1000]);

        Assert.Equal(HoldemRefusal.NotYourTurn, hand.Act(1, Call).Refusal);
        Assert.Equal(HoldemRefusal.NotAllowed, hand.Do(Check).Refusal);
        Assert.Equal(HoldemRefusal.NotAllowed, hand.Do(Bet, 50).Refusal);
        Assert.Equal(0, hand.CurrentSeat);

        hand.Do(Fold);
        hand.Do(Fold);
        Assert.Equal(HoldemRefusal.HandOver, hand.Act(2, Call).Refusal);
    }

    [Fact]
    public void An_all_in_for_less_than_a_raise_does_not_reopen_the_betting()
    {
        var hand = NewHand(0, [1000, 150, 1000]);
        hand.Do(Raise, 100);

        // Seat 1 goes all in for 150: 50 more, less than the 90 raise.
        hand.Do(AllIn);
        Assert.Equal(150, hand.CurrentBet);
        Assert.Equal(2, hand.CurrentSeat);
        hand.Do(Fold);

        Assert.Equal(0, hand.CurrentSeat);
        Assert.Equal(new[] { Fold, Call }, hand.Options!.Kinds);
        Assert.Equal(50, hand.Options.CallAmount);
        Assert.Equal(HoldemRefusal.NotAllowed, hand.Do(Raise, 400).Refusal);
        Assert.Equal(HoldemRefusal.NotAllowed, hand.Do(AllIn).Refusal);
    }

    [Fact]
    public void An_all_in_for_less_still_lets_a_player_who_has_not_acted_raise()
    {
        var hand = NewHand(0, [1000, 150, 1000]);
        hand.Do(Raise, 100);
        hand.Do(AllIn);

        Assert.Equal(2, hand.CurrentSeat);
        Assert.Equal(new[] { Fold, Call, Raise, AllIn }, hand.Options!.Kinds);
        Assert.Equal(240, hand.Options.MinRaiseTo);
    }

    [Fact]
    public void An_all_in_that_is_a_full_raise_reopens_the_betting()
    {
        var hand = NewHand(0, [1000, 250, 1000]);
        hand.Do(Raise, 100);
        hand.Do(AllIn);
        hand.Do(Fold);

        Assert.Equal(0, hand.CurrentSeat);
        Assert.Equal(new[] { Fold, Call, Raise, AllIn }, hand.Options!.Kinds);
        Assert.Equal(400, hand.Options.MinRaiseTo);
    }

    [Fact]
    public void When_everyone_else_is_all_in_the_board_runs_out_without_more_betting()
    {
        var hand = NewHand(0, [1000, 150, 1000], ["Ac Ad", "Kc Kd", "Qc Qd"]);
        hand.Do(Raise, 100);
        hand.Do(AllIn);
        hand.Do(Fold);
        hand.Do(Call);

        Assert.True(hand.IsComplete);
        Assert.Equal(5, hand.Board.Count);
        Assert.True(hand.Result!.Showdown);
        Assert.Equal(310, hand.Result.Winnings[0]);
    }

    [Fact]
    public void Side_pots_with_three_all_ins_go_to_the_best_hand_each_can_win()
    {
        // AA, KK and QQ on a dry board: seat 0 wins the main pot, seat 1 the side pot, seat 2 gets its excess back.
        var hand = NewHand(0, [100, 300, 600], ["As Ah", "Ks Kh", "Qs Qh"], "2c 7d 9c 3s 4d");
        hand.Do(AllIn);
        hand.Do(AllIn);
        hand.Do(AllIn);

        Assert.True(hand.IsComplete);
        var result = hand.Result!;
        Assert.Equal(new[] { 300L, 400L, 300L }, result.Pots.Select(p => p.Amount));
        Assert.Equal(new[] { 0, 1, 2 }, result.Pots.Select(p => p.WinnerSeats.Single()));
        Assert.Equal(300, result.Winnings[0]);
        Assert.Equal(400, result.Winnings[1]);
        Assert.Equal(300, result.Winnings[2]);
        Assert.Equal(3, result.Shown.Count);
    }

    [Fact]
    public void Pots_list_the_players_who_can_win_each_one()
    {
        var hand = NewHand(0, [100, 300, 600]);
        hand.Do(AllIn);
        hand.Do(AllIn);
        hand.Do(AllIn);

        var pots = hand.Pots;
        Assert.Equal(3, pots.Count);
        Assert.Equal(new[] { 0, 1, 2 }, pots[0].EligibleSeats);
        Assert.Equal(new[] { 1, 2 }, pots[1].EligibleSeats);
        Assert.Equal(new[] { 2 }, pots[2].EligibleSeats);
    }

    [Fact]
    public void A_folded_players_tokens_stay_in_the_pot_they_reached()
    {
        var hand = NewHand(0, [1000, 1000, 1000], ["Ac Ad", "Kc Kd", "Qc Qd"]);
        hand.Do(Raise, 50);
        hand.Do(Fold);
        hand.Do(Call);

        Assert.Equal(HoldemStreet.Flop, hand.Street);
        var pot = Assert.Single(hand.Pots);
        Assert.Equal(105, pot.Amount);
        Assert.Equal(new[] { 0, 2 }, pot.EligibleSeats);
    }

    [Fact]
    public void A_tied_pot_is_split_and_the_odd_token_goes_to_the_first_winner_left_of_the_button()
    {
        // Seat 0 calls 2, the small blind folds (1), the big blind checks: a pot of 5 and both play the royal flush on the board.
        var seats = new[] { new HoldemSeat(0, 100), new HoldemSeat(1, 100), new HoldemSeat(2, 100) };
        var deck = Deck(0, 3, ["2c 3d", "6c 7d", "4c 5d"], "Ah Kh Qh Jh Th");
        var hand = new HoldemHand(seats, 0, 1, 2, deck);
        hand.Do(Call);
        hand.Do(Fold);
        hand.CheckDown();

        var result = hand.Result!;
        Assert.True(result.Showdown);
        Assert.Equal(2, result.Winnings.Count(w => w.Value > 0));
        Assert.Equal(3, result.Winnings[2]);
        Assert.Equal(2, result.Winnings[0]);
        Assert.Equal(0, result.Winnings[1]);
    }

    [Fact]
    public void When_everyone_else_folds_the_last_player_wins_without_showing_cards()
    {
        var hand = NewHand(0, [1000, 1000, 1000]);
        hand.Do(Fold);
        hand.Do(Fold);

        Assert.True(hand.IsComplete);
        var result = hand.Result!;
        Assert.False(result.Showdown);
        Assert.Empty(result.Shown);
        Assert.Equal(15, result.Winnings[2]);
        Assert.Equal(0, result.Winnings[0]);
        Assert.Empty(hand.Board);
    }

    [Fact]
    public void The_better_kicker_wins_at_showdown()
    {
        var hand = NewHand(0, [1000, 1000], ["Ks Qd", "Kd Jc"], "As 7d 7c 2h 3d");
        hand.CheckDown();

        var result = hand.Result!;
        Assert.Equal(20, result.Winnings[0]);
        Assert.Equal(0, result.Winnings[1]);
        Assert.Equal("Pair of 7s", result.Shown.Single(s => s.Seat == 0).Description);
    }

    [Fact]
    public void Forfeit_out_of_turn_folds_the_player_and_the_hand_goes_on()
    {
        var hand = NewHand(0, [1000, 1000, 1000]);

        Assert.True(hand.Forfeit(1));
        Assert.True(hand.PlayerAt(1)!.Folded);
        Assert.Equal(0, hand.CurrentSeat);
        Assert.False(hand.Forfeit(1));

        hand.Do(Call);
        Assert.Equal(2, hand.CurrentSeat);
        Assert.Equal(Small, hand.PlayerAt(1)!.Committed);
    }

    [Fact]
    public void Forfeit_leaving_one_player_ends_the_hand_and_credits_everything_committed_to_them()
    {
        var hand = NewHand(0, [1000, 1000, 1000]);
        hand.Do(Fold);

        Assert.True(hand.Forfeit(1));

        Assert.True(hand.IsComplete);
        Assert.Equal(15, hand.Result!.Winnings[2]);
        Assert.False(hand.Forfeit(2));
        Assert.Equal(HoldemRefusal.HandOver, hand.Act(2, Check).Refusal);
    }

    [Fact]
    public void Forfeit_of_the_player_to_act_passes_the_turn()
    {
        var hand = NewHand(0, [1000, 1000, 1000]);

        Assert.True(hand.Forfeit(0));

        Assert.Equal(1, hand.CurrentSeat);
    }

    [Fact]
    public void Forfeit_can_end_a_betting_round_by_removing_the_last_player_who_had_to_act()
    {
        var hand = NewHand(0, [1000, 1000, 1000]);
        hand.Do(Call);
        hand.Do(Call);

        Assert.True(hand.Forfeit(2));

        Assert.Equal(HoldemStreet.Flop, hand.Street);
        Assert.Equal(1, hand.CurrentSeat);
    }

    [Fact]
    public void CapStake_lowers_what_the_player_can_still_put_in_and_changes_their_options()
    {
        var hand = NewHand(0, [1000, 1000]);
        Assert.Equal(1000, hand.Options!.MaxRaiseTo);

        hand.CapStake(0, 50);

        Assert.Equal(0, hand.CurrentSeat);
        Assert.Equal(45, hand.PlayerAt(0)!.Remaining);
        Assert.Equal(5, hand.Options!.CallAmount);
        Assert.Equal(50, hand.Options.MaxRaiseTo);
        Assert.Equal(HoldemRefusal.AmountTooHigh, hand.Do(Raise, 60).Refusal);
        Assert.True(hand.Do(Raise, 50).Accepted);
        Assert.True(hand.PlayerAt(0)!.IsAllIn);
    }

    [Fact]
    public void CapStake_to_what_is_already_committed_makes_the_player_all_in_and_the_hand_moves_on()
    {
        var hand = NewHand(0, [1000, 1000, 1000]);

        // Seat 0 committed nothing, so the cap equals its commitment: all in for 0, out of the action.
        hand.CapStake(0, hand.PlayerAt(0)!.Committed);

        Assert.True(hand.PlayerAt(0)!.IsAllIn);
        Assert.Equal(1, hand.CurrentSeat);
    }

    [Fact]
    public void CapStake_to_less_than_the_big_blind_gives_the_other_player_the_uncalled_part_back()
    {
        var hand = NewHand(0, [1000, 1000]);

        hand.CapStake(0, Small);

        Assert.True(hand.IsComplete);
        var result = hand.Result!;
        Assert.Equal(10, result.Winnings[0]);
        Assert.Equal(5, result.Winnings[1]);
    }

    [Fact]
    public void CapStake_refuses_raising_a_stake_or_going_below_what_is_committed()
    {
        var hand = NewHand(0, [1000, 1000]);

        Assert.Throws<ArgumentOutOfRangeException>(() => hand.CapStake(0, 2000));
        Assert.Throws<ArgumentOutOfRangeException>(() => hand.CapStake(0, 1));
        Assert.Throws<ArgumentException>(() => hand.CapStake(7, 10));
    }

    [Fact]
    public void A_short_stack_posts_a_blind_for_less_and_the_others_still_face_the_full_big_blind()
    {
        var hand = NewHand(0, [1000, 1000, 6]);

        Assert.Equal(6, hand.PlayerAt(2)!.Committed);
        Assert.True(hand.PlayerAt(2)!.IsAllIn);
        Assert.Equal(10, hand.CurrentBet);
        Assert.Equal(10, hand.Options!.CallAmount);
    }

    [Fact]
    public void Bad_arguments_are_refused_when_the_hand_is_created()
    {
        var deck = Deck(0, 2, null, "2h 7s 9h Js 3d");
        var two = new[] { new HoldemSeat(0, 100), new HoldemSeat(1, 100) };

        Assert.Throws<ArgumentException>(() => new HoldemHand([two[0]], 0, Small, Big, deck));
        Assert.Throws<ArgumentException>(() => new HoldemHand([two[0], two[0]], 0, Small, Big, deck));
        Assert.Throws<ArgumentException>(() => new HoldemHand(two, 5, Small, Big, deck));
        Assert.Throws<ArgumentException>(() => new HoldemHand(two, 0, Big, Small, deck));
        Assert.Throws<ArgumentException>(() => new HoldemHand(two, 0, Small, Big, deck.Take(8).ToList()));
        Assert.Throws<ArgumentException>(() => new HoldemHand([two[0], new HoldemSeat(1, 0)], 0, Small, Big, deck));
    }

    [Fact]
    public void A_shuffled_deck_holds_the_52_cards_once()
    {
        var deck = HoldemDeck.Shuffled();

        Assert.Equal(52, deck.Count);
        Assert.Equal(52, deck.Distinct().Count());
        Assert.NotEqual(deck, HoldemDeck.Shuffled());
    }

    [Fact]
    public void Random_play_always_pays_out_exactly_what_was_committed()
    {
        var random = new Random(412);
        for (var game = 0; game < 400; game++)
        {
            var count = random.Next(2, 7);
            var stakes = Enumerable.Range(0, count).Select(_ => (long)random.Next(20, 1500)).ToArray();
            var seats = stakes.Select((stake, seat) => new HoldemSeat(seat, stake)).ToList();
            var hand = new HoldemHand(seats, random.Next(count), Small, Big, HoldemDeck.Shuffled());

            var steps = 0;
            while (!hand.IsComplete)
            {
                Assert.True(++steps < 500, "The hand never ended");
                PlayRandomAction(hand, random);
            }

            var result = hand.Result!;
            Assert.Equal(hand.TotalPot, result.Winnings.Values.Sum());
            Assert.All(hand.Players, p => Assert.InRange(p.Committed, 0, p.Stake));
            Assert.All(result.Winnings.Values, w => Assert.True(w >= 0));
        }
    }

    private static void PlayRandomAction(HoldemHand hand, Random random)
    {
        var options = hand.Options!;
        var roll = random.Next(10);
        if (random.Next(25) == 0)
        {
            hand.Forfeit(hand.Players[random.Next(hand.Players.Count)].Seat);
            return;
        }

        if (roll < 2)
        {
            Assert.True(hand.Do(Fold).Accepted);
        }
        else if (roll < 4 && options.MinRaiseTo > 0)
        {
            var kind = options.Kinds.Contains(Bet) ? Bet : Raise;
            Assert.True(hand.Do(kind, random.NextInt64(options.MinRaiseTo, options.MaxRaiseTo + 1)).Accepted);
        }
        else if (roll == 4 && options.Kinds.Contains(AllIn))
        {
            Assert.True(hand.Do(AllIn).Accepted);
        }
        else
        {
            Assert.True(hand.Do(options.Kinds.Contains(Check) ? Check : Call).Accepted);
        }
    }
}
