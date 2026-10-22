using DiscordBot.Services.Recruitment;

namespace DiscordBot.Tests.Recruitment;

public class RecruitmentAcknowledgementTests
{
    [Fact]
    public async Task ModalOwnershipUsesPersistedSnapshotEvenWhileWorkerIsBusyAndSubmissionRechecksOwner()
    {
        using var rig = new RecruitmentRig();
        rig.Discord.Threads["100"] = new("100", "11", "200", "Example listing", rig.Clock.Now,
            false, false, false, false, Array.Empty<string>(), "Budget AUD 500 for a short Unity project", null, null);
        var service = new RecruitmentService(rig.Settings, rig.Discord, rig.Clock);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Assert.False(service.CanOpenAcknowledgement("1", "100", "200"));
        await service.StartAsync();
        try
        {
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var busy = service.RunAsync(async (_, ct) => { entered.SetResult(); await release.Task.WaitAsync(ct); return true; });
            await entered.Task;
            Assert.True(service.CanOpenAcknowledgement("1", "100", "200"));
            Assert.False(service.CanOpenAcknowledgement("1", "100", "201"));
            Assert.False(service.CanOpenAcknowledgement("1", "100", "900"));
            Assert.False(service.CanOpenAcknowledgement("2", "100", "200"));
            Assert.False(service.CanOpenAcknowledgement("1", "101", "200"));
            release.SetResult(); await busy;
            var rejected = await service.RunAsync((engine, ct) => engine.AcknowledgeAsync("1", "100", "201",
                engine.State!.Listings["100"].ValidCodes.Last(), ct));
            Assert.Contains("does not belong to you", rejected);
            Assert.Null(await service.RunAsync((engine, _) => Task.FromResult(engine.State!.Listings["100"].AcknowledgedAt)));
            var accepted = await service.RunAsync((engine, ct) => engine.AcknowledgeAsync("1", "100", "200",
                engine.State!.Listings["100"].ValidCodes.Last(), ct));
            Assert.Contains("accepted", accepted);
            await service.RunAsync((engine, ct) => engine.CloseOwnerAsync("1", "100", "200", ct));
            Assert.False(service.CanOpenAcknowledgement("1", "100", "200"));
        }
        finally { release.TrySetResult(); await service.StopAsync(default); }
        Assert.False(service.CanOpenAcknowledgement("1", "100", "200"));
    }

    [Fact]
    public async Task AuthorIsPingedOnceAfterUsableControlsAndUpdatesAndRestartsDoNotRepeatIt()
    {
        using var rig = new RecruitmentRig(); await rig.Start(); var post = await rig.Post();
        var notice = Assert.Single(rig.Discord.Messages.Values, m => m.Message.MentionUserId is not null);
        Assert.Equal((ulong)200, notice.Message.MentionUserId);
        Assert.Equal(notice.Id, post.AcknowledgementNotice.MessageId);
        Assert.Contains("<@200>", notice.Message.Content);
        Assert.Contains("Guidelines", notice.Message.Content); Assert.Contains("Acknowledge", notice.Message.Content);
        Assert.NotNull(post.PublicMessageId); Assert.NotNull(post.Deadline);
        Assert.Contains("Check the card for your deadline", notice.Message.Content);
        Assert.DoesNotContain("https://", notice.Message.Content);
        Assert.False(notice.Message.OwnerControls); Assert.False(notice.Message.AcknowledgementRequired);
        var sends = rig.Discord.Sends;
        await rig.Engine.ThreadChangedAsync(post.Id, default); await rig.Sweep();
        var oldControls = post.PublicMessageId!;
        await rig.Discord.DeleteAdvisoryAsync(post.Id, oldControls, default);
        await rig.Engine.MessageDeletedAsync(post.Id, new HashSet<string> { oldControls }, default);
        Assert.NotEqual(oldControls, post.PublicMessageId);
        Assert.Equal(sends + 1, rig.Discord.Sends); // Only the restored card is new.
        sends = rig.Discord.Sends;
        await rig.Restart();
        await rig.Ack(); await rig.Restart();
        Assert.Equal(sends, rig.Discord.Sends);
        Assert.Single(rig.Discord.Messages.Values, m => m.Message.MentionUserId is not null);
    }

    [Fact]
    public async Task AmbiguousAuthorNoticeRecoversPersistedSendWithoutSecondMessage()
    {
        using var rig = new RecruitmentRig(); await rig.Start(); rig.Discord.LoseAcknowledgementNoticeResponse = true;
        var post = await rig.Post();
        Assert.True(post.AcknowledgementNotice.SendPending); Assert.NotNull(post.AcknowledgementNotice.StartedAt);
        Assert.NotNull(post.AcknowledgementNotice.Fault); Assert.Null(post.AcknowledgementNotice.MessageId);
        var persisted = File.ReadAllText(Path.Combine(rig.Root, "recruitment/recruitment-state.json"));
        Assert.Contains("Author notice response lost", persisted);
        var sends = rig.Discord.Sends;
        await rig.Restart();
        var recovered = rig.State.Listings[post.Id].AcknowledgementNotice;
        Assert.False(recovered.SendPending); Assert.NotNull(recovered.MessageId); Assert.Null(recovered.Fault);
        Assert.Equal(sends, rig.Discord.Sends);
        Assert.Single(rig.Discord.Messages.Values, m => m.Message.MentionUserId is not null);
    }

    [Fact]
    public async Task NoticeFailureRetriesIndependentlyWithoutResettingAcknowledgementWindow()
    {
        using var rig = new RecruitmentRig(); await rig.Start();
        rig.Discord.AcknowledgementNoticeFailure = new TimeoutException("Notice delivery unavailable");
        var post = await rig.Post(); var deadline = post.Deadline; var delivered = post.PromptDeliveredAt;
        Assert.NotNull(deadline); Assert.Null(post.Fault); Assert.Null(post.AcknowledgementNotice.MessageId);
        rig.Clock.Now = post.AcknowledgementNotice.Fault!.RetryAt!.Value.AddTicks(1);
        rig.Discord.AcknowledgementNoticeFailure = null;
        await rig.Engine.RunDueAsync(default);
        Assert.NotNull(post.AcknowledgementNotice.MessageId); Assert.Null(post.AcknowledgementNotice.Fault);
        Assert.Equal(deadline, post.Deadline); Assert.Equal(delivered, post.PromptDeliveredAt);
    }

    [Fact]
    public async Task NoticeFailureDoesNotBlockAcknowledgementOrSendAnObsoletePingAfterAcceptance()
    {
        using var rig = new RecruitmentRig(); await rig.Start();
        rig.Discord.AcknowledgementNoticeFailure = new TimeoutException("Notice delivery unavailable");
        var post = await rig.Post();
        Assert.Contains("accepted", await rig.Ack());
        rig.Discord.AcknowledgementNoticeFailure = null;
        rig.Clock.Advance(TimeSpan.FromMinutes(1)); await rig.Engine.RunDueAsync(default); await rig.Restart();
        Assert.DoesNotContain(rig.Discord.Messages.Values, m => m.Message.MentionUserId is not null);
        Assert.NotNull(rig.State.Listings[post.Id].AcceptedAt);
    }
}
