using System.Collections.Concurrent;
using DiscordBot.Domain;
using DiscordBot.Services.Logging;
using DiscordBot.Services.Fun.Casino;
using DiscordBot.Settings;

namespace CasinoWeb.Games;

public enum BlackjackStatus
{
    Ok,
    InvalidBet,
    InsufficientTokens,
    /// <summary>A deal was asked while the player's previous hand is still open; the result carries that hand.</summary>
    HandInProgress,
    /// <summary>The player has no open hand: it was finished, timed out, or given back at a restart.</summary>
    NoHand,
    /// <summary>The action is not possible in the hand's current state.</summary>
    NotAllowed,
    Failed,
}

/// <summary>What the player may see of a hand. While the player acts, the dealer's second card is not in it.</summary>
/// <param name="Bet">The tokens at stake, doubled by a double down.</param>
/// <param name="DealerCards">The dealer's visible cards: only the first while the hand is open.</param>
/// <param name="DealerValue">The value of <paramref name="DealerCards"/>.</param>
/// <param name="Credited">Tokens given back to the balance at the end: twice the bet for a win, the bet for a tie, 0 for a loss.</param>
/// <param name="Net">The hand's result for the player, <paramref name="Credited"/> minus <paramref name="Bet"/>, recorded as the transaction.</param>
/// <param name="Allowed">The actions the player can take now; empty once finished.</param>
public sealed record BlackjackHandView(
    Guid HandId,
    long Bet,
    IReadOnlyList<Card> PlayerCards,
    int PlayerValue,
    IReadOnlyList<Card> DealerCards,
    int DealerValue,
    bool IsFinished,
    GamePlayerResult Result,
    long Credited,
    long Net,
    IReadOnlyList<BlackjackPlayerAction> Allowed);

/// <summary>The result of a deal or an action. <see cref="Balance"/> is the balance after it, 0 when the call was refused before reading it.</summary>
public sealed record BlackjackPlay(BlackjackStatus Status, BlackjackHandView? Hand = null, long Balance = 0);

/// <summary>
/// Solo blackjack against the dealer for the web casino, one open hand per player. The Discord rules run unchanged
/// (<see cref="Blackjack"/> in a <see cref="GameSession{TGame}"/>); what this adds is money safety: every bet is reserved
/// from the balance before it is played, so a hand can never be lost to a balance spent elsewhere, and a reserved bet is
/// always either settled or given back. Each reservation, settlement and release is logged with the user, the hand and
/// the amount, so a crash that skips both leaves an audit trail for <c>/admin casino tokens-add</c>.
/// </summary>
public sealed class WebBlackjackService(CasinoService casino, BotSettings settings, ILoggingService logging, TimeProvider time, Func<Blackjack> newGame)
{
    public const long MinBet = 1;
    private const string Log = "WebBlackjack";

    // One seat per player who ever played: bounded by the number of guild members. The gate serializes a player's calls,
    // so two clicks can never act on the same hand at once.
    private readonly ConcurrentDictionary<ulong, Seat> _seats = new();
    private volatile bool _stopped;

    private TimeSpan IdleTimeout => TimeSpan.FromMinutes(settings.Casino.GameTimeoutMinutes);

    /// <summary>The player's open hand, or <c>null</c>, so a page that was closed mid-hand can resume it.</summary>
    public async Task<BlackjackHandView?> GetOpenHandAsync(ulong userId, long balance)
    {
        if (!_seats.TryGetValue(userId, out var seat))
            return null;
        return await seat.RunAsync(() => Task.FromResult(seat.Hand is { } hand ? View(hand, balance) : null));
    }

    public async Task<BlackjackPlay> DealAsync(ulong userId, long bet)
    {
        if (bet < MinBet)
            return new BlackjackPlay(BlackjackStatus.InvalidBet);

        var seat = _seats.GetOrAdd(userId, _ => new Seat());
        return await seat.RunAsync(async () =>
        {
            var id = userId.ToString();
            if (seat.Hand is { } open)
            {
                var balance = (await casino.GetOrCreateCasinoUser(id)).Tokens;
                return new BlackjackPlay(BlackjackStatus.HandInProgress, View(open, balance), balance);
            }

            // Checked inside the gate: a deal that waited on it while the server began stopping must not reserve anything.
            if (_stopped)
                return new BlackjackPlay(BlackjackStatus.Failed);

            var handId = Guid.NewGuid();
            try
            {
                if (!await casino.TryReserveTokens(id, bet))
                    return new BlackjackPlay(BlackjackStatus.InsufficientTokens);
            }
            catch (Exception ex)
            {
                await logging.LogChannelAndFile($"{Log}: ERROR reserving {bet} tokens for user {userId}: {ex}", ExtendedLogSeverity.Error);
                return new BlackjackPlay(BlackjackStatus.Failed);
            }
            await logging.LogChannelAndFile($"{Log}: reserved {bet} tokens for user {userId}, hand {handId}");

            OpenHand hand;
            try
            {
                hand = StartHand(handId, userId, bet);
            }
            catch (Exception ex)
            {
                await logging.LogChannelAndFile($"{Log}: ERROR dealing hand {handId} for user {userId}: {ex}", ExtendedLogSeverity.Error);
                await ReleaseAsync(userId, handId, bet);
                return new BlackjackPlay(BlackjackStatus.Failed);
            }

            seat.Hand = hand;
            // A blackjack on the deal leaves the player nothing to do: the dealer plays and the hand settles at once.
            if (hand.Game.CurrentPlayer is null)
                return await FinishAsync(seat, hand, reason: "blackjack on the deal");

            try
            {
                var after = (await casino.GetOrCreateCasinoUser(id)).Tokens;
                return new BlackjackPlay(BlackjackStatus.Ok, View(hand, after), after);
            }
            catch (Exception ex)
            {
                // The hand is dealt and open: the client asks for the state again and resumes it.
                await logging.LogChannelAndFile($"{Log}: ERROR reading the balance after dealing hand {handId} for user {userId}: {ex}", ExtendedLogSeverity.Error);
                return new BlackjackPlay(BlackjackStatus.Failed);
            }
        });
    }

    public async Task<BlackjackPlay> ActAsync(ulong userId, BlackjackPlayerAction action)
    {
        if (!_seats.TryGetValue(userId, out var seat))
            return new BlackjackPlay(BlackjackStatus.NoHand);

        return await seat.RunAsync(async () =>
        {
            if (seat.Hand is not { } hand)
                return new BlackjackPlay(BlackjackStatus.NoHand);

            var id = userId.ToString();
            if (!hand.Game.CanPlayerAct(hand.Player) || !Enum.IsDefined(action))
                return new BlackjackPlay(BlackjackStatus.NotAllowed, View(hand, (await casino.GetOrCreateCasinoUser(id)).Tokens));

            try
            {
                var extra = 0L;
                if (action == BlackjackPlayerAction.DoubleDown)
                {
                    // The second bet is taken before the card is drawn, so a double down the balance cannot cover never happens.
                    extra = hand.Player.Bet;
                    if (!await casino.TryReserveTokens(id, extra))
                    {
                        var balance = (await casino.GetOrCreateCasinoUser(id)).Tokens;
                        return new BlackjackPlay(BlackjackStatus.InsufficientTokens, View(hand, balance), balance);
                    }
                    hand.Reserved += extra;
                    await logging.LogChannelAndFile($"{Log}: reserved {extra} more tokens for user {userId}, hand {hand.Id} (double down)");
                }

                try
                {
                    hand.Session.DoPlayerAction(userId, action);
                }
                catch when (extra > 0)
                {
                    hand.Reserved -= extra;
                    await ReleaseAsync(userId, hand.Id, extra);
                    throw;
                }
                hand.LastActionAt = time.GetUtcNow();

                if (hand.Game.CurrentPlayer is null)
                    return await FinishAsync(seat, hand, reason: action.ToString());

                var after = (await casino.GetOrCreateCasinoUser(id)).Tokens;
                return new BlackjackPlay(BlackjackStatus.Ok, View(hand, after), after);
            }
            catch (Exception ex)
            {
                await logging.LogChannelAndFile($"{Log}: ERROR on {action} for user {userId}, hand {hand.Id}: {ex}", ExtendedLogSeverity.Error);
                return new BlackjackPlay(BlackjackStatus.Failed);
            }
        });
    }

    /// <summary>Stands and settles every hand that got no action for <c>Casino.GameTimeoutMinutes</c>, so no bet stays reserved for long.</summary>
    public async Task SweepIdleHandsAsync()
    {
        var now = time.GetUtcNow();
        foreach (var (userId, seat) in _seats)
        {
            if (seat.Hand is not { } candidate || now - candidate.LastActionAt < IdleTimeout)
                continue;

            try
            {
                await seat.RunAsync(async () =>
                {
                    // Rechecked inside the gate: the player may have acted or finished while the sweep waited.
                    if (seat.Hand is not { } hand || time.GetUtcNow() - hand.LastActionAt < IdleTimeout)
                        return;
                    if (hand.Game.CanPlayerAct(hand.Player))
                        hand.Session.DoPlayerAction(userId, BlackjackPlayerAction.Stand);
                    await FinishAsync(seat, hand, reason: "timed out");
                });
            }
            catch (Exception ex)
            {
                await logging.LogChannelAndFile($"{Log}: ERROR settling the idle hand of user {userId}: {ex}", ExtendedLogSeverity.Error);
            }
        }
    }

    /// <summary>Refuses new deals and gives back the reserved bets of every open hand. Called once, when the server stops.</summary>
    public async Task ReleaseAllAsync()
    {
        _stopped = true;
        foreach (var (userId, seat) in _seats)
        {
            await seat.RunAsync(async () =>
            {
                if (seat.Hand is not { } hand)
                    return;
                seat.Hand = null;
                await ReleaseAsync(userId, hand.Id, hand.Reserved);
            });
        }
    }

    private OpenHand StartHand(Guid handId, ulong userId, long bet)
    {
        var game = newGame();
        var session = new GameSession<Blackjack>(game, maxSeats: 1);
        if (!session.AddPlayer(userId, bet))
            throw new InvalidOperationException("The player could not take the seat.");
        // A solo table starts as soon as its one player is ready.
        session.SetPlayerReady(userId, true);
        if (game.State != GameState.InProgress)
            throw new InvalidOperationException($"The hand did not start: it is {game.State}.");
        return new OpenHand(handId, game, session, session.GetPlayer(userId)!, bet, time.GetUtcNow());
    }

    /// <summary>Plays the dealer, credits the bet plus the result and records the result as one <c>Game</c> transaction.</summary>
    private async Task<BlackjackPlay> FinishAsync(Seat seat, OpenHand hand, string reason)
    {
        var userId = hand.Player.UserId;
        while (hand.Session.HasNextDealerAction())
            await hand.Session.DoNextDealerAction();

        var (_, net) = hand.Session.EndGame().Single();
        var credited = hand.Reserved + net;
        // The hand ends here whatever the settlement does: retrying it could pay twice.
        seat.Hand = null;

        long balance;
        try
        {
            balance = await casino.UpdateUserTokens(userId.ToString(), credited, TransactionKind.Game,
                new Dictionary<string, string> { ["game"] = hand.Game.Name }, recordedAmount: net);
        }
        catch (Exception ex)
        {
            await logging.LogChannelAndFile(
                $"{Log}: could not settle hand {hand.Id} of user {userId} ({reason}): owed {credited} tokens (bet {hand.Reserved}, result {net}), already taken from the balance. " +
                $"The update may have been partly applied: check the balance, then fix it with /admin casino tokens-add if needed. {ex.Message}",
                ExtendedLogSeverity.Error);
            return new BlackjackPlay(BlackjackStatus.Failed);
        }

        await logging.LogChannelAndFile($"{Log}: settled hand {hand.Id} of user {userId} ({reason}): bet {hand.Reserved}, credited {credited}, result {net}");
        return new BlackjackPlay(BlackjackStatus.Ok, View(hand, balance, credited, net), balance);
    }

    // Best effort: the hand is already gone, so a failure is logged with the amount instead of thrown.
    private async Task ReleaseAsync(ulong userId, Guid handId, long amount)
    {
        try
        {
            await casino.ReleaseTokens(userId.ToString(), amount);
            await logging.LogChannelAndFile($"{Log}: gave back {amount} tokens to user {userId}, hand {handId}");
        }
        catch (Exception ex)
        {
            await logging.LogChannelAndFile(
                $"{Log}: could not give back {amount} tokens to user {userId}, hand {handId}: {ex.Message}. Credit them with /admin casino tokens-add.",
                ExtendedLogSeverity.Error);
        }
    }

    private static BlackjackHandView View(OpenHand hand, long balance, long credited = 0, long net = 0)
    {
        var game = hand.Game;
        var finished = game.IsCompleted;
        var dealer = finished ? game.DealerCards.ToList() : game.DealerCards.Take(1).ToList();
        var allowed = new List<BlackjackPlayerAction>();
        if (!finished && game.CanPlayerAct(hand.Player))
        {
            allowed.Add(BlackjackPlayerAction.Hit);
            allowed.Add(BlackjackPlayerAction.Stand);
            if (balance >= hand.Player.Bet)
                allowed.Add(BlackjackPlayerAction.DoubleDown);
        }

        return new BlackjackHandView(
            hand.Id,
            hand.Player.Bet,
            game.GameData[hand.Player].PlayerCards.ToList(),
            game.GetPlayerValue(hand.Player),
            dealer,
            BlackjackHelper.CalculateHandValue(dealer),
            finished,
            finished ? hand.Player.Result : GamePlayerResult.NoResult,
            credited,
            net,
            allowed);
    }

    private sealed class Seat
    {
        private readonly SemaphoreSlim _gate = new(1, 1);

        public OpenHand? Hand { get; set; }

        public async Task<T> RunAsync<T>(Func<Task<T>> work)
        {
            await _gate.WaitAsync();
            try
            {
                return await work();
            }
            finally
            {
                _gate.Release();
            }
        }

        public Task RunAsync(Func<Task> work) => RunAsync(async () =>
        {
            await work();
            return true;
        });
    }

    private sealed class OpenHand(Guid id, Blackjack game, GameSession<Blackjack> session, DiscordGamePlayer player, long reserved, DateTimeOffset startedAt)
    {
        public Guid Id { get; } = id;
        public Blackjack Game { get; } = game;
        public GameSession<Blackjack> Session { get; } = session;
        public DiscordGamePlayer Player { get; } = player;

        /// <summary>Tokens taken from the balance for this hand: the bet, doubled by a double down.</summary>
        public long Reserved { get; set; } = reserved;
        public DateTimeOffset LastActionAt { get; set; } = startedAt;
    }
}
