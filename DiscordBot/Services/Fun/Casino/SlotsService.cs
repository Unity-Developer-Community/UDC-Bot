using System.Collections.Concurrent;
using DiscordBot.Domain;

namespace DiscordBot.Services.Fun.Casino;

/// <summary>Balance, jackpot and transaction access of the slots, separated from the database so the service is testable.</summary>
public interface ISlotsLedger
{
    Task<long> GetTokens(string userId);

    /// <summary>Takes the bet from the balance when it covers it, without a transaction row. <c>false</c> when the balance is too low.</summary>
    Task<bool> Reserve(string userId, long bet);

    /// <summary>Gives a reserved bet back, without a transaction row.</summary>
    Task Release(string userId, long bet);

    /// <summary>Adds the payout to the balance and records one <c>Game</c> transaction of <paramref name="net"/>, payout minus bet.</summary>
    /// <returns>The new balance.</returns>
    Task<long> Settle(string userId, long payout, long net, Dictionary<string, string> details);

    /// <summary>The jackpot in hundredths of a token, 0 when its database row is missing.</summary>
    Task<long> GetPot();

    /// <summary>Adds to the jackpot and returns the new pot, in hundredths.</summary>
    Task<long> AddContribution(long hundredths);

    /// <summary>Takes the share of a won jackpot atomically and returns it, in hundredths.</summary>
    Task<long> ClaimShare(int bet);
}

public enum SlotsStatus
{
    Ok,
    InvalidBet,
    InsufficientTokens,
    /// <summary>The player's previous spin is still being processed.</summary>
    Busy,
    /// <summary>The hold offer was already used, replaced, or has expired.</summary>
    NoHold,
    Failed
}

/// <summary>The held reels of a pending hold offer.</summary>
public sealed record SlotsHold(long OfferId, int Bet, IReadOnlyList<bool> Held);

/// <summary>What a spin or respin did. Only <see cref="Status"/> is meaningful unless it is <see cref="SlotsStatus.Ok"/>.</summary>
public sealed record SlotsPlay
{
    public SlotsStatus Status { get; init; }
    public SlotResult? Result { get; init; }
    public int Bet { get; init; }

    /// <summary>Tokens paid to the player.</summary>
    public long Payout { get; init; }

    /// <summary>Change of the player's balance, 0 or more for a free respin.</summary>
    public long Net { get; init; }
    public long Balance { get; init; }
    public long PotHundredths { get; init; }

    /// <summary>The animation, or <c>null</c> when it could not be rendered: the text result is still complete.</summary>
    public byte[]? Gif { get; init; }

    /// <summary>The hold offer of a losing spin, <c>null</c> when none was made.</summary>
    public SlotsHold? Hold { get; init; }

    /// <summary>A jackpot or a line rarer than one spin in <see cref="SlotMachine.PublicWinOneIn"/> is announced in the channel.</summary>
    public bool PostPublicly => Status == SlotsStatus.Ok && Result is { } result
        && (result.IsJackpot || SlotMachine.IsRareWin(result.Symbols));

    public static SlotsPlay Refused(SlotsStatus status) => new() { Status = status };
}

/// <summary>
/// Plays the slot machine for a player: validates, spins, settles the balance and the shared jackpot, renders the GIF
/// and keeps the pending hold offers. Slots is not a table game, so it does not go through <see cref="GameService"/>.
/// </summary>
public class SlotsService
{
    private const string GameName = "Slots";
    private static readonly TimeSpan HoldLifetime = TimeSpan.FromMinutes(10);

    private readonly ISlotsLedger _ledger;
    private readonly ISlotRandom _random;
    private readonly SlotsRenderer _renderer;
    private readonly ILoggingService _logging;
    private readonly TimeProvider _time;

    private readonly ConcurrentDictionary<ulong, byte> _inFlight = new();
    private readonly ConcurrentDictionary<ulong, PendingHold> _holds = new();
    private long _lastOfferId;

    public SlotsService(CasinoService casinoService, DatabaseService databaseService, SlotsRenderer renderer, ILoggingService logging)
        : this(new CasinoSlotsLedger(casinoService, databaseService), new SecureSlotRandom(), renderer, logging, TimeProvider.System)
    {
    }

    public SlotsService(ISlotsLedger ledger, ISlotRandom random, SlotsRenderer renderer, ILoggingService logging, TimeProvider time)
    {
        _ledger = ledger;
        _random = random;
        _renderer = renderer;
        _logging = logging;
        _time = time;
    }

    public Task<long> GetPotHundredths() => _ledger.GetPot();

    public async Task<SlotsPlay> SpinAsync(ulong userId, int bet)
    {
        if (bet < SlotMachine.MinBet || bet > SlotMachine.MaxBet)
            return SlotsPlay.Refused(SlotsStatus.InvalidBet);

        // A click while the previous spin is still being processed would double-spend the same balance read.
        if (!_inFlight.TryAdd(userId, 0))
            return SlotsPlay.Refused(SlotsStatus.Busy);

        try
        {
            var balance = await _ledger.GetTokens(userId.ToString());
            if (balance < bet)
                return SlotsPlay.Refused(SlotsStatus.InsufficientTokens);

            // Refuse before anything changes: a missing jackpot would charge the bet and pay a win of 0.
            await EnsureJackpotAsync();

            var result = SlotMachine.Spin(_random);

            // The check above can be stale by now (a gift or a table game may have spent the tokens): the reservation decides.
            // It comes last, right before the settlement that gives the bet back on failure, so nothing that can throw sits between.
            if (!await _ledger.Reserve(userId.ToString(), bet))
                return SlotsPlay.Refused(SlotsStatus.InsufficientTokens);

            DiscardExpiredHolds();
            _holds.TryRemove(userId, out _); // a new spin ends the previous offer

            return await PlayAsync(userId, result, bet, balance, betReserved: true);
        }
        catch (Exception ex)
        {
            await _logging.LogChannelAndFile($"SlotsService: ERROR spinning for user {userId} (bet {bet}): {ex}", ExtendedLogSeverity.Error);
            return SlotsPlay.Refused(SlotsStatus.Failed);
        }
        finally
        {
            _inFlight.TryRemove(userId, out _);
        }
    }

    /// <summary>Spends the player's hold offer: spins the reels that are not held once, for free. An offer can be spent once.</summary>
    public async Task<SlotsPlay> RespinAsync(ulong userId, long offerId)
    {
        if (!_inFlight.TryAdd(userId, 0))
            return SlotsPlay.Refused(SlotsStatus.Busy);

        try
        {
            if (!_holds.TryGetValue(userId, out var hold) || hold.OfferId != offerId)
                return SlotsPlay.Refused(SlotsStatus.NoHold);

            // Checked before the offer is spent, so a missing jackpot does not cost the player the free respin.
            await EnsureJackpotAsync();

            // Removing exactly this entry is what makes the offer single use: the engine itself would accept a second respin.
            if (!_holds.TryRemove(new KeyValuePair<ulong, PendingHold>(userId, hold)) || hold.IsExpired(_time.GetUtcNow()))
                return SlotsPlay.Refused(SlotsStatus.NoHold);

            var balance = await _ledger.GetTokens(userId.ToString());
            var result = SlotMachine.Respin(hold.Result, hold.Snapshot(), _random);
            return await PlayAsync(userId, result, hold.Bet, balance, betReserved: false);
        }
        catch (Exception ex)
        {
            await _logging.LogChannelAndFile($"SlotsService: ERROR on respin for user {userId}: {ex}", ExtendedLogSeverity.Error);
            return SlotsPlay.Refused(SlotsStatus.Failed);
        }
        finally
        {
            _inFlight.TryRemove(userId, out _);
        }
    }

    /// <summary>Flips the hold on a reel and returns the held reels, or <c>null</c> when the offer is gone or expired.</summary>
    public SlotsHold? ToggleHold(ulong userId, long offerId, int reel)
    {
        if (reel < 0 || reel >= SlotMachine.ReelCount)
            return null;
        if (!_holds.TryGetValue(userId, out var hold) || hold.OfferId != offerId || hold.IsExpired(_time.GetUtcNow()))
            return null;

        hold.Toggle(reel);
        return new SlotsHold(hold.OfferId, hold.Bet, hold.Snapshot());
    }

    private async Task<SlotsPlay> PlayAsync(ulong userId, SlotResult result, int bet, long balanceBefore, bool betReserved)
    {
        var (payout, net, balance, pot, shareHundredths) = await SettleAsync(userId, result, bet, balanceBefore, betReserved);

        SlotsHold? hold = null;
        if (result.HoldOffered)
        {
            var pending = new PendingHold(Interlocked.Increment(ref _lastOfferId), result, bet, _time.GetUtcNow() + HoldLifetime);
            _holds[userId] = pending;
            hold = new SlotsHold(pending.OfferId, bet, pending.Snapshot());
        }

        return new SlotsPlay
        {
            Status = SlotsStatus.Ok,
            Result = result,
            Bet = bet,
            Payout = payout,
            Net = net,
            Balance = balance,
            PotHundredths = pot,
            Gif = await TryRenderAsync(result),
            Hold = hold
        };
    }

    /// <summary>
    /// Feeds or claims the jackpot first, then settles the player. A failure before the settlement gives the reserved bet back;
    /// a failure of the settlement itself is logged with the amount so an admin can credit it.
    /// </summary>
    private async Task<(long Payout, long Net, long Balance, long PotHundredths, long ShareHundredths)> SettleAsync(
        ulong userId, SlotResult result, int bet, long balanceBefore, bool betReserved)
    {
        long pot;
        long share = 0;
        long payout;
        try
        {
            var contribution = result.IsRespin ? 0 : SlotSettlementMath.Contribution(bet);
            pot = contribution > 0 ? await _ledger.AddContribution(contribution) : await _ledger.GetPot();

            if (result.IsJackpot)
            {
                share = await _ledger.ClaimShare(bet);
                if (share <= 0)
                    throw new InvalidOperationException($"The jackpot claim for a bet of {bet} returned {share}: the jackpot row is missing or was changed.");
                pot = SlotSettlementMath.PotAfterClaim(pot, share);
                payout = share / SlotSettlementMath.HundredthsPerToken;
            }
            else
            {
                payout = (long)result.Multiplier * bet;
            }
        }
        catch when (betReserved)
        {
            await ReleaseBetAsync(userId, bet);
            throw;
        }

        var net = payout - (result.IsRespin ? 0 : bet);

        // A respin that wins nothing changes nothing: it would only add an empty line to the history.
        if (result.IsRespin && net == 0)
            return (payout, net, balanceBefore, pot, share);

        var details = new Dictionary<string, string> { ["game"] = GameName };
        if (result.IsRespin) details["respin"] = "true";
        if (result.IsJackpot) details["jackpot"] = payout.ToString();

        long balance;
        try
        {
            balance = await _ledger.Settle(userId.ToString(), payout, net, details);
        }
        catch
        {
            // Nothing is credited for a loss, so giving the bet back cannot pay twice: the player is left as if the spin never ran.
            if (betReserved && payout == 0)
            {
                await ReleaseBetAsync(userId, bet);
                throw;
            }

            // The jackpot and the balance are separate writes and the bet is already taken, so say what the player is owed for an admin to settle.
            await _logging.LogChannelAndFile(
                share > 0
                    ? $"SlotsService: jackpot share of {payout} tokens claimed for user {userId} but not credited. Owed: {payout} tokens (the bet of {bet} was already taken). The balance update may have been partly applied: check it, then fix it with /admin casino tokens-add."
                    : $"SlotsService: could not credit user {userId} {payout} tokens for a {(result.IsRespin ? "free respin" : "spin")} (payout {payout}, bet {bet}, the bet was already taken). The balance update may have been partly applied: check it, then fix it with /admin casino tokens-add if needed.",
                ExtendedLogSeverity.Error);
            throw;
        }

        return (payout, net, balance, pot, share);
    }

    // Best effort: the spin already failed, so a failure to give the bet back is logged instead of masking the original error.
    private async Task ReleaseBetAsync(ulong userId, int bet)
    {
        try
        {
            await _ledger.Release(userId.ToString(), bet);
        }
        catch (Exception ex)
        {
            await _logging.LogChannelAndFile($"SlotsService: a spin failed and its bet of {bet} tokens could not be given back to user {userId}: {ex.Message}. Credit it with /admin casino tokens-add.", ExtendedLogSeverity.Error);
        }
    }

    private async Task EnsureJackpotAsync()
    {
        var pot = await _ledger.GetPot();
        if (pot < SlotSettlementMath.JackpotFloorHundredths)
            throw new InvalidOperationException($"The jackpot is {pot} hundredths, below its floor: the casino_jackpot row is missing or damaged.");
    }

    // The spin is already settled: a rendering failure only costs the animation.
    private async Task<byte[]?> TryRenderAsync(SlotResult result)
    {
        try
        {
            using var stream = await _renderer.RenderSpinAsync(result);
            return stream.ToArray();
        }
        catch (Exception ex)
        {
            await _logging.LogChannelAndFile($"SlotsService: could not render the spin animation: {ex}", ExtendedLogSeverity.Error);
            return null;
        }
    }

    private void DiscardExpiredHolds()
    {
        var now = _time.GetUtcNow();
        foreach (var entry in _holds.Where(e => e.Value.IsExpired(now)))
            _holds.TryRemove(entry);
    }

    private sealed class PendingHold(long offerId, SlotResult result, int bet, DateTimeOffset expiresAt)
    {
        private readonly bool[] _held = new bool[SlotMachine.ReelCount];
        private readonly object _gate = new();

        public long OfferId { get; } = offerId;
        public SlotResult Result { get; } = result;
        public int Bet { get; } = bet;

        public bool IsExpired(DateTimeOffset now) => now >= expiresAt;

        public void Toggle(int reel)
        {
            lock (_gate) _held[reel] = !_held[reel];
        }

        public bool[] Snapshot()
        {
            lock (_gate) return (bool[])_held.Clone();
        }
    }
}
