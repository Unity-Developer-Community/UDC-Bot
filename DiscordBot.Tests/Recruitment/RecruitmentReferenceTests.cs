using DiscordBot.Services.Recruitment;

namespace DiscordBot.Tests.Recruitment;

public class RecruitmentReferenceTests
{
    [Fact]
    public async Task StartupCreatesOneLockedPinnedReferencePerForumAndRestartReusesThem()
    {
        using var rig = new RecruitmentRig(); await rig.Start();
        Assert.Equal(4, rig.Discord.ReferenceCreates); Assert.Empty(rig.State.Listings);
        var ids = rig.State.Forums.Values.Select(f => f.Reference.ThreadId).ToArray();
        foreach (var id in ids)
        {
            var reference = rig.Discord.Threads[id!];
            Assert.True(reference.Pinned && reference.Locked && reference.Archived);
            Assert.Contains("information below", reference.Starter);
            Assert.DoesNotContain(rig.State.RotationCode, reference.Starter);
        }
        var checks = rig.Discord.ReferenceChecks;
        await rig.Sweep(); Assert.Equal(checks, rig.Discord.ReferenceChecks);
        await rig.Restart(); Assert.Equal(4, rig.Discord.ReferenceCreates);
        Assert.Equal(ids, rig.State.Forums.Values.Select(f => f.Reference.ThreadId));
        Assert.All(rig.Engine.Health().Forums, f => Assert.NotNull(f.ReferenceId));
    }

    [Fact]
    public async Task ExistingPinnedStaffReferenceIsReusedWithoutReplacingItsContent()
    {
        using var rig = new RecruitmentRig();
        rig.Discord.Threads["100"] = new ThreadSnapshot("100", "11", "900", "Information", rig.Clock.Now,
            false, true, false, false, Array.Empty<string>(), "Moderator-maintained rules", rig.Clock.Now.AddYears(-1), rig.Clock.Now, OwnerIsStaff: true);
        await rig.Start();
        Assert.Equal("100", rig.State.Forums["paid-recruiting"].Reference.ThreadId);
        Assert.False(rig.State.Forums["paid-recruiting"].Reference.BotOwned);
        Assert.Equal("Moderator-maintained rules", rig.Discord.Threads["100"].Starter);
        Assert.Equal(3, rig.Discord.ReferenceCreates); Assert.Empty(rig.State.Listings);
    }

    [Fact]
    public async Task LostCreationResponseRecoversExistingThreadAcrossRestart()
    {
        using var rig = new RecruitmentRig(); rig.Discord.LoseNextReferenceResponse = true;
        await rig.Start();
        var receipt = rig.State.Forums["paid-recruiting"].Reference;
        Assert.True(receipt.CreatePending); Assert.Null(receipt.ThreadId); Assert.NotNull(receipt.Fault);
        Assert.Equal(4, rig.Discord.ReferenceCreates);
        await rig.Restart();
        Assert.Equal(4, rig.Discord.ReferenceCreates);
        Assert.False(rig.State.Forums["paid-recruiting"].Reference.CreatePending);
        Assert.NotNull(rig.State.Forums["paid-recruiting"].Reference.ThreadId);
        Assert.Null(rig.State.Forums["paid-recruiting"].Reference.Fault);
    }

    [Fact]
    public async Task PinFailureRetainsIdentityAndDoesNotBlockMemberAcceptance()
    {
        using var rig = new RecruitmentRig(); rig.Discord.FailReferencePin = true; await rig.Start();
        Assert.All(rig.State.Forums.Values, f => { Assert.NotNull(f.Reference.ThreadId); Assert.NotNull(f.Reference.Fault); });
        Assert.Contains("reference held", rig.Engine.Health().Text());
        await rig.Post(); Assert.Contains("accepted", await rig.Ack());
        Assert.Equal(4, rig.Discord.ReferenceCreates);
        rig.Discord.FailReferencePin = false; await rig.Restart();
        Assert.Equal(4, rig.Discord.ReferenceCreates); Assert.All(rig.State.Forums.Values, f => Assert.Null(f.Reference.Fault));
    }

    [Fact]
    public async Task DeletedReferenceIsReplacedButAccessFailureNeverCreatesADuplicate()
    {
        using var rig = new RecruitmentRig(); await rig.Start();
        var id = rig.State.Forums["paid-recruiting"].Reference.ThreadId!;
        rig.Discord.UnknownThreads.Add(id); await rig.Restart();
        Assert.Equal(id, rig.State.Forums["paid-recruiting"].Reference.ThreadId); Assert.Equal(4, rig.Discord.ReferenceCreates);
        rig.Discord.UnknownThreads.Remove(id); rig.Discord.Threads.Remove(id);
        await rig.Engine.ThreadDeletedAsync(id, default); await rig.Engine.RunDueAsync(default);
        Assert.NotEqual(id, rig.State.Forums["paid-recruiting"].Reference.ThreadId);
        Assert.Equal(5, rig.Discord.ReferenceCreates); Assert.Equal(0, rig.Engine.Health().Open);
    }

    [Fact]
    public async Task IncompleteDiscoveryDefersReferenceCreation()
    {
        using var rig = new RecruitmentRig(); rig.Discord.InventoryComplete = false; await rig.Start();
        Assert.Equal(0, rig.Discord.ReferenceCreates);
        rig.Discord.InventoryComplete = true; await rig.Sweep();
        Assert.Equal(4, rig.Discord.ReferenceCreates);
    }

    [Fact]
    public async Task OverdueReferenceRetryDoesNotPollWhileForumSetupIsHeld()
    {
        using var rig = new RecruitmentRig(); rig.Discord.FailReferencePin = true; await rig.Start();
        rig.Clock.Advance(TimeSpan.FromMinutes(1));
        rig.Discord.Forums["11"] = rig.Discord.Forums["11"] with { Topic = "External Guidelines edit" };
        await rig.Engine.SweepAsync(default, retryHeld: false); await rig.Engine.RefreshOverviewAsync(default);
        Assert.NotNull(rig.State.Forums["paid-recruiting"].Fault);
        Assert.True(rig.Engine.NextDueAt > rig.Clock.Now);
        var reads = rig.Discord.ForumReads;
        await rig.Engine.RunDueAsync(default);
        Assert.Equal(reads, rig.Discord.ForumReads);
    }

    [Fact]
    public async Task RetainedStaffReferenceIsReusedEvenWhenNormalInventorySkipsItsKnownId()
    {
        using var rig = new RecruitmentRig(); await rig.Start(); var post = await rig.Post(); await rig.Staff("reference");
        rig.Discord.Threads[post.Id] = rig.Discord.Threads[post.Id] with { Pinned = true, OwnerIsStaff = true };
        var old = rig.State.Forums["paid-recruiting"].Reference.ThreadId!;
        rig.Discord.Threads.Remove(old); await rig.Engine.ThreadDeletedAsync(old, default);
        await rig.Restart();
        Assert.Equal(post.Id, rig.State.Forums["paid-recruiting"].Reference.ThreadId);
        Assert.Equal(4, rig.Discord.ReferenceCreates);
    }

    [Fact]
    public async Task MissingBotStarterDoesNotAuthorizeDuplicateReferenceAfterLostResponse()
    {
        using var rig = new RecruitmentRig(); rig.Discord.LoseNextReferenceResponse = true; await rig.Start();
        var reference = rig.Discord.Threads.Values.Single(t => t.ForumId == "11");
        rig.Discord.Threads[reference.Id] = reference with { Starter = null };
        await rig.Restart();
        Assert.Equal(4, rig.Discord.ReferenceCreates);
        Assert.True(rig.State.Forums["paid-recruiting"].Reference.CreatePending);
        rig.Discord.Threads[reference.Id] = reference;
        await rig.Sweep();
        Assert.Equal(reference.Id, rig.State.Forums["paid-recruiting"].Reference.ThreadId);
        Assert.Equal(4, rig.Discord.ReferenceCreates);
    }
}
