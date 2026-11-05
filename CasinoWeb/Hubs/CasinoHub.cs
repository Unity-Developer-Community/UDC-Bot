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
public sealed class CasinoHub(SlotsService slots, CasinoService casino, BotSettings settings) : Hub
{
    private ulong UserId => ulong.TryParse(Context.UserIdentifier, out var id) ? id : throw new HubException(HubErrors.BadUser);

    public async Task<HubState> GetState()
    {
        if (!settings.Casino.Enabled)
            return new HubState(false, 0, SlotsInfo.Create(0));

        var user = await casino.GetOrCreateCasinoUser(UserId.ToString());
        return new HubState(true, user.Tokens, SlotsInfo.Create(await slots.GetPotHundredths()));
    }

    [GameAction]
    public async Task<SlotsPlayDto> SlotsSpin(int bet) => SlotsPlayDto.From(await slots.SpinAsync(UserId, bet));

    /// <summary>The held reels after the flip, or <c>null</c> when the offer is gone or expired.</summary>
    [GameAction]
    public SlotsHoldDto? SlotsToggleHold(long offerId, int reel) =>
        slots.ToggleHold(UserId, offerId, reel) is { } hold ? SlotsHoldDto.From(hold) : null;

    [GameAction]
    public async Task<SlotsPlayDto> SlotsRespin(long offerId) => SlotsPlayDto.From(await slots.RespinAsync(UserId, offerId));
}
