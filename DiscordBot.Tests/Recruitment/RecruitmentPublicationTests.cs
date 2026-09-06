using DiscordBot.Services.Recruitment;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DiscordBot.Tests.Recruitment;

[TestClass]
public sealed class RecruitmentPublicationTests
{
    [TestMethod]
    public async Task FourTemplates_RenderCompleteBoundedTopics_AndCodesRotateAtMondayUtc()
    {
        await using var f = new RecruitmentAdvisoryFixture();
        f.Templates.ValidateAll();
        var topics = Enum.GetValues<RecruitmentForumKind>().Select(kind => f.Templates.Render(f.Templates.Load(kind), "ABCDE")).ToArray();
        Assert.AreEqual(4, topics.Distinct().Count());
        Assert.IsTrue(topics.All(topic => topic.Length <= 4096 && topic.Contains("UDC acknowledgement code: ABCDE")));
        foreach (int _ in Enumerable.Range(0, 100)) Assert.IsTrue(RecruitmentGuidelines.IsCode(RecruitmentGuidelines.NewCode()));
        var monday = new DateTimeOffset(2026, 9, 7, 0, 0, 0, TimeSpan.Zero);
        Assert.AreEqual(monday.AddDays(-7), RecruitmentGuidelines.WeekStart(monday.AddTicks(-1)));
        Assert.AreEqual(monday, RecruitmentGuidelines.WeekStart(monday.ToOffset(TimeSpan.FromHours(10))));
    }

    [TestMethod]
    [DataRow("No code")]
    [DataRow("UDC acknowledgement code: {{code}} {{unknown}}")]
    [DataRow("UDC acknowledgement code: {{code}} {{code}}")]
    [DataRow("UDC acknowledgement code: {{code}} {{")]
    [DataRow("An unlabelled {{code}}")]
    public async Task InvalidTemplates_AreRejected(string template)
    {
        await using var f = new RecruitmentAdvisoryFixture();
        Assert.Throws<InvalidDataException>(() => f.Templates.Render(template, "ABCDE"));
        Assert.Throws<InvalidDataException>(() => f.Templates.Render("UDC acknowledgement code: {{code}}" + new string('x', 4096), "ABCDE"));
    }

    [TestMethod]
    public async Task BlankForum_AppendsClosedWithoutLosingMetadata_AndPublicationIsIdempotent()
    {
        await using var f = new RecruitmentAdvisoryFixture(); await f.InitializeAsync();
        var original = new RecruitmentForumTag(900, "Graphics", true, 1234, null);
        f.Discord.Forums[101] = f.Discord.Forums[101] with { Tags = [original] };
        await f.Guidelines.EnsureAsync(f.Forum, default);
        await f.Guidelines.EnsureAsync(f.Forum, default);
        Assert.AreEqual(original, f.Discord.Forums[101].Tags[0]);
        Assert.AreEqual(1, f.Discord.TagAppends); Assert.AreEqual(1, f.Discord.Publishes);
        var saved = (await f.State()).Forums[101].Publication;
        Assert.IsNotNull(saved.Confirmed?.PublishedAtUtc); Assert.IsNull(saved.Candidate);
        Assert.AreEqual(1101ul, saved.ClosedTagId);
    }

    [TestMethod]
    public async Task UnmanagedOrDriftedTopic_RequiresExactPreviewAdoption()
    {
        await using var f = new RecruitmentAdvisoryFixture(); await f.InitializeAsync();
        f.Discord.Forums[101] = f.Discord.Forums[101] with { Topic = "Staff's existing guidelines" };
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Guidelines.EnsureAsync(f.Forum, default));
        Assert.AreEqual(0, f.Discord.Publishes);
        var preview = await f.Guidelines.PreviewAsync(f.Forum, default);
        f.Discord.Forums[101] = f.Discord.Forums[101] with { Topic = "Staff revised it" };
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Guidelines.EnsureAsync(f.Forum, default, preview.CurrentTopicHash));
        preview = await f.Guidelines.PreviewAsync(f.Forum, default);
        await f.Guidelines.EnsureAsync(f.Forum, default, preview.CurrentTopicHash);
        Assert.AreEqual(1, f.Discord.Publishes);
        f.Discord.Forums[101] = f.Discord.Forums[101] with { Topic = "Another manual edit" };
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Guidelines.EnsureAsync(f.Forum, default));
        Assert.AreEqual("Another manual edit", f.Discord.Forums[101].Topic);
    }

    [TestMethod]
    [DataRow("renamed")]
    [DataRow("duplicate")]
    [DataRow("moderated")]
    [DataRow("full")]
    public async Task ConflictingTags_PauseSetupWithoutRemovingMetadata(string conflict)
    {
        await using var f = new RecruitmentAdvisoryFixture(); await f.InitializeAsync();
        await f.Guidelines.EnsureAsync(f.Forum, default);
        var closed = f.Discord.Forums[101].Tags.Single();
        RecruitmentForumTag[] tags = conflict switch
        {
            "renamed" => [closed with { Name = "Retired" }],
            "duplicate" => [closed, closed with { Id = 999 }],
            "moderated" => [closed with { Moderated = true }],
            _ => Enumerable.Range(1, 20).Select(i => new RecruitmentForumTag((ulong)i, "Tag " + i, false, null, null)).ToArray()
        };
        f.Discord.Forums[101] = f.Discord.Forums[101] with { Tags = tags };
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Guidelines.EnsureAsync(f.Forum, default));
        CollectionAssert.AreEqual(tags, f.Discord.Forums[101].Tags.ToArray());
        if (conflict == "renamed")
        {
            var preview = await f.Guidelines.PreviewAsync(f.Forum, default);
            await f.Guidelines.EnsureAsync(f.Forum, default, preview.CurrentTopicHash, preview.CurrentTagHash);
            Assert.AreEqual("Retired", f.Discord.Forums[101].Tags[0].Name);
            Assert.AreEqual("Closed", f.Discord.Forums[101].Tags[1].Name);
        }
    }

    [TestMethod]
    public async Task LostPublishResponse_RecoversCandidateWithoutChangingCodeOrRepeatingWrite()
    {
        await using var f = new RecruitmentAdvisoryFixture(); await f.InitializeAsync();
        f.Discord.FailPublishAfterWrite = true;
        await Assert.ThrowsAsync<IOException>(() => f.Guidelines.EnsureAsync(f.Forum, default));
        var candidate = (await f.State()).Forums[101].Publication.Candidate!;
        Assert.IsNull((await f.State()).Forums[101].Publication.Confirmed);
        f.Discord.FailPublishAfterWrite = false;
        await f.Store.ReleaseAsync(); // Force recovery from persisted state, not a retained snapshot.
        await f.Guidelines.EnsureAsync(f.Forum, default);
        Assert.AreEqual(candidate.Code, await f.Code()); Assert.AreEqual(1, f.Discord.Publishes);
    }

    [TestMethod]
    public async Task ClearingPreviouslyOwnedTopic_IsDrift_AndMalformedPublicationMetadataCannotCommit()
    {
        await using var f = new RecruitmentAdvisoryFixture(); await f.StartAsync();
        f.Discord.Forums[101] = f.Discord.Forums[101] with { Topic = "" };
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Guidelines.EnsureAsync(f.Forum, default));
        await Assert.ThrowsAsync<InvalidDataException>(() => f.Store.UpdateAsync(state =>
        {
            state.Forums[101].Publication.Confirmed!.Code = "invalid";
            return true;
        }));
        Assert.IsTrue(f.Store.IsHealthy);
        Assert.IsTrue(RecruitmentGuidelines.IsCode(await f.Code()));
    }

    [TestMethod]
    public async Task Rotation_IsPerForum_AndAddsOnlyConfirmedCodesToItsOpenChallenges()
    {
        await using var f = new RecruitmentAdvisoryFixture();
        f.Time.Now = new(2026, 9, 6, 23, 50, 0, TimeSpan.Zero);
        await f.StartAsync();
        string issued = await f.Code();
        var otherReceipt = (await f.State()).Forums[102].Publication.Confirmed!;
        f.Discord.FailingForum = 102;
        f.Time.Advance(TimeSpan.FromMinutes(11));
        await f.Coordinator.TickAsync(default);
        var state = await f.State();
        CollectionAssert.Contains(state.Posts[10].AcceptedCodes, issued);
        CollectionAssert.Contains(state.Posts[10].AcceptedCodes, state.Forums[101].Publication.Confirmed!.Code);
        Assert.AreEqual(otherReceipt.WeekStartUtc, state.Forums[102].Publication.Confirmed!.WeekStartUtc);
        Assert.IsNotNull(state.Forums[102].Publication.Error);
        Assert.AreEqual(new DateTimeOffset(2026, 9, 7, 0, 20, 0, TimeSpan.Zero), state.Posts[10].ChallengeDeadlineUtc);
    }
}
