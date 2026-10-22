using DiscordBot.Services.Recruitment;
using DiscordBot.Services.Recruitment.Domain.Policies;

namespace DiscordBot.Tests.Recruitment;

public class AutomaticActionPolicyTests
{
    [Theory]
    [InlineData(false, false, true)]
    [InlineData(true, true, true)]
    [InlineData(true, false, false)]
    public void TimeoutRequiresCurrentEvidenceAndRecoveredWindow(bool current, bool recovering, bool cancelled)
    {
        var now = DateTimeOffset.UnixEpoch.AddDays(1);
        var post = new Listing { Deadline = now.AddMinutes(-1) };
        var action = new RecruitmentAction { Automatic = true, Condition = RecruitmentConditions.Timeout };
        Assert.Equal(cancelled, AutomaticActionPolicy.ShouldCancel(new(), post, action, now, 30, current, recovering, false));
        post.AcknowledgedAt = now.AddMinutes(-2);
        Assert.True(AutomaticActionPolicy.ShouldCancel(new(), post, action, now, 30, current, recovering, false));
        action.Automatic = false;
        Assert.False(AutomaticActionPolicy.ShouldCancel(new(), post, action, now, 30, false, true, false));
    }
}
