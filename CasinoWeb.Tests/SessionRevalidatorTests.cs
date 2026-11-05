using CasinoWeb.Auth;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging.Abstractions;

namespace CasinoWeb.Tests;

public class SessionRevalidatorTests
{
    private sealed class FakeChecker : IGuildMembershipChecker
    {
        public MembershipStatus Answer { get; set; } = MembershipStatus.Member;
        public int Calls { get; private set; }

        public Task<MembershipStatus> CheckAsync(string accessToken, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(Answer);
        }
    }

    private readonly FakeChecker _checker = new();
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 11, 5, 12, 0, 0, TimeSpan.Zero));
    private readonly AuthenticationProperties _properties = new();
    private readonly SessionRevalidator _revalidator;

    public SessionRevalidatorTests()
    {
        _revalidator = new SessionRevalidator(_checker, _clock, NullLogger<SessionRevalidator>.Instance);
        _properties.StoreTokens([new AuthenticationToken { Name = SessionRevalidator.AccessTokenName, Value = "token" }]);
        SessionRevalidator.MarkChecked(_properties, _clock.GetUtcNow());
    }

    [Fact]
    public async Task Within_ten_minutes_discord_is_not_asked()
    {
        _clock.Advance(TimeSpan.FromMinutes(9) + TimeSpan.FromSeconds(59));

        Assert.Equal(SessionCheck.Fresh, await _revalidator.CheckAsync(_properties, default));
        Assert.Equal(0, _checker.Calls);
    }

    [Fact]
    public async Task After_ten_minutes_a_member_stays_and_the_clock_restarts()
    {
        _clock.Advance(SessionRevalidator.Interval);

        Assert.Equal(SessionCheck.Renewed, await _revalidator.CheckAsync(_properties, default));
        Assert.Equal(1, _checker.Calls);

        _clock.Advance(TimeSpan.FromMinutes(9));
        Assert.Equal(SessionCheck.Fresh, await _revalidator.CheckAsync(_properties, default));
        Assert.Equal(1, _checker.Calls);
    }

    [Fact]
    public async Task After_ten_minutes_someone_who_left_the_guild_is_revoked()
    {
        _checker.Answer = MembershipStatus.NotMember;
        _clock.Advance(SessionRevalidator.Interval);

        Assert.Equal(SessionCheck.Revoked, await _revalidator.CheckAsync(_properties, default));
    }

    [Fact]
    public async Task When_discord_is_down_the_session_stays_and_is_asked_again_after_a_minute()
    {
        _checker.Answer = MembershipStatus.Unavailable;
        _clock.Advance(SessionRevalidator.Interval);
        Assert.Equal(SessionCheck.Renewed, await _revalidator.CheckAsync(_properties, default));

        _clock.Advance(TimeSpan.FromSeconds(59));
        Assert.Equal(SessionCheck.Fresh, await _revalidator.CheckAsync(_properties, default));

        _checker.Answer = MembershipStatus.NotMember;
        _clock.Advance(TimeSpan.FromSeconds(2));
        Assert.Equal(SessionCheck.Revoked, await _revalidator.CheckAsync(_properties, default));
    }

    [Fact]
    public async Task A_session_without_an_access_token_is_revoked()
    {
        var properties = new AuthenticationProperties();

        Assert.Equal(SessionCheck.Revoked, await _revalidator.CheckAsync(properties, default));
        Assert.Equal(0, _checker.Calls);
    }
}
