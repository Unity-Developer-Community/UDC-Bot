using CasinoWeb.Games;
using DiscordBot.Services.Fun.Casino;
using DiscordBot.Settings;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace CasinoWeb.Hubs;

/// <summary>
/// The one connection a player's browser keeps open. The server decides everything; the methods take intents and return
/// results. The membership, rate limit and casino switch are applied by <see cref="CasinoHubFilter"/>.
/// </summary>
[Authorize]
public sealed class CasinoHub(SlotsService slots, SlotsWinAnnouncer announcer, WebBlackjackService blackjack, CasinoService casino, BotSettings settings) : Hub
{
    private ulong UserId => ulong.TryParse(Context.UserIdentifier, out var id) ? id : throw new HubException(HubErrors.BadUser);

    public async Task<HubState> GetState()
    {
        if (!settings.Casino.Enabled)
            return new HubState(false, 0, SlotsInfo.Create(0), new BlackjackInfo(WebBlackjackService.MinBet, null));

        var user = await casino.GetOrCreateCasinoUser(UserId.ToString());
        var hand = await blackjack.GetOpenHandAsync(UserId, user.Tokens);
        return new HubState(
            true,
            user.Tokens,
            SlotsInfo.Create(await slots.GetPotHundredths()),
            new BlackjackInfo(WebBlackjackService.MinBet, hand is null ? null : BlackjackHandDto.From(hand)));
    }

    [GameAction]
    public async Task<SlotsPlayDto> SlotsSpin(int bet) => Announced(await slots.SpinAsync(UserId, bet));

    /// <summary>The held reels after the flip, or <c>null</c> when the offer is gone or expired.</summary>
    [GameAction]
    public SlotsHoldDto? SlotsToggleHold(long offerId, int reel) =>
        slots.ToggleHold(UserId, offerId, reel) is { } hold ? SlotsHoldDto.From(hold) : null;

    [GameAction]
    public async Task<SlotsPlayDto> SlotsRespin(long offerId) => Announced(await slots.RespinAsync(UserId, offerId));

    /// <summary>Starts the announcement of a big win in Discord without waiting for it, then converts the play for the browser.</summary>
    private SlotsPlayDto Announced(SlotsPlay play)
    {
        announcer.Announce(UserId, play);
        return SlotsPlayDto.From(play);
    }

    [GameAction]
    public async Task<BlackjackPlayDto> BlackjackDeal(long bet) => BlackjackPlayDto.From(await blackjack.DealAsync(UserId, bet));

    /// <param name="action"><c>hit</c>, <c>stand</c> or <c>doubleDown</c>.</param>
    [GameAction]
    public async Task<BlackjackPlayDto> BlackjackAct(string action) => BlackjackWire.ParseAction(action) is { } parsed
        ? BlackjackPlayDto.From(await blackjack.ActAsync(UserId, parsed))
        : BlackjackPlayDto.From(new BlackjackPlay(BlackjackStatus.NotAllowed));
}
