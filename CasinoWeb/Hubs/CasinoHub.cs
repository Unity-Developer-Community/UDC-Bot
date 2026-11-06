using System.Security.Claims;
using CasinoWeb.Auth;
using CasinoWeb.Games;
using CasinoWeb.Tables;
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
public sealed class CasinoHub(SlotsService slots, SlotsWinAnnouncer announcer, WebBlackjackService blackjack, CasinoService casino, BotSettings settings, TableService tables) : Hub
{
    // Set when this connection was counted as present, so the disconnection is counted only for a connection that was.
    private const string PresentKey = "casino.present";
    private const int MaxNameLength = 32;

    private ulong UserId => ulong.TryParse(Context.UserIdentifier, out var id) ? id : throw new HubException(HubErrors.BadUser);

    public override async Task OnConnectedAsync()
    {
        Context.Items[PresentKey] = true;
        await tables.UserConnectedAsync(UserId);
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        if (Context.Items.Remove(PresentKey))
            await tables.UserDisconnectedAsync(UserId);
        await base.OnDisconnectedAsync(exception);
    }

    public async Task<HubState> GetState()
    {
        // The seated table is sent even while the casino is closed, so a reload can still offer to leave it.
        var poker = PokerInfo.Create(tables.AllowAiSeats, await tables.GetSeatedViewAsync(UserId));
        if (!settings.Casino.Enabled)
            return new HubState(false, 0, SlotsInfo.Create(0), new BlackjackInfo(WebBlackjackService.MinBet, null), poker);

        var user = await casino.GetOrCreateCasinoUser(UserId.ToString());
        var hand = await blackjack.GetOpenHandAsync(UserId, user.Tokens);
        return new HubState(
            true,
            user.Tokens,
            SlotsInfo.Create(await slots.GetPotHundredths()),
            new BlackjackInfo(WebBlackjackService.MinBet, hand is null ? null : BlackjackHandDto.From(hand)),
            poker);
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

    /// <summary>The same claim as <c>/casino tokens daily</c>: one statement in the database decides, so a claim from Discord at the same moment pays once.</summary>
    [GameAction]
    public async Task<DailyClaimDto> ClaimDailyReward()
    {
        var reward = settings.Casino.DailyRewardTokens;
        var (success, awarded, balance, nextRewardTime) = await casino.TryClaimDailyReward(UserId.ToString());
        return success
            ? new DailyClaimDto("ok", awarded, balance, new DailyInfo(reward, settings.Casino.DailyRewardIntervalSeconds))
            : new DailyClaimDto("notYet", 0, balance, DailyInfo.Create(reward, nextRewardTime));
    }

    #region Poker tables

    [GameAction]
    public IReadOnlyList<TableSummaryDto> TablesList() => [.. tables.List().Select(TableSummaryDto.From)];

    /// <param name="bigBlind">The big blind of one of the stakes presets in <see cref="PokerInfo.Stakes"/>.</param>
    [GameAction]
    public async Task<TableCallDto> TableCreate(long bigBlind) => TableCallDto.From(await tables.CreateAsync(UserId, DisplayName, bigBlind));

    /// <summary>Follows a table with public information only; the answer and the pushes show no hole cards but the showdown's.</summary>
    public async Task<TableCallDto> TableWatch(int tableId) => TableCallDto.From(await tables.WatchAsync(UserId, tableId));

    public Task TableUnwatch() => tables.UnwatchAsync(UserId);

    /// <param name="seat">The seat wanted, or <c>null</c> for the first free one.</param>
    [GameAction]
    public async Task<TableCallDto> TableSit(int tableId, int? seat) => TableCallDto.From(await tables.SitAsync(UserId, DisplayName, tableId, seat));

    /// <summary>Allowed while the casino is closed: a hand the player is in folds for them.</summary>
    public async Task<TableCallDto> TableLeave() => TableCallDto.From(await tables.LeaveAsync(UserId));

    [GameAction]
    public async Task<TableCallDto> TableAddAi() => TableCallDto.From(await tables.AddAiAsync(UserId));

    [GameAction]
    public async Task<TableCallDto> TableRemoveAi() => TableCallDto.From(await tables.RemoveAiAsync(UserId));

    /// <param name="action"><c>fold</c>, <c>check</c>, <c>call</c>, <c>bet</c>, <c>raise</c> or <c>allIn</c>.</param>
    /// <param name="amount">For a bet or raise, the total of the player's bet in this street.</param>
    [GameAction]
    public async Task<TableCallDto> PokerAct(string action, long amount) => TableCallDto.From(await tables.ActAsync(UserId, action, amount));

    private string DisplayName
    {
        get
        {
            var name = Context.User?.FindFirstValue(AuthenticationSetup.DisplayNameClaim)?.Trim();
            return string.IsNullOrEmpty(name) ? UserId.ToString() : name.Length > MaxNameLength ? name[..MaxNameLength] : name;
        }
    }

    #endregion
}
