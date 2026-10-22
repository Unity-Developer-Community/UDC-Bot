using System.Net;
using System.Text;
using Discord.Net.Rest;
using DiscordBot.Services.Recruitment;
using DiscordBot.Services.Recruitment.Infrastructure.Discord;

namespace DiscordBot.Tests.Recruitment;

[Collection("Recruitment transport")]
public class RecruitmentRequestScopeTests
{
    [Fact]
    public async Task NestedScopeRestoresAfterExceptionAndOrdinaryTrafficKeepsItsBehavior()
    {
        Assert.False(RecruitmentRequests.IsScoped);
        using (RecruitmentRequests.BeginScope(new DiscordRetryDeadlines()))
        {
            Assert.True(RecruitmentRequests.IsScoped);
            await Assert.ThrowsAsync<RecruitmentDeferredException>(async () =>
            {
                using var nested = RecruitmentRequests.BeginScope();
                await Task.Yield();
                await RecruitmentRequests.SendAsync("GET", "channels/300", default, () => Task.FromResult(Limited()));
            });
            Assert.True(RecruitmentRequests.IsScoped);
        }
        Assert.False(RecruitmentRequests.IsScoped);
        var ordinary = await RecruitmentRequests.SendAsync("GET", "channels/301", default, () => Task.FromResult(Limited()));
        Assert.Equal(HttpStatusCode.TooManyRequests, ordinary.StatusCode);
        ordinary.Stream.Dispose();
    }

    [Fact]
    public async Task GlobalDeadlineIsSharedAcrossRoutesAndOrdinaryWaitUsesInjectedTime()
    {
        var clock = new ManualClock();
        var waits = new List<TimeSpan>();
        var deadlines = new DiscordRetryDeadlines(clock, (delay, _) => { waits.Add(delay); clock.Advance(delay); return Task.CompletedTask; });
        var start = clock.GetUtcNow();
        var error = await Assert.ThrowsAsync<RecruitmentDeferredException>(() => deadlines.SendAsync("GET", "channels/1", default,
            () => Task.FromResult(Limited(global: true)), recruitment: true));
        Assert.Equal(start.AddSeconds(6), error.RetryAt);
        var sent = false;
        await Assert.ThrowsAsync<RecruitmentDeferredException>(() => deadlines.SendAsync("GET", "guilds/9", default,
            () => { sent = true; return Task.FromResult(Limited()); }, recruitment: true));
        Assert.False(sent);
        var response = await deadlines.SendAsync("GET", "guilds/9", default,
            () => Task.FromResult(new RestResponse(HttpStatusCode.OK, new Dictionary<string, string>(), new MemoryStream())), recruitment: false);
        Assert.Equal(TimeSpan.FromSeconds(6), Assert.Single(waits));
        response.Stream.Dispose();
    }

    [Theory]
    [InlineData(1, 1, 16)]
    [InlineData(4, 3, 123)]
    [InlineData(10, 5, 905)]
    public void BackoffIsCappedAndHasExplicitJitter(int attempts, int jitter, double seconds) =>
        Assert.Equal(seconds, RecruitmentBackoff.Seconds(attempts, jitter));

    private static RestResponse Limited(bool global = false) => new(HttpStatusCode.TooManyRequests, new Dictionary<string, string>(),
        new MemoryStream(Encoding.UTF8.GetBytes($"{{\"retry_after\":5,\"global\":{global.ToString().ToLowerInvariant()}}}")));
}
