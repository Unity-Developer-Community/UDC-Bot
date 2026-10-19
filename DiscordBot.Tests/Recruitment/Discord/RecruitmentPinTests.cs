using DiscordBot.Utils;
using DiscordBot.Services.Recruitment;

namespace DiscordBot.Tests.Recruitment;

public class RecruitmentPinTests
{
    [Fact]
    public async Task SuccessIsDurableAndManualUnpinIsPreservedIncludingExplicitRecovery()
    {
        using var rig = new RecruitmentRig(); await rig.Start(); var post = await rig.Post();
        Assert.True(post.PublicPin.Confirmed); Assert.Equal(post.PublicMessageId, post.PublicPin.MessageId);
        Assert.False(post.Pinned); Assert.False(post.Exempt); Assert.Equal(1, rig.Discord.PinWrites);
        rig.Discord.MessagePins.Clear();
        await rig.Sweep(); await rig.Staff("reconcile"); await rig.Restart();
        Assert.True(rig.State.Listings[post.Id].PublicPin.Confirmed);
        Assert.Empty(rig.Discord.MessagePins); Assert.Equal(1, rig.Discord.PinWrites);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CosmeticPermanentFailureDoesNotBlockControlsAcceptanceOrAlterWindow(bool full)
    {
        using var rig = new RecruitmentRig(); await rig.Start();
        rig.Discord.PinFailure = new InvalidOperationException(full ? "Maximum pins reached" : "Pin Messages permission missing");
        var post = await rig.Post(); var deadline = post.Deadline;
        Assert.NotNull(deadline); Assert.Null(post.Fault); Assert.Null(post.Hold);
        Assert.NotNull(post.PublicPin.Fault); Assert.Null(post.PublicPin.Fault!.RetryAt);
        Assert.Contains("optional response pin", rig.Engine.Health().Attention);
        var lookup = await rig.Staff("lookup");
        Assert.Contains("Optional response pin: pending", lookup);
        Assert.Contains(full ? "Maximum pins reached" : "Pin Messages permission missing", lookup);
        Assert.Contains("after relevant access change or explicit recovery", lookup);
        Assert.DoesNotContain("Pin Messages permission missing", RecruitmentPresentation.Details(post));
        Assert.Contains("accepted", await rig.Ack()); Assert.Equal(deadline, post.Deadline);
        rig.Clock.Advance(TimeSpan.FromDays(40)); await rig.Sweep();
        Assert.Equal(ListingStatus.Open, post.Status); Assert.NotNull(post.AcceptedAt);
        rig.Discord.PinFailure = null;
        rig.Engine.InvalidateAccess(); await rig.Sweep();
        Assert.True(post.PublicPin.Confirmed);
    }

    [Fact]
    public async Task LostResponseRestartReadsPinWithoutWritingAgain()
    {
        using var rig = new RecruitmentRig(); await rig.Start(); rig.Discord.LosePinResponse = true;
        var post = await rig.Post(); var deadline = post.Deadline;
        Assert.False(post.PublicPin.Confirmed); Assert.NotNull(post.PublicPin.Fault!.RetryAt);
        rig.Clock.Advance(TimeSpan.FromMinutes(1)); await rig.Restart();
        post = rig.State.Listings[post.Id];
        Assert.True(post.PublicPin.Confirmed); Assert.Null(post.PublicPin.Fault); Assert.Equal(1, rig.Discord.PinWrites);
        Assert.NotNull(deadline); Assert.Null(post.Fault);
    }

    [Fact]
    public async Task RateLimitRetriesClosedListingWithUnchangedPublicRender()
    {
        using var rig = new RecruitmentRig(); await rig.Start();
        var retry = rig.Clock.Now.AddMinutes(2);
        rig.Discord.PinFailure = new RecruitmentDeferredException(retry);
        var post = await rig.Post(); await rig.Ack(); await rig.Staff("close");
        Assert.Equal(ListingStatus.Closed, post.Status); Assert.Equal(retry, post.PublicPin.Fault!.RetryAt);
        Assert.Contains(DiscordDisplayText.Timestamp(retry), await rig.Staff("lookup"));
        rig.Discord.PinFailure = null; rig.Clock.Advance(TimeSpan.FromMinutes(2));
        await rig.Engine.RunDueAsync(default);
        Assert.True(post.PublicPin.Confirmed); Assert.Equal(ListingStatus.Closed, post.Status);
        Assert.True(rig.Discord.Threads[post.Id].Archived); Assert.True(rig.Discord.Threads[post.Id].Locked);
    }

    [Fact]
    public async Task DeletedResponseReplacementGetsItsOwnPin()
    {
        using var rig = new RecruitmentRig(); await rig.Start(); var post = await rig.Post();
        var old = post.PublicMessageId!;
        foreach (var key in rig.Discord.Messages.Where(m => m.Value.Id == old).Select(m => m.Key).ToArray()) rig.Discord.Messages.Remove(key);
        await rig.Engine.MessageDeletedAsync(post.Id, new HashSet<string> { old }, default);
        Assert.NotEqual(old, post.PublicMessageId); Assert.True(post.PublicPin.Confirmed);
        Assert.Equal(post.PublicMessageId, post.PublicPin.MessageId); Assert.Equal(2, rig.Discord.PinWrites);
    }

    [Fact]
    public async Task ReplacementPreservesInterruptedPinArchiveIntent()
    {
        using var rig = new RecruitmentRig(); await rig.Start(); var post = await rig.Post(); await rig.Ack();
        var old = post.PublicMessageId!;
        post.PublicPin.Confirmed = false; post.PublicPin.RestoreArchive = post.PublicPin.RestoreLocked = true;
        post.PublicPin.Fault = new RecruitmentFault("Restore interrupted", rig.Clock.Now.AddSeconds(15));
        foreach (var key in rig.Discord.Messages.Where(m => m.Value.Id == old).Select(m => m.Key).ToArray()) rig.Discord.Messages.Remove(key);
        await rig.Engine.MessageDeletedAsync(post.Id, new HashSet<string> { old }, default);
        var replacement = rig.Discord.Messages.Values.Single(m => m.Id == post.PublicMessageId).Message;
        Assert.True(replacement.RestoreArchive); Assert.True(replacement.RestoreLocked);
        Assert.True(post.PublicPin.Confirmed); Assert.NotEqual(old, post.PublicPin.MessageId);
    }

    [Fact]
    public async Task NetworkIoFailureIsCosmeticButPinIntentStorageFailureIsFatal()
    {
        using var rig = new RecruitmentRig(); await rig.Start();
        rig.Discord.PinFailure = new IOException("Network connection interrupted");
        var post = await rig.Post();
        Assert.Null(post.Fault); Assert.NotNull(post.Deadline); Assert.NotNull(post.PublicPin.Fault!.RetryAt);
        Assert.Contains("accepted", await rig.Ack());
        rig.Discord.PinFailure = null; rig.Clock.Now = post.PublicPin.Fault.RetryAt!.Value;
        rig.Discord.Threads[post.Id] = rig.Discord.Threads[post.Id] with { Archived = true, Locked = true };
        rig.Discord.BeforePinWrite = () => Directory.CreateDirectory(Path.Combine(rig.Root, "recruitment/recruitment-state.json.tmp"));
        await Assert.ThrowsAsync<IOException>(() => rig.Engine.RunDueAsync(default));
        Assert.Contains("stopped", rig.Engine.Status);
        Assert.False(post.PublicPin.Confirmed);
    }

    [Fact]
    public async Task BotMessagePinNeverProtectsPendingListingFromTimeout()
    {
        using var rig = new RecruitmentRig(); await rig.Start(); var post = await rig.Post();
        Assert.True(post.PublicPin.Confirmed); rig.Clock.Now = post.Deadline!.Value.AddSeconds(1);
        await rig.Engine.RunDueAsync(default);
        Assert.Equal(ListingStatus.Deleted, post.Status); Assert.Contains((post.Id, "delete"), rig.Discord.Actions);
    }
}
