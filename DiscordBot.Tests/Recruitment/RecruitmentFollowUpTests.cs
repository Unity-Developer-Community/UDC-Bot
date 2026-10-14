using DiscordBot.Services.Recruitment;

namespace DiscordBot.Tests.Recruitment;

public class RecruitmentFollowUpTests
{
    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task StaffAndBotPostsNeverEnterMemberWorkflow(bool staff, bool bot)
    {
        using var rig = new RecruitmentRig(); await rig.Start();
        rig.Discord.Threads["100"] = Thread(rig) with { OwnerIsStaff = staff, OwnerIsBot = bot };
        await rig.Engine.ThreadChangedAsync("100", default); await rig.Sweep();
        Assert.Empty(rig.State.Listings);
        Assert.DoesNotContain(rig.Discord.Messages.Keys, k => k.StartsWith("100:"));
        Assert.Empty(rig.Discord.Actions);
    }

    [Fact]
    public async Task ExistingStaffRecordLosesControlsAndSlotWithoutChangingItsContent()
    {
        using var rig = new RecruitmentRig(); await rig.Start(); var post = await rig.Post();
        rig.Discord.Threads[post.Id] = rig.Discord.Threads[post.Id] with { OwnerIsStaff = true };
        await rig.Engine.ThreadChangedAsync(post.Id, default);
        Assert.Equal(ListingStatus.Reference, post.Status); Assert.Null(post.PublicMessageId);
        Assert.Null(post.Deadline); Assert.Equal(0, rig.Engine.Health().Open);
        Assert.Equal("Budget AUD 500 for a short Unity project", rig.Discord.Threads[post.Id].Starter);
        Assert.Empty(rig.Discord.Actions);
    }

    [Fact]
    public async Task ConfirmedDeletionClearsHeldCountsEvenWhileSetupIsUnavailable()
    {
        using var rig = new RecruitmentRig(); await rig.Start(); var post = await rig.Post(); await rig.Ack();
        post.Fault = new RecruitmentFault("Locked advisory", null); post.Hold = post.Fault.Message;
        post.AvailabilityUnknown = true;
        rig.Engine.InvalidateAccess(); rig.Discord.FailPermissions = true; await rig.Sweep();
        rig.Discord.Threads.Remove(post.Id);
        await rig.Engine.ThreadDeletedAsync(post.Id, default);
        Assert.Equal(ListingStatus.Deleted, post.Status); Assert.Null(post.Fault); Assert.Null(post.Hold);
        Assert.False(post.AvailabilityUnknown); Assert.Equal(0, rig.Engine.Health().Open); Assert.Equal(0, rig.Engine.Health().Held);
        Assert.NotNull(rig.State.Authors[post.OwnerId].LastAcceptedCreation);
    }

    [Fact]
    public async Task SafetyAuditFindsDeletedPermanentlyHeldListingWithoutRetryingItsWrite()
    {
        using var rig = new RecruitmentRig(); await rig.Start(); var post = await rig.Post();
        post.Fault = new RecruitmentFault("Locked advisory", null); post.Hold = post.Fault.Message;
        rig.Discord.Threads.Remove(post.Id);
        await rig.Engine.SweepAsync(default, retryHeld: false);
        Assert.Equal(ListingStatus.Deleted, post.Status); Assert.Null(post.Fault); Assert.Null(post.Hold);
        Assert.Empty(rig.Discord.Actions);
    }

    [Fact]
    public async Task ClosedTagEventClosesOnceInsteadOfCancellingItsOwnAction()
    {
        using var rig = new RecruitmentRig();
        var service = new RecruitmentService(rig.Settings, rig.Discord, rig.Clock);
        try
        {
            await service.StartAsync();
            var tag = await service.RunAsync((engine, _) => Task.FromResult(engine.State!.Forums["paid-recruiting"].Tags["closed"].Id));
            rig.Discord.Threads["100"] = Thread(rig) with { Tags = new[] { tag } };
            service.NotifyThread("100");
            await service.RunAsync((_, _) => Task.FromResult(true));
            service.NotifyThread("100");
            var post = await service.RunAsync((engine, _) => Task.FromResult(engine.State!.Listings["100"]));
            Assert.Equal(ListingStatus.Closed, post.Status);
            Assert.Single(rig.Discord.Actions, a => a == ("100", "close"));
            Assert.Single(post.Audit, a => a.Reason == "Closed tag applied");
            rig.Discord.Threads.Remove("100"); service.NotifyThreadDeletion("100");
            await service.RunAsync((_, _) => Task.FromResult(true));
            Assert.Equal(ListingStatus.Deleted, post.Status); Assert.Equal(0, service.Health.Open);
        }
        finally { await service.StopAsync(default); }
    }

    private static ThreadSnapshot Thread(RecruitmentRig rig) => new("100", "11", "200", "Reference", rig.Clock.Now,
        false, false, false, false, Array.Empty<string>(), "Read the information below", rig.Clock.Now.AddYears(-2), rig.Clock.Now.AddMonths(-3));
}
