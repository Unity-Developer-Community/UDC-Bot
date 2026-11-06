using System.Net;
using System.Text;
using Discord;
using DiscordBot.Domain;
using DiscordBot.Extensions;
using DiscordBot.Services;
using DiscordBot.Services.Fun.Casino;
using DiscordBot.Services.Logging;

namespace CasinoWeb.Tests;

public sealed class FakeTimeProvider(DateTimeOffset start) : TimeProvider
{
    private DateTimeOffset _now = start;

    public override DateTimeOffset GetUtcNow() => _now;

    public void Advance(TimeSpan by) => _now += by;
}

/// <summary>Plays the parts of Discord's API the casino calls: token exchange, the user's profile and the guild member lookup.</summary>
public sealed class FakeDiscordHandler : HttpMessageHandler
{
    public const string UserId = "4242";
    public const string AccessToken = "access-token-of-4242";

    public HttpStatusCode MemberStatus { get; set; } = HttpStatusCode.OK;
    public int MemberLookups { get; private set; }
    public string? LastMemberLookupToken { get; private set; }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var path = request.RequestUri!.AbsolutePath;
        if (path.EndsWith("/oauth2/token", StringComparison.Ordinal))
        {
            return Json($$"""{"access_token":"{{AccessToken}}","token_type":"Bearer","expires_in":604800,"scope":"identify guilds.members.read"}""");
        }

        if (path.EndsWith("/users/@me", StringComparison.Ordinal))
        {
            return Json($$"""{"id":"{{UserId}}","username":"ada","global_name":"Ada Lovelace","avatar":"abc123","discriminator":"0"}""");
        }

        if (path.Contains("/guilds/", StringComparison.Ordinal) && path.EndsWith("/member", StringComparison.Ordinal))
        {
            MemberLookups++;
            LastMemberLookupToken = request.Headers.Authorization?.Parameter;
            return Task.FromResult(new HttpResponseMessage(MemberStatus) { Content = new StringContent("{}") });
        }

        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
    }

    private static Task<HttpResponseMessage> Json(string body) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
    {
        Content = new StringContent(body, Encoding.UTF8, "application/json"),
    });
}

/// <summary>An in-memory casino table: the web casino's tests do not need PostgreSQL.</summary>
public sealed class FakeCasinoDatabase : ICasinoDatabase
{
    public FakeCasinoRepo Repo { get; } = new();

    public ICasinoRepo? CasinoQuery => Repo;
}

public sealed class FakeCasinoRepo : ICasinoRepo
{
    public Dictionary<string, CasinoUser> Users { get; } = [];
    public List<TokenTransaction> Transactions { get; } = [];

    /// <summary>Makes every balance change fail, as when the database drops.</summary>
    public bool FailAdjust { get; set; }

    public Task<CasinoUser> InsertCasinoUser(CasinoUser user)
    {
        Users[user.UserID] = user;
        return Task.FromResult(user);
    }

    public Task<CasinoUser> GetCasinoUser(string userId) =>
        Task.FromResult(Users.GetValueOrDefault(userId)!);

    public Task<TokenTransaction> InsertTransaction(TokenTransaction tokenTransaction)
    {
        Transactions.Add(tokenTransaction);
        return Task.FromResult(tokenTransaction);
    }

    public Task<IList<CasinoUser>> GetTopTokenHolders(int limit) => throw new NotSupportedException();
    public Task UpdateTokens(string userId, long tokens, DateTime updatedAt) => throw new NotSupportedException();
    /// <summary>Like the SQL statement: the balance never goes below 0, and a missing user changes nothing.</summary>
    public Task<long?> AdjustTokens(string userId, long delta, DateTime updatedAt)
    {
        if (FailAdjust)
            throw new InvalidOperationException("The database is down.");
        if (!Users.TryGetValue(userId, out var user))
            return Task.FromResult<long?>(null);
        user.Tokens = Math.Max(0, user.Tokens + delta);
        return Task.FromResult<long?>(user.Tokens);
    }

    public Task<long?> TryDebitTokens(string userId, long amount, DateTime updatedAt)
    {
        if (!Users.TryGetValue(userId, out var user) || user.Tokens < amount)
            return Task.FromResult<long?>(null);
        user.Tokens -= amount;
        return Task.FromResult<long?>(user.Tokens);
    }
    public Task<long?> TransferTokens(string fromUserId, string toUserId, long amount, DateTime updatedAt) => throw new NotSupportedException();
    public Task<long?> ClaimDailyReward(string userId, long reward, DateTime now, DateTime eligibleBefore) => throw new NotSupportedException();
    public Task DeleteCasinoUser(string userId) => throw new NotSupportedException();
    public Task ClearAllCasinoUsers() => throw new NotSupportedException();
    public Task<IList<TokenTransaction>> GetUserTransactionHistory(string userId, int limit) => throw new NotSupportedException();
    public Task<IList<TokenTransaction>> GetRecentTransactions(int limit) => throw new NotSupportedException();
    public Task ClearAllTransactions() => throw new NotSupportedException();
    public Task<IList<TokenTransaction>> GetTransactionsOfType(string transactionType) => throw new NotSupportedException();
    public Task<CasinoJackpot?> GetJackpot() => throw new NotSupportedException();
    public Task<long> AddJackpotContribution(long contributionHundredths, DateTime updatedAt) => throw new NotSupportedException();
    public Task<long> ClaimJackpotShare(int bet, long floorHundredths, DateTime updatedAt) => throw new NotSupportedException();
    public Task<long> TestCasinoConnection() => throw new NotSupportedException();
}

/// <summary>The slots' balance, jackpot and transactions in memory, so a hub test sees exactly what a spin settled.</summary>
public sealed class FakeSlotsLedger : ISlotsLedger
{
    public long Tokens { get; set; } = 1_000;
    public long Pot { get; set; } = SlotSettlementMath.JackpotFloorHundredths;

    /// <summary>One entry per settlement: the recorded net amount and the transaction details.</summary>
    public List<(long Net, Dictionary<string, string> Details)> Settlements { get; } = [];

    public Task<long> GetTokens(string userId) => Task.FromResult(Tokens);

    public Task<bool> Reserve(string userId, long bet)
    {
        if (Tokens < bet)
            return Task.FromResult(false);
        Tokens -= bet;
        return Task.FromResult(true);
    }

    public Task Release(string userId, long bet)
    {
        Tokens += bet;
        return Task.CompletedTask;
    }

    public Task<long> Settle(string userId, long payout, long net, Dictionary<string, string> details)
    {
        Tokens += payout;
        Settlements.Add((net, details));
        return Task.FromResult(Tokens);
    }

    public Task<long> GetPot() => Task.FromResult(Pot);

    public Task<long> AddContribution(long hundredths) => Task.FromResult(Pot += hundredths);

    public Task<long> ClaimShare(int bet)
    {
        var share = SlotSettlementMath.JackpotShare(Pot, bet);
        Pot = SlotSettlementMath.PotAfterClaim(Pot, share);
        return Task.FromResult(share);
    }
}

/// <summary>Hands out the scripted values in order, so a test chooses the reel stops and the hold roll.</summary>
public sealed class ScriptedSlotRandom : ISlotRandom
{
    private readonly Queue<int> _values = new();

    public void Enqueue(params int[] values)
    {
        foreach (var value in values)
            _values.Enqueue(value);
    }

    public int Next(int maxExclusive)
    {
        var value = _values.Dequeue();
        Assert.InRange(value, 0, maxExclusive - 1);
        return value;
    }
}

/// <summary>A blackjack dealt from a fixed order of cards: player, dealer, player, dealer, then the draws in order.</summary>
public sealed class RiggedBlackjack(params Card[] order) : Blackjack
{
    protected override void InitializeGame()
    {
        base.InitializeGame();
        Deck = new Deck(order);
        foreach (var player in Players)
            GameData[player].PlayerCards.Clear();
        DealerCards.Clear();
        for (var i = 0; i < 2; i++)
        {
            foreach (var player in Players)
                GameData[player].PlayerCards.Add(Deck.DrawCard()!);
            DealerCards.Add(Deck.DrawCard()!);
        }
    }

    /// <summary>Spades of the given values: 1 is an ace, 11–13 are faces.</summary>
    public static Card[] Cards(params int[] values) => values.Select(v => new Card(v, CardSuit.Spades)).ToArray();
}

/// <summary>Keeps the log lines, so a test can check the audit trail.</summary>
public sealed class RecordingLoggingService : ILoggingService
{
    public List<(ExtendedLogSeverity Severity, string Message)> Lines { get; } = [];

    public void LogXp(string channel, string user, float baseXp, float bonusXp, float xpReduce, int totalXp) { }

    public Task Log(LogBehaviour behaviour, string message, ExtendedLogSeverity severity = ExtendedLogSeverity.Info, Embed? embed = null)
    {
        lock (Lines)
            Lines.Add((severity, message));
        return Task.CompletedTask;
    }

    public Task<IUserMessage?> LogComponentsToChannel(MessageComponent components, IReadOnlyCollection<FileAttachment>? files = null) => Task.FromResult<IUserMessage?>(null);
}
