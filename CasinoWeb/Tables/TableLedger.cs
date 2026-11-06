using DiscordBot.Domain;
using DiscordBot.Services.Fun.Casino;
using DiscordBot.Services.Logging;

namespace CasinoWeb.Tables;

public enum TakeResult
{
    Taken,
    Insufficient,
    Failed,
}

/// <summary>
/// The tokens of the tables. Every blind and bet is taken from the balance when it is made, so a hand can never be played with
/// money spent elsewhere; the hand then credits the winnings, or the table gives back what a hand never finished. Each step is
/// logged with the user, the table, the hand and the amount (prefix <c>WebPoker</c>): a crash that skips the credit leaves a
/// <c>took</c> line with no <c>settled</c> or <c>gave back</c> line, to credit with <c>/admin casino tokens-add</c>.
/// </summary>
public sealed class TableLedger(CasinoService casino, ILoggingService logging)
{
    private const string Log = "WebPoker";

    public async Task<long> GetBalanceAsync(ulong userId) => (await casino.GetOrCreateCasinoUser(userId.ToString())).Tokens;

    /// <param name="where">The table and hand, as logged: <c>table 3, hand 12</c>.</param>
    public async Task<TakeResult> TryTakeAsync(ulong userId, long amount, string where)
    {
        try
        {
            if (!await casino.TryReserveTokens(userId.ToString(), amount))
            {
                return TakeResult.Insufficient;
            }
        }
        catch (Exception ex)
        {
            await logging.LogChannelAndFile($"{Log}: ERROR taking {amount} tokens from user {userId}, {where}: {ex.Message}", ExtendedLogSeverity.Error);
            return TakeResult.Failed;
        }

        await logging.LogChannelAndFile($"{Log}: took {amount} tokens from user {userId}, {where}");
        return TakeResult.Taken;
    }

    /// <summary>Best effort: a failure is logged with the amount instead of thrown.</summary>
    public async Task GiveBackAsync(ulong userId, long amount, string where)
    {
        try
        {
            await casino.ReleaseTokens(userId.ToString(), amount);
            await logging.LogChannelAndFile($"{Log}: gave back {amount} tokens to user {userId}, {where}");
        }
        catch (Exception ex)
        {
            await logging.LogChannelAndFile(
                $"{Log}: could not give back {amount} tokens to user {userId}, {where}: {ex.Message}. Credit them with /admin casino tokens-add.",
                ExtendedLogSeverity.Error);
        }
    }

    /// <summary>Credits the winnings and records the hand's net result as one <c>Game</c> transaction.</summary>
    /// <param name="staked">What the player put in, already taken from the balance.</param>
    public async Task<bool> SettleAsync(ulong userId, long credited, long staked, string gameName, string where)
    {
        var net = credited - staked;
        try
        {
            await casino.UpdateUserTokens(userId.ToString(), credited, TransactionKind.Game,
                new Dictionary<string, string> { ["game"] = gameName }, recordedAmount: net);
        }
        catch (Exception ex)
        {
            await logging.LogChannelAndFile(
                $"{Log}: could not settle {where} of user {userId}: owed {credited} tokens (staked {staked}, result {net}), already taken from the balance. " +
                $"The update may have been partly applied: check the balance, then fix it with /admin casino tokens-add if needed. {ex.Message}",
                ExtendedLogSeverity.Error);
            return false;
        }

        await logging.LogChannelAndFile($"{Log}: settled {where} of user {userId}: staked {staked}, credited {credited}, result {net}");
        return true;
    }
}
