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

    [Fact]
    public void PendingLine_NoReminders()
    {
        Assert.Equal("No pending reminders.", ReminderService.PendingLine([]));
    }

    [Fact]
    public void PendingLine_ShowsCountAndSoonest()
    {
        var soonest = Now.AddHours(1);
        var line = ReminderService.PendingLine([new ReminderItem { When = Now.AddDays(2) }, new ReminderItem { When = soonest }]);
        Assert.Equal($"2 pending, next due {AuditLogCards.Timestamp(new DateTimeOffset(soonest), 'R')}.", line);
    }
}
