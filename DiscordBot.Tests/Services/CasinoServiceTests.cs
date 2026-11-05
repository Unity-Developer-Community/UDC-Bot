using Discord;
using DiscordBot.Domain;
using DiscordBot.Extensions;
using DiscordBot.Services;
using DiscordBot.Services.Fun.Casino;
using DiscordBot.Services.Logging;
using DiscordBot.Settings;
using Testcontainers.PostgreSql;

namespace DiscordBot.Tests.Services;

/// <summary>A throwaway PostgreSQL with the casino tables, created by the same code the bot runs at startup.</summary>
public sealed class CasinoDatabaseFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:16-alpine").Build();

    public BotSettings Settings { get; private set; } = new();
    public DatabaseService Database { get; private set; } = null!;
    public CasinoService Casino { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        await _container.StartAsync();

        Settings = new BotSettings { DbConnectionString = _container.GetConnectionString() };
        Database = new DatabaseService(new QuietLogging(), Settings);
        Casino = new CasinoService(Database, new QuietLogging(), Settings);

        // DatabaseService creates the tables on a background task.
        var deadline = DateTime.UtcNow.AddSeconds(60);
        while (true)
        {
            try
            {
                await Database.CasinoQuery!.TestCasinoConnection();
                await Database.CasinoQuery!.GetRecentTransactions(1); // created right after the users table
                return;
            }
            catch when (DateTime.UtcNow < deadline)
            {
                await Task.Delay(200);
            }
        }
    }

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();

    private sealed class QuietLogging : ILoggingService
    {
        public void LogXp(string channel, string user, float baseXp, float bonusXp, float xpReduce, int totalXp) { }
        public Task Log(LogBehaviour behaviour, string message, ExtendedLogSeverity severity = ExtendedLogSeverity.Info, Embed? embed = null) => Task.CompletedTask;
    }
}

/// <summary>Needs Docker. Each test uses its own users, so the tests share one database.</summary>
public class CasinoServiceTests(CasinoDatabaseFixture db) : IClassFixture<CasinoDatabaseFixture>
{
    private CasinoService Casino => db.Casino;
    private ICasinoRepo Repo => db.Database.CasinoQuery!;

    private async Task<string> NewUser(long tokens)
    {
        var id = Guid.NewGuid().ToString("N");
        await Casino.GetOrCreateCasinoUser(id);
        await Casino.SetUserTokens(id, tokens, "admin");
        return id;
    }

    private async Task<long> Balance(string id) => (await Casino.GetOrCreateCasinoUser(id)).Tokens;

    [Fact]
    public async Task ConcurrentChanges_AreAllApplied()
    {
        var user = await NewUser(1_000);

        var changes = Enumerable.Range(0, 50).Select(i => i % 2 == 0 ? 10L : -4L).ToList();
        await Task.WhenAll(changes.Select(delta => Casino.UpdateUserTokens(user, delta, TransactionKind.Game)));

        Assert.Equal(1_000 + changes.Sum(), await Balance(user));
    }

    [Fact]
    public async Task AChangeBelowZero_StopsAtZero()
    {
        var user = await NewUser(30);

        await Casino.UpdateUserTokens(user, -50, TransactionKind.Game);

        Assert.Equal(0, await Balance(user));
    }

    [Fact]
    public async Task TwoDebitsOfTheWholeBalance_OnlyOneSucceeds()
    {
        var user = await NewUser(100);

        var results = await Task.WhenAll(
            Repo.TryDebitTokens(user, 100, DateTime.UtcNow),
            Repo.TryDebitTokens(user, 100, DateTime.UtcNow));

        Assert.Single(results, r => r is not null);
        Assert.Equal(0, await Balance(user));
    }

    [Fact]
    public async Task TwoReservationsOfTheWholeBalance_OnlyOneSucceeds()
    {
        var user = await NewUser(100);

        var results = await Task.WhenAll(Casino.TryReserveTokens(user, 100), Casino.TryReserveTokens(user, 100));

        Assert.Single(results, r => r);
        Assert.Equal(0, await Balance(user));
    }

    [Fact]
    public async Task AReleasedReservation_GivesTheTokensBack_WithoutAHistoryRow()
    {
        var user = await NewUser(100);
        var rowsBefore = (await Casino.GetUserTransactionHistory(user, 50)).Count;

        Assert.True(await Casino.TryReserveTokens(user, 40));
        Assert.Equal(60, await Balance(user));
        await Casino.ReleaseTokens(user, 40);

        Assert.Equal(100, await Balance(user));
        Assert.Equal(rowsBefore, (await Casino.GetUserTransactionHistory(user, 50)).Count);
    }

    [Fact]
    public async Task SettlingAReservedBet_ChangesTheBalanceByThePayoutAndRecordsTheNet()
    {
        var user = await NewUser(100);
        Assert.True(await Casino.TryReserveTokens(user, 10));

        var balance = await Casino.UpdateUserTokens(user, 25, TransactionKind.Game, recordedAmount: 15);

        Assert.Equal(115, balance);
        Assert.Equal(15, (await Casino.GetUserTransactionHistory(user, 1)).Single().Amount);
    }

    [Fact]
    public async Task TwoGiftsOfTheWholeBalance_OnlyOneSucceeds()
    {
        var sender = await NewUser(100);
        var firstReceiver = await NewUser(0);
        var secondReceiver = await NewUser(0);

        var results = await Task.WhenAll(
            Casino.TransferTokens(sender, firstReceiver, 100),
            Casino.TransferTokens(sender, secondReceiver, 100));

        Assert.Single(results, r => r);
        Assert.Equal(0, await Balance(sender));
        Assert.Equal(100, await Balance(firstReceiver) + await Balance(secondReceiver));
    }

    [Fact]
    public async Task MutualGiftsAtTheSameTime_NeitherBlocksTheOther()
    {
        var first = await NewUser(10_000);
        var second = await NewUser(10_000);

        // Opposite directions lock the same two rows in opposite orders unless the statement fixes the order.
        var gifts = Enumerable.Range(0, 40).Select(i => i % 2 == 0
            ? Casino.TransferTokens(first, second, 3)
            : Casino.TransferTokens(second, first, 1));
        var results = await Task.WhenAll(gifts);

        Assert.All(results, Assert.True);
        Assert.Equal(10_000 - 20 * 3 + 20 * 1, await Balance(first));
        Assert.Equal(10_000 + 20 * 3 - 20 * 1, await Balance(second));
    }

    [Fact]
    public async Task GiftsFromManySendersToOneReceiver_AreAllCredited()
    {
        var receiver = await NewUser(0);
        var senders = new List<string>();
        for (var i = 0; i < 20; i++) senders.Add(await NewUser(10));

        var results = await Task.WhenAll(senders.Select(s => Casino.TransferTokens(s, receiver, 10)));

        Assert.All(results, Assert.True);
        Assert.Equal(200, await Balance(receiver));
    }

    [Fact]
    public async Task ADailyClaim_PaysExactlyWhenTheIntervalHasElapsed()
    {
        var user = await NewUser(0);
        var last = (await Casino.GetOrCreateCasinoUser(user)).LastDailyReward;

        Assert.Null(await Repo.ClaimDailyReward(user, 100, DateTime.UtcNow, last.AddTicks(-10)));
        Assert.Equal(100, await Repo.ClaimDailyReward(user, 100, DateTime.UtcNow, last));
    }

    [Fact]
    public async Task AGift_MovesTokensFromSenderToReceiver()
    {
        var sender = await NewUser(100);
        var receiver = await NewUser(5);

        Assert.True(await Casino.TransferTokens(sender, receiver, 30));

        Assert.Equal(70, await Balance(sender));
        Assert.Equal(35, await Balance(receiver));
    }

    [Fact]
    public async Task AGiftBeyondTheBalance_ChangesNothing()
    {
        var sender = await NewUser(10);
        var receiver = await NewUser(5);

        Assert.False(await Casino.TransferTokens(sender, receiver, 11));

        Assert.Equal(10, await Balance(sender));
        Assert.Equal(5, await Balance(receiver));
    }

    [Fact]
    public async Task AGiftToYourself_ChangesNothing()
    {
        var user = await NewUser(10);

        Assert.False(await Casino.TransferTokens(user, user, 5));

        Assert.Equal(10, await Balance(user));
    }

    [Fact]
    public async Task ConcurrentDailyClaims_PayOnce()
    {
        var user = await NewUser(0);

        var results = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => Casino.TryClaimDailyReward(user)));

        Assert.Single(results, r => r.success);
        Assert.Equal(db.Settings.Casino.DailyRewardTokens, await Balance(user));
        Assert.All(results.Where(r => !r.success), r => Assert.True(r.nextRewardTime > DateTime.UtcNow));
    }

    [Fact]
    public async Task ADailyClaimBeforeTheInterval_PaysNothing()
    {
        var user = await NewUser(0);
        Assert.True((await Casino.TryClaimDailyReward(user)).success);

        var second = await Casino.TryClaimDailyReward(user);

        Assert.False(second.success);
        Assert.Equal(db.Settings.Casino.DailyRewardTokens, second.newBalance);
    }
}
