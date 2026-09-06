using DiscordBot.Services.Recruitment;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DiscordBot.Tests.Recruitment;

[TestClass]
public sealed class RecruitmentAdvisoryTests
{
    [TestMethod]
    public async Task SuccessfulPracticeSurvivesFailedStatusEdit_AndItsTextRecovers()
    {
        await using var f = new RecruitmentAdvisoryFixture(); await f.StartAsync();
        await f.Owners.SubmitCodeAsync(f.Owner, await f.Generation(), await f.Code(), default);
        f.Discord.FailEdits = true;
        Assert.IsFalse(await f.Coordinator.TryRefreshPostAsync(10, default));
        Assert.AreEqual(RecruitmentAcknowledgement.Passed, (await f.Post()).Acknowledgement);
        Assert.IsNotNull((await f.Post()).Advisory.Error);
        f.Discord.FailEdits = false; f.Time.Advance(TimeSpan.FromMinutes(3));
        await f.Coordinator.TickAsync(default);
        Assert.IsNull((await f.Post()).Advisory.Error);
        Assert.IsTrue(f.Discord.Messages.Values.Single().Embed.Fields.Single(field => field.Name == "Acknowledgement").Value.Contains("completed"));
    }

    [TestMethod]
    public async Task ImportedPosts_GetNoRetroactivePrompt_AndManualClosedCancelsWithoutPenalty()
    {
        await using var f = new RecruitmentAdvisoryFixture(); await f.InitializeAsync();
        await f.Store.UpdateAsync(state => { state.Posts[10].Observation.Imported = true; return true; });
        await f.Coordinator.TickAsync(default);
        Assert.AreEqual(0, f.Discord.Sends); Assert.IsNull((await f.Post()).ChallengeDeadlineUtc);
        await f.Store.UpdateAsync(state => { state.Posts[10].Observation.Imported = false; return true; });
        await f.Coordinator.RefreshPostAsync(10, default);
        f.Discord.Post = f.Discord.Post! with { Tags = [1101] };
        await f.Coordinator.RefreshPostAsync(10, default);
        Assert.IsTrue((await f.Post()).ClosedRequested);
        Assert.AreEqual(RecruitmentAcknowledgement.Cancelled, (await f.Post()).Acknowledgement);
        Assert.AreEqual(0, f.Discord.Actions);
    }

    [TestMethod]
    public async Task WindowStartsAfterSendAndControlsDelivery_AndPracticeNeverAcceptsOrPenalizes()
    {
        await using var f = new RecruitmentAdvisoryFixture(); await f.InitializeAsync();
        f.Discord.OnSend = () => f.Time.Advance(TimeSpan.FromMinutes(4));
        f.Discord.OnEdit = () => f.Time.Advance(TimeSpan.FromSeconds(10));
        await f.Coordinator.TickAsync(default);
        var post = await f.Post();
        Assert.AreEqual(RecruitmentTestData.Now.AddMinutes(4).AddSeconds(10), post.PromptedAtUtc);
        Assert.IsNotNull(post.PromptedAtUtc);
        Assert.AreEqual(post.PromptedAtUtc.Value.AddMinutes(30), post.ChallengeDeadlineUtc);
        string answer = await f.Owners.SubmitCodeAsync(f.Owner, await f.Generation(), (await f.Code()).ToLowerInvariant(), default);
        Assert.IsTrue(answer.Contains("completed"));
        f.Time.Advance(TimeSpan.FromDays(40)); await f.Coordinator.TickAsync(default);
        post = await f.Post();
        Assert.AreEqual(RecruitmentAcknowledgement.Passed, post.Acknowledgement);
        Assert.IsNull(post.AcceptedAtUtc); Assert.IsFalse(post.EnforcementEnrolled); Assert.IsFalse(post.ChallengeEnforceable);
        Assert.AreEqual(0, (await f.State()).Authors.Count); Assert.AreEqual(0, f.Discord.Actions);
    }

    [TestMethod]
    public async Task FailedControlDelivery_DoesNotStartWindow_AndRecoveryGrantsFullWindow()
    {
        await using var f = new RecruitmentAdvisoryFixture(); await f.InitializeAsync();
        f.Discord.FailEdits = true; await f.Coordinator.TickAsync(default);
        Assert.IsNull((await f.Post()).PromptedAtUtc);
        Assert.AreEqual(1, f.Discord.Sends);
        f.Time.Advance(TimeSpan.FromHours(2)); f.Discord.FailEdits = false;
        await f.Coordinator.TickAsync(default);
        Assert.AreEqual(f.Time.Now.AddMinutes(30), (await f.Post()).ChallengeDeadlineUtc);
        Assert.AreEqual(1, f.Discord.Sends);
        string oldGeneration = await f.Generation();
        await f.Coordinator.InitializeAsync(default);
        f.Time.Advance(TimeSpan.FromHours(2)); await f.Coordinator.TickAsync(default);
        Assert.AreNotEqual(oldGeneration, await f.Generation());
        Assert.AreEqual(f.Time.Now.AddMinutes(30), (await f.Post()).ChallengeDeadlineUtc);
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Owners.SubmitCodeAsync(f.Owner, oldGeneration, "ABCDE", default));
    }

    [TestMethod]
    public async Task LostSendResponse_SearchesBeforeRetry_AndRecoversExactlyOneMessage()
    {
        await using var f = new RecruitmentAdvisoryFixture(); await f.InitializeAsync();
        f.Discord.FailSendAfterWrite = true; await f.Coordinator.TickAsync(default);
        Assert.IsNull((await f.Post()).PromptedAtUtc);
        f.Discord.SearchPages.Enqueue(new(null, 400, false));
        f.Time.Advance(TimeSpan.FromMinutes(3)); await f.Coordinator.TickAsync(default);
        Assert.AreEqual(400ul, (await f.Post()).Advisory.SearchBeforeId);
        Assert.AreEqual(1, f.Discord.Sends);
        await f.Store.ReleaseAsync();
        f.Time.Advance(TimeSpan.FromMinutes(3)); await f.Coordinator.TickAsync(default);
        Assert.AreEqual(1, f.Discord.Sends); Assert.AreEqual(1, f.Discord.Messages.Count);
        Assert.IsNotNull((await f.Post()).AdvisoryMessageId);
        Assert.AreEqual(f.Time.Now.AddMinutes(30), (await f.Post()).ChallengeDeadlineUtc);
    }

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public async Task RenderOrUploadFailure_RecoversWithEquivalentText(bool renderFailure)
    {
        await using var f = new RecruitmentAdvisoryFixture(); await f.InitializeAsync();
        f.Banner.Fail = renderFailure; f.Discord.FailSendBeforeWrite = !renderFailure;
        await f.Coordinator.TickAsync(default);
        f.Discord.FailSendBeforeWrite = false;
        f.Time.Advance(TimeSpan.FromMinutes(3)); await f.Coordinator.TickAsync(default);
        Assert.IsFalse(f.Discord.LastSendHadImage);
        Assert.IsNotNull((await f.Post()).Advisory.RenderError);
        var view = f.Discord.Messages.Values.Single();
        Assert.IsTrue(view.Embed.Fields.Any(field => field.Name == "Known context"));
        string publicText = view.Embed.Description + string.Join(" ", view.Embed.Fields.Select(field => field.Value)) + view.ImageDescription;
        Assert.IsFalse(publicText.Contains(await f.Code()));
        Assert.AreEqual(RecruitmentAcknowledgement.Pending, (await f.Post()).Acknowledgement);
    }

    [TestMethod]
    public async Task DeletedAdvisory_IsReplacedAndRestartsWindow_WhileArchivedOrProtectedPostsAreUntouched()
    {
        await using var f = new RecruitmentAdvisoryFixture(); await f.StartAsync();
        string oldGeneration = await f.Generation(); f.Discord.Messages.Clear();
        f.Time.Advance(TimeSpan.FromMinutes(3)); await f.Coordinator.TickAsync(default);
        f.Time.Advance(TimeSpan.FromMinutes(3)); await f.Coordinator.TickAsync(default);
        Assert.AreEqual(2, f.Discord.Sends); Assert.AreNotEqual(oldGeneration, await f.Generation());
        Assert.AreEqual(f.Time.Now.AddMinutes(30), (await f.Post()).ChallengeDeadlineUtc);
        f.Discord.Post = f.Discord.Post! with { Archived = true };
        f.Discord.Messages.Clear(); f.Time.Advance(TimeSpan.FromMinutes(3)); await f.Coordinator.TickAsync(default);
        Assert.AreEqual(2, f.Discord.Sends); Assert.AreEqual(0, f.Discord.Actions);
    }

    [TestMethod]
    public async Task TimeoutCanBeRenewedWithoutFailureHistory_AndGuessesAreRateLimited()
    {
        await using var f = new RecruitmentAdvisoryFixture(); await f.StartAsync();
        for (int i = 0; i < 5; i++) await f.Owners.SubmitCodeAsync(f.Owner, await f.Generation(), "wrong", default);
        Assert.IsTrue((await f.Owners.SubmitCodeAsync(f.Owner, await f.Generation(), await f.Code(), default)).Contains("wait"));
        f.Time.Advance(TimeSpan.FromMinutes(31)); await f.Coordinator.TickAsync(default);
        Assert.AreEqual(RecruitmentAcknowledgement.Pending, (await f.Post()).Acknowledgement);
        Assert.AreEqual(0, f.Discord.Actions);
        await f.Owners.RenewAsync(f.Owner, await f.Generation(), default);
        await f.Coordinator.RefreshPostAsync(10, default);
        Assert.AreEqual(f.Time.Now.AddMinutes(30), (await f.Post()).ChallengeDeadlineUtc);
        Assert.AreEqual(0, (await f.Post()).Advisory.IncorrectAttempts);
    }
}
