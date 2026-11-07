using CasinoWeb.Games;
using DiscordBot.Domain;
using DiscordBot.Services.Fun.Casino;

namespace CasinoWeb.Hubs;

/// <summary>What the client needs when it opens a game: the rules to display and the numbers that change.</summary>
public sealed record HubState(bool CasinoEnabled, long Balance, SlotsInfo Slots, BlackjackInfo Blackjack);

/// <param name="MinBet">Smallest accepted bet.</param>
/// <param name="MaxBet">Largest accepted bet.</param>
/// <param name="PotTokens">The shared jackpot in whole tokens.</param>
/// <param name="Reels">The symbols of each reel strip in order; a result's stop is an index into its reel.</param>
/// <param name="Paytable">The winning lines, best first.</param>
public sealed record SlotsInfo(int MinBet, int MaxBet, long PotTokens, IReadOnlyList<IReadOnlyList<string>> Reels, IReadOnlyList<PaytableLine> Paytable)
{
    public static SlotsInfo Create(long potHundredths) => new(
        SlotMachine.MinBet,
        SlotMachine.MaxBet,
        SlotsMessageBuilder.PotTokens(potHundredths),
        SlotMachine.Reels.Select(strip => (IReadOnlyList<string>)strip.Select(SlotsWire.Symbol).ToArray()).ToArray(),
        SlotMachine.Paytable.Select(e => new PaytableLine(SlotsWire.Symbol(e.Symbol), e.Count, e.Multiplier, e.IsJackpot)).ToArray());
}

public sealed record PaytableLine(string Symbol, int Count, int Multiplier, bool IsJackpot);

/// <summary>The result of a spin or respin. Only <see cref="Status"/> is meaningful unless it is <c>ok</c>.</summary>
public sealed record SlotsPlayDto(
    string Status,
    int Bet,
    long Payout,
    long Net,
    long Balance,
    long PotTokens,
    SlotsResultDto? Result,
    SlotsHoldDto? Hold)
{
    public static SlotsPlayDto From(SlotsPlay play) => play.Status != SlotsStatus.Ok || play.Result is not { } result
        ? new SlotsPlayDto(SlotsWire.Status(play.Status), 0, 0, 0, 0, 0, null, null)
        : new SlotsPlayDto(
            SlotsWire.Status(play.Status),
            play.Bet,
            play.Payout,
            play.Net,
            play.Balance,
            SlotsMessageBuilder.PotTokens(play.PotHundredths),
            new SlotsResultDto(result.Stops, result.Symbols.Select(SlotsWire.Symbol).ToArray(), result.Multiplier, result.IsJackpot, result.IsRespin),
            play.Hold is null ? null : SlotsHoldDto.From(play.Hold));
}

public sealed record SlotsResultDto(IReadOnlyList<int> Stops, IReadOnlyList<string> Symbols, int Multiplier, bool IsJackpot, bool IsRespin);

public sealed record SlotsHoldDto(long OfferId, IReadOnlyList<bool> Held)
{
    public static SlotsHoldDto From(SlotsHold hold) => new(hold.OfferId, hold.Held);
}

/// <summary>The lower-case words the client reads, so the wire format does not depend on the C# enum names.</summary>
internal static class SlotsWire
{
    public static string Symbol(SlotSymbol symbol) => symbol.ToString().ToLowerInvariant();

    public static string Status(SlotsStatus status) => status switch
    {
        SlotsStatus.Ok => "ok",
        SlotsStatus.InvalidBet => "invalidBet",
        SlotsStatus.InsufficientTokens => "insufficientTokens",
        SlotsStatus.Busy => "busy",
        SlotsStatus.NoHold => "noHold",
        _ => "failed",
    };
}

/// <param name="Tokens">What one claim pays.</param>
/// <param name="SecondsUntilNext">Whole seconds until the next claim is possible, rounded up; 0 when it is available now. Seconds rather than a time, so the client's own clock cannot skew the countdown.</param>
public sealed record DailyInfo(long Tokens, long SecondsUntilNext)
{
    public static DailyInfo Create(long tokens, DateTime nextRewardTime) =>
        new(tokens, Math.Max(0, (long)Math.Ceiling((nextRewardTime - DateTime.UtcNow).TotalSeconds)));
}

/// <param name="Status"><c>ok</c> when the reward was paid, <c>notYet</c> when the last claim is too recent (from the web or from Discord).</param>
/// <param name="Awarded">Tokens added by this claim; 0 for <c>notYet</c>.</param>
/// <param name="Balance">The balance after the claim.</param>
/// <param name="Daily">The reward amount and the time until the next claim.</param>
public sealed record DailyClaimDto(string Status, long Awarded, long Balance, DailyInfo Daily);

/// <param name="MinBet">Smallest accepted bet; the balance is the only upper limit, as in Discord.</param>
/// <param name="Hand">The player's open hand, so a page closed mid-hand resumes it; <c>null</c> when there is none.</param>
public sealed record BlackjackInfo(long MinBet, BlackjackHandDto? Hand);

/// <summary>The result of a deal or an action. <see cref="Hand"/> is set for <c>ok</c>, and for a refusal that leaves the hand open.</summary>
public sealed record BlackjackPlayDto(string Status, long Balance, BlackjackHandDto? Hand)
{
    public static BlackjackPlayDto From(BlackjackPlay play) =>
        new(BlackjackWire.Status(play.Status), play.Balance, play.Hand is null ? null : BlackjackHandDto.From(play.Hand));
}

/// <param name="Dealer">The dealer's visible cards: only the first one while the player acts, all once the hand is finished.</param>
/// <param name="Result"><c>win</c>, <c>tie</c> or <c>loss</c> once finished, otherwise <c>null</c>.</param>
/// <param name="Credited">Tokens given back to the balance at the end: twice the bet for a win, the bet for a tie, 0 for a loss.</param>
/// <param name="Net">What the hand won or lost, as recorded in the history.</param>
/// <param name="Actions">What the player can do now: <c>hit</c>, <c>stand</c>, <c>doubleDown</c> (only when the balance covers a second bet).</param>
public sealed record BlackjackHandDto(
    string HandId,
    long Bet,
    IReadOnlyList<CardDto> Player,
    int PlayerValue,
    IReadOnlyList<CardDto> Dealer,
    int DealerValue,
    bool Finished,
    string? Result,
    long Credited,
    long Net,
    IReadOnlyList<string> Actions)
{
    public static BlackjackHandDto From(BlackjackHandView hand) => new(
        hand.HandId.ToString(),
        hand.Bet,
        hand.PlayerCards.Select(CardDto.From).ToArray(),
        hand.PlayerValue,
        hand.DealerCards.Select(CardDto.From).ToArray(),
        hand.DealerValue,
        hand.IsFinished,
        hand.IsFinished ? BlackjackWire.Result(hand.Result) : null,
        hand.Credited,
        hand.Net,
        hand.Allowed.Select(BlackjackWire.Action).ToArray());
}

/// <param name="Rank"><c>A</c>, <c>2</c>–<c>10</c>, <c>J</c>, <c>Q</c> or <c>K</c>.</param>
/// <param name="Suit"><c>hearts</c>, <c>diamonds</c>, <c>clubs</c> or <c>spades</c>.</param>
public sealed record CardDto(string Rank, string Suit)
{
    public static CardDto From(Card card) => new(
        card.Value switch
        {
            1 => "A",
            11 => "J",
            12 => "Q",
            13 => "K",
            _ => card.Value.ToString(),
        },
        card.Suit.ToString().ToLowerInvariant());
}

internal static class BlackjackWire
{
    public static string Status(BlackjackStatus status) => status switch
    {
        BlackjackStatus.Ok => "ok",
        BlackjackStatus.InvalidBet => "invalidBet",
        BlackjackStatus.InsufficientTokens => "insufficientTokens",
        BlackjackStatus.HandInProgress => "handInProgress",
        BlackjackStatus.NoHand => "noHand",
        BlackjackStatus.NotAllowed => "notAllowed",
        _ => "failed",
    };

    public static string Result(GamePlayerResult result) => result switch
    {
        GamePlayerResult.Won => "win",
        GamePlayerResult.Tie => "tie",
        _ => "loss",
    };

    public static string Action(BlackjackPlayerAction action) => action switch
    {
        BlackjackPlayerAction.Hit => "hit",
        BlackjackPlayerAction.Stand => "stand",
        _ => "doubleDown",
    };

    public static BlackjackPlayerAction? ParseAction(string? action) => action switch
    {
        "hit" => BlackjackPlayerAction.Hit,
        "stand" => BlackjackPlayerAction.Stand,
        "doubleDown" => BlackjackPlayerAction.DoubleDown,
        _ => null,
    };
}
