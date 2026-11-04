using DiscordBot.Domain;

namespace DiscordBot.Services.Fun.Casino;

/// <summary>The production <see cref="ISlotsLedger"/>: balances through <see cref="CasinoService"/>, the jackpot through the casino repository.</summary>
public class CasinoSlotsLedger(CasinoService casinoService, DatabaseService databaseService) : ISlotsLedger
{
    private ICasinoRepo Repo => databaseService.CasinoQuery ?? throw new InvalidOperationException("Casino database is not available");

    public async Task<long> GetTokens(string userId) => (await casinoService.GetOrCreateCasinoUser(userId)).Tokens;

    public Task Credit(string userId, long delta, Dictionary<string, string> details) =>
        casinoService.UpdateUserTokens(userId, delta, TransactionKind.Game, details);

    public async Task<long> GetPot() => (await Repo.GetJackpot()).Amount;

    public Task<long> AddContribution(long hundredths) => Repo.AddJackpotContribution(hundredths, DateTime.UtcNow);

    public Task<long> ClaimShare(int bet) => Repo.ClaimJackpotShare(bet, SlotSettlementMath.JackpotFloorHundredths, DateTime.UtcNow);
}
