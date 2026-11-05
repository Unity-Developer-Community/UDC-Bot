using System.Net;
using System.Text;
using DiscordBot.Domain;
using DiscordBot.Extensions;
using DiscordBot.Services;

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
    public Task<long?> AdjustTokens(string userId, long delta, DateTime updatedAt) => throw new NotSupportedException();
    public Task<long?> TryDebitTokens(string userId, long amount, DateTime updatedAt) => throw new NotSupportedException();
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
