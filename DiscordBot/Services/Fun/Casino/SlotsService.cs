using System.Collections.Concurrent;
using DiscordBot.Domain;

namespace DiscordBot.Services.Fun.Casino;

/// <summary>Balance, jackpot and transaction access of the slots, separated from the database so the service is testable.</summary>
public interface ISlotsLedger
{
    Task<long> GetTokens(string userId);

    /// <summary>Changes the balance by <paramref name="delta"/> and records a <c>Game</c> transaction.</summary>
    Task Credit(string userId, long delta, Dictionary<string, string> details);

    /// <summary>The jackpot in hundredths of a token.</summary>
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

    /// <summary>A jackpot or a win of at least <see cref="SlotMachine.PublicWinMultiplier"/> times the bet is announced in the channel.</summary>
    public bool PostPublicly => Status == SlotsStatus.Ok && Result is { } result
        && (result.IsJackpot || result.Multiplier >= SlotMachine.PublicWinMultiplier);

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

            DiscardExpiredHolds();
            _holds.TryRemove(userId, out _); // a new spin ends the previous offer

            var result = SlotMachine.Spin(_random);
            return await PlayAsync(userId, result, bet, balance);
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

            // Removing exactly this entry is what makes the offer single use: the engine itself would accept a second respin.
            if (!_holds.TryRemove(new KeyValuePair<ulong, PendingHold>(userId, hold)) || hold.IsExpired(_time.GetUtcNow()))
                return SlotsPlay.Refused(SlotsStatus.NoHold);

            var balance = await _ledger.GetTokens(userId.ToString());
            var result = SlotMachine.Respin(hold.Result, hold.Snapshot(), _random);
            return await PlayAsync(userId, result, hold.Bet, balance);
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

    private async Task<SlotsPlay> PlayAsync(ulong userId, SlotResult result, int bet, long balanceBefore)
    {
        var (payout, net, pot, shareHundredths) = await SettleAsync(userId, result, bet);

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
            Balance = balanceBefore + net,
            PotHundredths = pot,
            Gif = await TryRenderAsync(result),
            Hold = hold
        };
    }

    /// <summary>
    /// Feeds or claims the jackpot first, then settles the player. A failure after a claim is logged with the amount so an admin
    /// can credit it back.
    /// </summary>
    private async Task<(long Payout, long Net, long PotHundredths, long ShareHundredths)> SettleAsync(ulong userId, SlotResult result, int bet)
    {
        var contribution = result.IsRespin ? 0 : SlotSettlementMath.Contribution(bet);
        var pot = contribution > 0 ? await _ledger.AddContribution(contribution) : await _ledger.GetPot();

        long share = 0;
        long payout;
        if (result.IsJackpot)
        {
            share = await _ledger.ClaimShare(bet);
            pot = SlotSettlementMath.PotAfterClaim(pot, share);
            payout = share / SlotSettlementMath.HundredthsPerToken;
        }
        else
        {
            payout = (long)result.Multiplier * bet;
        }

        var net = payout - (result.IsRespin ? 0 : bet);

        // A respin that wins nothing changes nothing: it would only add an empty line to the history.
        if (result.IsRespin && net == 0)
            return (payout, net, pot, share);

        var details = new Dictionary<string, string> { ["game"] = GameName };
        if (result.IsRespin) details["respin"] = "true";
        if (result.IsJackpot) details["jackpot"] = payout.ToString();

        try
        {
            await _ledger.Credit(userId.ToString(), net, details);
        }
        catch
        {
            if (share > 0)
                await _logging.LogChannelAndFile(
                    $"SlotsService: jackpot share of {payout} tokens claimed for user {userId} but not credited. Credit it with /admin casino tokens-add.",
                    ExtendedLogSeverity.Error);
            throw;
        }

        return (payout, net, pot, share);
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
