using DiscordBot.Domain;

namespace DiscordBot.Services.Fun.Casino;

/// <summary>The production <see cref="ISlotsLedger"/>: balances through <see cref="CasinoService"/>, the jackpot through the casino repository.</summary>
public class CasinoSlotsLedger(CasinoService casinoService, ICasinoDatabase databaseService) : ISlotsLedger
{
    private ICasinoRepo Repo => databaseService.CasinoQuery ?? throw new InvalidOperationException("Casino database is not available");

    public async Task<long> GetTokens(string userId) => (await casinoService.GetOrCreateCasinoUser(userId)).Tokens;

    public Task<bool> Reserve(string userId, long bet) => casinoService.TryReserveTokens(userId, bet);

    public Task Release(string userId, long bet) => casinoService.ReleaseTokens(userId, bet);

    public Task<long> Settle(string userId, long payout, long net, Dictionary<string, string> details) =>
        casinoService.UpdateUserTokens(userId, payout, TransactionKind.Game, details, recordedAmount: net);

    public async Task<long> GetPot() => (await Repo.GetJackpot())?.Amount ?? 0;

    public Task<long> AddContribution(long hundredths) => Repo.AddJackpotContribution(hundredths, DateTime.UtcNow);

    public Task<long> ClaimShare(int bet) => Repo.ClaimJackpotShare(bet, SlotSettlementMath.JackpotFloorHundredths, DateTime.UtcNow);
}
