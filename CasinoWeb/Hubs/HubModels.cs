using DiscordBot.Domain;
using DiscordBot.Services.Fun.Casino;

namespace CasinoWeb.Hubs;

/// <summary>What the client needs when it opens a game: the rules to display and the numbers that change.</summary>
public sealed record HubState(bool CasinoEnabled, long Balance, SlotsInfo Slots);

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
