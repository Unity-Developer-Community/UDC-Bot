using DiscordBot.Services.Recruitment;
using DiscordBot.Services.Recruitment.Domain.Policies;

namespace DiscordBot.Tests.Recruitment;

public class RecruitmentDuePolicyTests
{
    [Fact]
    public void RecoveryHoldSuppressesExpiredChildRetries()
    {
        var now = DateTimeOffset.UnixEpoch.AddDays(1);
        var state = new RecruitmentState { RecoveryFault = new("Review required", null) };
        state.Forums.Add("forum", new ForumReceipt { Fault = new("Retry", now.AddMinutes(-1)) });
        Assert.Null(RecruitmentSchedule.Next(state, now, DateTimeOffset.MaxValue, false, false, 30, new()));
        state.RecoveryFault = null;
        var due = RecruitmentSchedule.Next(state, now, DateTimeOffset.MaxValue, false, false, 30, new());
        Assert.Equal(RecruitmentDueReason.ForumRetry, due!.Reason);
        Assert.Equal(now.AddMinutes(-1), due.At);
    }

    [Fact]
    public void HeldOptionalPinDoesNotCreateImmediatePolling()
    {
        var now = DateTimeOffset.UnixEpoch.AddDays(1);
        var post = new Listing { Status = ListingStatus.Closed, PublicMessageId = "101",
            PublicPin = new MessagePinReceipt { MessageId = "101", Fault = new("Permission required", null) } };
        Assert.Null(RecruitmentSchedule.Listing(new(), post, now, 30, false));
        post.PublicPin.Fault = new("Retry", now.AddMinutes(1));
        var due = RecruitmentSchedule.Listing(new(), post, now, 30, false);
        Assert.Equal(RecruitmentDueReason.Pin, due!.Reason);
        Assert.Equal(now.AddMinutes(1), due.At);
    }

    [Fact]
    public async Task AuditDeadlineUsesExplicitJitterAndInjectedClock()
    {
        using var rig = new RecruitmentRig();
        using var engine = new RecruitmentEngine(rig.Settings, rig.Discord, rig.Clock, auditJitter: () => 0.5);
        await engine.StartAsync(default);
        Assert.Equal(rig.Clock.Now.AddMinutes(16.5), engine.Health().NextDue);
    }
}
