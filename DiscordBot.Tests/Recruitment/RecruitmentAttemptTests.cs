using DiscordBot.Services.Recruitment;

namespace DiscordBot.Tests.Recruitment;

public class RecruitmentAttemptTests
{
    [Fact]
    public async Task DeletedCreationsCountAcrossForumsAndRestartWithoutExtendingPause()
    {
        using var rig = new RecruitmentRig(); await rig.Start();
        for (var i = 0; i < 2; i++)
        {
            var post = await rig.Post((100 + i).ToString(), forum: (11 + i).ToString());
            rig.Discord.Threads.Remove(post.Id); await rig.Engine.ThreadChangedAsync(post.Id, default);
        }
        await rig.Restart();
        var third = await rig.Post("102", forum: "13");
        Assert.Equal(ListingStatus.Closed, third.Status);
        Assert.Null(third.PromptDeliveredAt); Assert.Null(rig.State.Authors["200"].LastAcceptedCreation);
        var pause = rig.State.Authors["200"].AttemptPausedUntil;
        Assert.Equal(rig.Clock.Now.AddHours(1), pause);
        rig.Clock.Advance(TimeSpan.FromMinutes(10));
        var fourth = await rig.Post("103", forum: "14");
        Assert.Equal(ListingStatus.Closed, fourth.Status); Assert.Equal(pause, fourth.AttemptPausedUntil);
        await rig.Restart();
        Assert.Equal(pause, rig.State.Authors["200"].AttemptPausedUntil);
        rig.Clock.Advance(TimeSpan.FromMinutes(50));
        await rig.Post("104"); Assert.Contains("accepted", await rig.Ack("104"));
    }

    [Fact]
    public async Task RediscoveryDoesNotCountRetriesOrOldHistory()
    {
        using var rig = new RecruitmentRig(); await rig.Start();
        await rig.Post(created: rig.Clock.Now.AddDays(-10));
        Assert.Empty(rig.State.Authors["200"].RecentCreations);
        await rig.Post("101", owner: "201");
        await rig.Engine.ObserveAsync(rig.Discord.Threads["101"], true, default);
        await rig.Sweep(); await rig.Restart();
        Assert.Single(rig.State.Authors["201"].RecentCreations);
    }

    [Fact]
    public async Task EarlierPendingAttemptCanStillAcceptAfterLaterAttemptsTriggerPause()
    {
        using var rig = new RecruitmentRig(); await rig.Start(); await rig.Post();
        await rig.Post("101"); await rig.Post("102");
        Assert.Equal(ListingStatus.Closed, rig.State.Listings["101"].Status);
        Assert.NotNull(rig.State.Authors["200"].AttemptPausedUntil);
        Assert.Contains("accepted", await rig.Ack());
        Assert.NotNull(rig.State.Listings["100"].AcceptedAt);
    }

    [Fact]
    public async Task ModeratorResetIsAuditedAndDoesNotWaiveAcceptedCooldownOrReopenAttempts()
    {
        using var rig = new RecruitmentRig(); await rig.Start(); await rig.Post(); await rig.Ack();
        await rig.Post("101"); var third = await rig.Post("102");
        Assert.Contains("Moderator access", await rig.Staff("reset-attempts", "102", actor: "201"));
        Assert.NotNull(rig.State.Authors["200"].AttemptPausedUntil);
        await rig.Staff("reset-attempts", "102"); await rig.Restart();
        var author = rig.State.Authors["200"];
        Assert.Null(author.AttemptPausedUntil); Assert.Empty(author.RecentCreations);
        Assert.NotNull(author.LastAcceptedCreation); Assert.Equal(ListingStatus.Closed, rig.State.Listings[third.Id].Status);
        Assert.Contains(rig.State.Listings[third.Id].Audit, a => a.Reason.Contains("reset-attempts"));
    }

    [Fact]
    public async Task PrivateEligibilityChecksMembershipAndExplainsExistingListing()
    {
        using var rig = new RecruitmentRig(); await rig.Start(); await rig.Post(); await rig.Ack();
        var result = await rig.Engine.EligibilityAsync("1", "200", default);
        Assert.Contains("<#100>", result); Assert.Contains("conversation history", result); Assert.Contains("cooldown", result);
        Assert.DoesNotContain("100", await rig.Engine.EligibilityAsync("2", "200", default));
        rig.Discord.MissingMembers.Add("200");
        Assert.Contains("current members", await rig.Engine.EligibilityAsync("1", "200", default));
        Assert.Contains("no recorded listing restrictions", await rig.Engine.EligibilityAsync("1", "201", default));
    }
}
