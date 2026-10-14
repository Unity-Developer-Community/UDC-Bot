using DiscordBot.Services.Recruitment;

namespace DiscordBot.Tests.Recruitment;

public class RecruitmentSchedulingTests
{
    [Fact]
    public async Task AcknowledgementAndUnchangedThreadEventDoNotScanForumsOrRewriteMessages()
    {
        using var rig = new RecruitmentRig(); await rig.Start(); var post = await rig.Post();
        var forums = rig.Discord.ForumReads; var inventories = rig.Discord.Inventories;
        Assert.Contains("accepted", await rig.Ack());
        Assert.Equal(forums, rig.Discord.ForumReads); Assert.Equal(inventories, rig.Discord.Inventories);
        var publications = rig.Discord.Upserts; var history = rig.Discord.ReplyReads;
        await rig.Engine.ThreadChangedAsync(post.Id, default);
        Assert.Equal(publications, rig.Discord.Upserts); Assert.Equal(history, rig.Discord.ReplyReads);
    }

    [Fact]
    public async Task PositiveReplyEvidenceDoesNotNeedFurtherHistoryScans()
    {
        using var rig = new RecruitmentRig(); await rig.Start(); var post = await rig.Post(); await rig.Ack();
        rig.Engine.ObserveReply(new ReplyObservation(post.Id, "201", false, false, false));
        var reads = rig.Discord.ReplyReads;
        await rig.Sweep(); await rig.Engine.ThreadChangedAsync(post.Id, default);
        Assert.Equal(reads, rig.Discord.ReplyReads);
    }

    [Fact]
    public async Task DeadlineRunsWithoutInventoryOrSetupAndCooldownWakeupAcceptsWaitingListing()
    {
        using var rig = new RecruitmentRig(); await rig.Start(); var first = await rig.Post(); await rig.Ack(); await rig.Staff("close");
        rig.Clock.Now = first.CreatedAt.AddDays(rig.Settings.Recruitment.CooldownDays).AddMinutes(-2);
        var waiting = await rig.Post("101"); await rig.Ack("101");
        Assert.Null(waiting.AcceptedAt);
        var forums = rig.Discord.ForumReads; var inventories = rig.Discord.Inventories;
        rig.Clock.Advance(TimeSpan.FromMinutes(2)); await rig.Engine.RunDueAsync(default);
        Assert.NotNull(waiting.AcceptedAt);
        Assert.Equal(forums, rig.Discord.ForumReads); Assert.Equal(inventories, rig.Discord.Inventories);
    }

    [Fact]
    public async Task ConfirmedBlockerDeletionWakesOtherAttemptBySameAuthor()
    {
        using var rig = new RecruitmentRig(); await rig.Start(); await rig.Post(); await rig.Ack();
        await rig.Staff("waive-cooldown", "200"); var waiting = await rig.Post("101"); await rig.Ack("101");
        Assert.Null(waiting.AcceptedAt);
        rig.Discord.Threads.Remove("100");
        await rig.Engine.ThreadChangedAsync("100", default);
        Assert.NotNull(waiting.AcceptedAt);
    }

    [Fact]
    public async Task DeletedControlsReceiveFullUsableWindowWithoutForumScan()
    {
        using var rig = new RecruitmentRig(); await rig.Start(); var post = await rig.Post();
        rig.Clock.Advance(TimeSpan.FromMinutes(29)); var old = post.PublicMessageId!;
        await rig.Discord.DeleteAdvisoryAsync(post.Id, old, default);
        var reads = rig.Discord.ForumReads;
        await rig.Engine.MessageDeletedAsync(post.Id, new HashSet<string> { old }, default);
        Assert.NotEqual(old, post.PublicMessageId);
        Assert.Equal(rig.Clock.Now.AddMinutes(30), post.Deadline);
        Assert.Equal(reads, rig.Discord.ForumReads);
    }

    [Fact]
    public async Task MissingStarterHasBoundedRetryAndStartsWindowOnlyAfterDelivery()
    {
        using var rig = new RecruitmentRig(); await rig.Start();
        rig.Discord.Threads["100"] = Snapshot(rig.Clock.Now) with { Starter = null };
        await rig.Engine.ThreadChangedAsync("100", default);
        Assert.Null(rig.State.Listings["100"].Deadline);
        var reads = rig.Discord.ThreadReads;
        rig.Clock.Advance(TimeSpan.FromSeconds(10)); await rig.Engine.RunDueAsync(default);
        Assert.Equal(reads, rig.Discord.ThreadReads);
        rig.Discord.Threads["100"] = rig.Discord.Threads["100"] with { Starter = "Available" };
        rig.Clock.Advance(TimeSpan.FromSeconds(5)); await rig.Engine.RunDueAsync(default);
        Assert.Equal(rig.Clock.Now.AddMinutes(30), rig.State.Listings["100"].Deadline);
    }

    [Fact]
    public async Task ServiceIsQuietForTenIdleMinutesAndCoalescesRepeatedThreadEvents()
    {
        using var rig = new RecruitmentRig();
        var service = new RecruitmentService(rig.Settings, rig.Discord, rig.Clock);
        try
        {
            await service.StartAsync();
            var baseline = (rig.Discord.ForumReads, rig.Discord.Inventories, rig.Discord.Upserts, rig.Discord.ThreadReads);
            for (var i = 0; i < 10; i++)
            {
                rig.Clock.Advance(TimeSpan.FromMinutes(1));
                await service.RunAsync((_, _) => Task.FromResult(true));
            }
            Assert.Equal(baseline, (rig.Discord.ForumReads, rig.Discord.Inventories, rig.Discord.Upserts, rig.Discord.ThreadReads));
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var blocked = service.RunAsync(async (_, _) => { entered.SetResult(); await release.Task; return true; });
            await entered.Task;
            rig.Discord.Threads["100"] = Snapshot(rig.Clock.Now);
            for (var i = 0; i < 100; i++) service.NotifyThread("100");
            release.SetResult(); await blocked;
            await service.RunAsync((_, _) => Task.FromResult(true));
            Assert.Equal(1, service.Health.Open);
            Assert.Equal(baseline.Inventories, rig.Discord.Inventories);
            Assert.InRange(rig.Discord.ThreadReads - baseline.ThreadReads, 1, 2);
        }
        finally { await service.StopAsync(default); }
    }

    [Fact]
    public async Task QueuedUnknownIdentityPreventsAcceptingANewerAttemptByTheSameAuthor()
    {
        using var rig = new RecruitmentRig();
        var service = new RecruitmentService(rig.Settings, rig.Discord, rig.Clock);
        try
        {
            await service.StartAsync();
            rig.Discord.Threads["101"] = Snapshot(rig.Clock.Now.AddSeconds(1)) with { Id = "101" };
            service.NotifyThread("101"); await service.RunAsync((_, _) => Task.FromResult(true));
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var acknowledgement = service.RunAsync(async (engine, ct) =>
            {
                entered.SetResult(); await release.Task;
                return await engine.AcknowledgeAsync("1", "101", "200", engine.State!.Listings["101"].ValidCodes.Last(), ct);
            }, "101");
            await entered.Task;
            rig.Discord.Threads["100"] = Snapshot(rig.Clock.Now);
            service.NotifyThread("100"); release.SetResult();
            Assert.DoesNotContain("is accepted", await acknowledgement);
            var accepted = await service.RunAsync((engine, _) => Task.FromResult(engine.State!.Listings["101"].AcceptedAt));
            Assert.Null(accepted);
            Assert.Equal(2, service.Health.Open);
            Assert.Equal(1, rig.Discord.Inventories);
        }
        finally { await service.StopAsync(default); }
    }

    [Fact]
    public async Task StatusRemainsReadableDuringStartupAndShutdownCancelsPendingWork()
    {
        using var rig = new RecruitmentRig();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        rig.Discord.BeforeAccess = () => { entered.TrySetResult(); return release.Task; };
        var service = new RecruitmentService(rig.Settings, rig.Discord, rig.Clock);
        var startup = service.StartAsync(); await entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.False(startup.IsCompleted);
        Assert.NotNull(service.Health);
        Assert.False(service.Health.InventoryComplete);
        Assert.False(service.CanReadStatus("other-guild", "900"));
        var queued = service.RunAsync((_, _) => Task.FromResult(true));
        await service.StopAsync(default).WaitAsync(TimeSpan.FromSeconds(2));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => startup);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => queued);
        Assert.Equal("Stopped", service.Health.Status);
    }

    [Fact]
    public async Task QueueOverflowImmediatelyInvalidatesEvidenceBeforeAutomaticDelete()
    {
        using var rig = new RecruitmentRig();
        var service = new RecruitmentService(rig.Settings, rig.Discord, rig.Clock);
        try
        {
            await service.StartAsync(); rig.Discord.Threads["100"] = Snapshot(rig.Clock.Now); service.NotifyThread("100");
            await service.RunAsync((_, _) => Task.FromResult(true));
            rig.Discord.BeforeDelete = () => { for (var i = 0; i < 260; i++) service.NotifyThread((2000 + i).ToString()); };
            rig.Clock.Advance(TimeSpan.FromMinutes(31));
            await service.RunAsync(async (engine, ct) => { await engine.RunDueAsync(ct); return true; });
            Assert.Empty(rig.Discord.Actions);
        }
        finally { await service.StopAsync(default); }
    }

    [Fact]
    public async Task QueuedControlDeletionFencesTimeoutUntilReplacementIsDelivered()
    {
        using var rig = new RecruitmentRig();
        var service = new RecruitmentService(rig.Settings, rig.Discord, rig.Clock);
        try
        {
            await service.StartAsync(); rig.Discord.Threads["100"] = Snapshot(rig.Clock.Now); service.NotifyThread("100");
            var control = await service.RunAsync((engine, _) => Task.FromResult(engine.State!.Listings["100"].PublicMessageId!));
            rig.Discord.BeforeDelete = () =>
            {
                rig.Discord.Messages.Remove("100:recruitment:100:public");
                service.NotifyMessageDeletion("100", new HashSet<string> { control });
            };
            rig.Clock.Advance(TimeSpan.FromMinutes(31));
            await service.RunAsync((_, _) => Task.FromResult(true));
            await service.RunAsync(async (engine, ct) => { await engine.RunDueAsync(ct); return true; });
            Assert.Empty(rig.Discord.Actions);
            var deadline = await service.RunAsync((engine, _) => Task.FromResult(engine.State!.Listings["100"].Deadline));
            Assert.Equal(rig.Clock.Now.AddMinutes(30), deadline);
        }
        finally { await service.StopAsync(default); }
    }

    private static ThreadSnapshot Snapshot(DateTimeOffset now) => new("100", "11", "200", "Example", now, false, false, false, false,
        Array.Empty<string>(), "A listing", now.AddYears(-1), now.AddMonths(-1));
}
