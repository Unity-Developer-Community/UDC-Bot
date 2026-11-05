using DiscordBot.Services.Server;

namespace DiscordBot.Tests.Services;

public class ReminderServiceTests
{
    private static readonly DateTime Now = new(2026, 1, 1, 12, 0, 0);

    [Fact]
    public void IsTooFarAway_ExactlyThirtyDays_IsAllowed()
    {
        Assert.False(ReminderService.IsTooFarAway(Now.AddDays(30), Now));
    }

    [Fact]
    public void IsTooFarAway_JustOverThirtyDays_IsRefused()
    {
        Assert.True(ReminderService.IsTooFarAway(Now.AddDays(30).AddSeconds(1), Now));
    }

    [Fact]
    public void IsTooFarAway_OneHour_IsAllowed()
    {
        Assert.False(ReminderService.IsTooFarAway(Now.AddHours(1), Now));
    }
}
