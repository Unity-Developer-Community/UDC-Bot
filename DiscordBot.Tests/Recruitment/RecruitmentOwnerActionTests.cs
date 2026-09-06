using DiscordBot.Services.Recruitment.Policy;
using DiscordBot.Services.Recruitment.State;
using DiscordBot.Settings.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DiscordBot.Tests.Recruitment;

[TestClass]
public sealed class RecruitmentOwnerActionTests
{
    [TestMethod]
    public async Task WrongOwnerGuildThreadAndGeneration_AreRejectedBeforePublicMutation()
    {
        await using var f = new RecruitmentAdvisoryFixture(); await f.StartAsync();
        foreach (var context in new[] { f.Owner with { UserId = 999 }, f.Owner with { GuildId = 2 }, f.Owner with { ThreadId = 11 } })
            await Assert.ThrowsAsync<InvalidOperationException>(() => f.Owners.PrepareAsync(context, "0123456789ABCDEF", ActionKind.Delete, default));
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Owners.PrepareAsync(f.Owner, "0123456789ABCDEF", ActionKind.Delete, default));
        f.Options.Mode = RecruitmentMode.Observe;
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Owners.PrepareAsync(f.Owner, "0123456789ABCDEF", ActionKind.Delete, default));
        Assert.AreEqual(0, f.Discord.Actions);
    }

    [TestMethod]
    [DataRow("expired")]
    [DataRow("accepted")]
    [DataRow("version")]
    [DataRow("pinned")]
    [DataRow("unknown")]
    [DataRow("read-failed")]
    public async Task Confirmation_RechecksChangedStateBeforeRemoval(string change)
    {
        await using var f = new RecruitmentAdvisoryFixture(); await f.StartAsync();
        var confirmation = await f.Owners.PrepareAsync(f.Owner, await f.Generation(), ActionKind.Delete, default);
        if (change == "expired") f.Time.Advance(TimeSpan.FromMinutes(3));
        if (change == "accepted") await f.Store.UpdateAsync(state => { state.Posts[10].AcceptedAtUtc = f.Time.Now; state.Posts[10].Acknowledgement = AcknowledgementStatus.Passed; return true; });
        if (change == "version") await f.Store.UpdateAsync(state => { state.Posts[10].Advisory.Version++; return true; });
        if (change == "pinned") f.Discord.Post = f.Discord.Post! with { Pinned = true };
        if (change == "unknown") f.Discord.Post = f.Discord.Post! with { OrdinaryAuthor = null };
        if (change == "read-failed") f.Discord.FailReads = true;
        await Assert.ThrowsAsync<Exception>(() => f.Owners.ConfirmAsync(f.Owner, confirmation.Token, default));
        Assert.AreEqual(0, f.Discord.Actions);
    }

    [TestMethod]
    [DataRow(ActionKind.Delete)]
    [DataRow(ActionKind.LockArchive)]
    public async Task LostActionResponse_IsRecoveredWithoutRepeatingMutation_AndReplayIsRejected(ActionKind kind)
    {
        await using var f = new RecruitmentAdvisoryFixture(); await f.StartAsync();
        if (kind == ActionKind.LockArchive)
            await f.Owners.SubmitCodeAsync(f.Owner, await f.Generation(), await f.Code(), default);
        var confirmation = await f.Owners.PrepareAsync(f.Owner, await f.Generation(), kind, default);
        f.Discord.FailActionAfterWrite = true;
        await Assert.ThrowsAsync<IOException>(() => f.Owners.ConfirmAsync(f.Owner, confirmation.Token, default));
        Assert.IsNull((await f.Post()).PendingAction!.CompletedAtUtc);
        await f.Store.ReleaseAsync();
        await f.Owners.RecoverAsync(10, default);
        await f.Owners.RecoverAsync(10, default);
        Assert.AreEqual(1, f.Discord.Actions);
        Assert.IsNotNull((await f.Post()).PendingAction!.CompletedAtUtc);
        Assert.AreEqual(kind == ActionKind.Delete ? ListingLifecycle.Deleted : ListingLifecycle.Closed, (await f.Post()).Lifecycle);
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Owners.ConfirmAsync(f.Owner, confirmation.Token, default));
        Assert.AreEqual(0, (await f.State()).Authors.Count);
    }

    [TestMethod]
    public async Task SuccessfulPractice_InvalidatesEarlierRemovalConfirmation()
    {
        await using var f = new RecruitmentAdvisoryFixture(); await f.StartAsync();
        var confirmation = await f.Owners.PrepareAsync(f.Owner, await f.Generation(), ActionKind.Delete, default);
        await f.Owners.SubmitCodeAsync(f.Owner, await f.Generation(), await f.Code(), default);
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Owners.ConfirmAsync(f.Owner, confirmation.Token, default));
        Assert.AreEqual(0, f.Discord.Actions);
    }

    [TestMethod]
    public async Task ExplicitRemoval_PreservesPriorAcceptedDeletionHistory()
    {
        await using var f = new RecruitmentAdvisoryFixture(); await f.StartAsync();
        await f.Store.UpdateAsync(state =>
        {
            state.Posts[10].Acknowledgement = AcknowledgementStatus.Passed;
            state.Posts[10].AcceptedAtUtc = f.Time.Now;
            return true;
        });
        var confirmation = await f.Owners.PrepareAsync(f.Owner, await f.Generation(), ActionKind.Delete, default);
        await f.Owners.ConfirmAsync(f.Owner, confirmation.Token, default);
        var state = await f.State();
        Assert.AreEqual(f.Time.Now, state.Posts[10].DeletedObservedAtUtc);
        Assert.AreEqual(f.Time.Now, state.Authors[123].Groups[ListingGroup.Recruiting].LastAcceptedDeletedAtUtc);
    }

    [TestMethod]
    public async Task DriftedGuidelines_CannotBeAcknowledgedFromOldReceipt()
    {
        await using var f = new RecruitmentAdvisoryFixture(); await f.StartAsync();
        string issued = await f.Code();
        f.Discord.Forums[101] = f.Discord.Forums[101] with { Topic = "Staff editing guidelines" };
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Owners.SubmitCodeAsync(f.Owner, "bad-generation", issued, default));
        string generation = await f.Generation();
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Owners.SubmitCodeAsync(f.Owner, generation, issued, default));
        Assert.AreEqual(AcknowledgementStatus.Pending, (await f.Post()).Acknowledgement);
    }
}
