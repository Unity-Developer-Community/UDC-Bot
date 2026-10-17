using DiscordBot.Services.Recruitment;
using Newtonsoft.Json.Linq;

namespace DiscordBot.Tests.Recruitment;

public class RecruitmentStorageAndSetupTests
{
    [Fact]
    public void ShippedGuidelinesFitBudgetWithMaximumDurations()
    {
        using var rig = new RecruitmentRig();
        var settings = rig.Settings.Recruitment;
        settings.AcknowledgementMinutes = 1440;
        settings.CooldownDays = settings.UnansweredDays = 365;
        var assets = new RecruitmentAssets(rig.Settings.AssetsRootPath, settings);
        foreach (var purpose in settings.Forums.Mapping().Keys)
        {
            var text = assets.Render(purpose, "ABCDE", settings);
            Assert.InRange(text.Length, 1, 1000);
            Assert.Contains("pinned rules", text);
            Assert.Contains("ABCDE", text);
        }
    }

    [Theory]
    [InlineData(1000)]
    [InlineData(1001)]
    public void GuidelinesBudgetChecksRenderedLength(int length)
    {
        using var rig = new RecruitmentRig();
        var assets = new RecruitmentAssets(rig.Settings.AssetsRootPath, rig.Settings.Recruitment);
        assets.Templates["paid-recruiting"] = new string('x', length - 5) + "{{code}}";
        if (length == 1000)
            Assert.Equal(length, assets.Render("paid-recruiting", "ABCDE", rig.Settings.Recruitment).Length);
        else
            Assert.Throws<InvalidDataException>(() => assets.Render("paid-recruiting", "ABCDE", rig.Settings.Recruitment));
    }

    [Fact]
    public async Task StoreRoundTripsCooldownAndRefusesSecondWriter()
    {
        using var rig = new RecruitmentRig(); await rig.Start(); var post = await rig.Post(); await rig.Ack();
        Assert.Throws<IOException>(() => new RecruitmentStateStore(rig.Root, "1", rig.Settings.Recruitment.Forums.Mapping().ToDictionary(x => x.Key, x => x.Value.ToString()), rig.Clock.Now));
        await rig.Restart(); Assert.Equal(post.CreatedAt, rig.State.Authors["200"].LastAcceptedCreation);
        Assert.NotNull(rig.State.Listings["100"].AcceptedAt);
    }

    [Fact]
    public async Task CurrentStateRoundTripsV2DeliveryAndArchiveIntentWithHistory()
    {
        using var rig = new RecruitmentRig(); await rig.Start(); var post = await rig.Post(); await rig.Ack();
        rig.Engine.Dispose();
        var forums = rig.Settings.Recruitment.Forums.Mapping().ToDictionary(x => x.Key, x => x.Value.ToString());
        var fileName = "recruitment-observations-" + new string('a', 64) + ".png";
        using (var writer = new RecruitmentStateStore(rig.Root, "1", forums, rig.Clock.Now))
        {
            var pending = writer.State.Listings["100"];
            pending.PublicMediaAttachmentId = "901"; pending.PublicMediaFileName = fileName;
            pending.PublicRestoreArchive = true; pending.PublicRestoreLocked = true; pending.PublicSendPending = true;
            pending.PublicSendStartedAt = rig.Clock.Now; writer.Save();
        }
        using var store = new RecruitmentStateStore(rig.Root, "1", forums, rig.Clock.Now);
        Assert.Equal("901", store.State.Listings["100"].PublicMediaAttachmentId);
        Assert.Equal(fileName, store.State.Listings["100"].PublicMediaFileName);
        Assert.True(store.State.Listings["100"].PublicRestoreLocked); Assert.True(store.State.Listings["100"].PublicSendPending);
        Assert.Equal(post.AcceptedAt, store.State.Listings["100"].AcceptedAt);
        Assert.Equal(post.CreatedAt, store.State.Authors["200"].LastAcceptedCreation);
    }

    [Theory]
    [InlineData("corrupt")]
    [InlineData("missing")]
    [InlineData("authors")]
    [InlineData("schema")]
    public async Task LostOrMalformedHistoryStopsFeatureWithoutSilentlyResetting(string damage)
    {
        using var rig = new RecruitmentRig(); await rig.Start(); await rig.Post(); await rig.Ack(); rig.Engine.Dispose();
        var path = Path.Combine(rig.Root, "recruitment/recruitment-state.json");
        if (damage == "missing") File.Delete(path);
        else if (damage == "corrupt") File.WriteAllText(path, "{broken");
        else
        {
            var json = JObject.Parse(File.ReadAllText(path));
            if (damage == "authors") json.Remove("Authors");
            else json["Schema"] = "legacy-v1";
            File.WriteAllText(path, json.ToString());
        }
        var damaged = File.Exists(path) ? File.ReadAllText(path) : null;
        await rig.Restart(); Assert.Null(rig.Engine.State); Assert.Contains("stopped", rig.Engine.Status);
        Assert.Equal(damaged, File.Exists(path) ? File.ReadAllText(path) : null);
    }

    [Fact]
    public async Task ExistingV2StateLoadsWithoutNewOptionalOperationalReceipts()
    {
        using var rig = new RecruitmentRig(); await rig.Start(); await rig.Post(); await rig.Ack();
        var accepted = rig.State.Authors["200"].LastAcceptedCreation;
        rig.Engine.Dispose();
        var path = Path.Combine(rig.Root, "recruitment/recruitment-state.json");
        using var reader = new Newtonsoft.Json.JsonTextReader(new StringReader(File.ReadAllText(path))) { DateParseHandling = Newtonsoft.Json.DateParseHandling.None };
        var json = JObject.Load(reader);
        json.Remove("Overview"); json.Remove("RecoveryFault");
        foreach (var forum in json["Forums"]!.Children<JProperty>()) { ((JObject)forum.Value).Remove("Fault"); ((JObject)forum.Value).Remove("Reference"); }
        foreach (var listing in json["Listings"]!.Children<JProperty>())
        {
            ((JObject)listing.Value).Remove("Fault");
            ((JObject)listing.Value["Facts"]!).Remove("MemberRoleCount"); ((JObject)listing.Value["Facts"]!).Remove("Portfolio");
        }
        File.WriteAllText(path, json.ToString());
        await rig.Restart();
        Assert.True(rig.Engine.State is not null, rig.Engine.Status);
        Assert.Equal(accepted, rig.State.Authors["200"].LastAcceptedCreation);
        Assert.NotNull(rig.State.Overview);
        Assert.NotNull(rig.State.Listings["100"].AcceptedAt);
        Assert.Equal(4, rig.Discord.ReferenceCreates); // Existing bot references recovered from their marker.
    }

    [Fact]
    public async Task ChangedForumIdsCannotAdoptExistingHistory()
    {
        using var rig = new RecruitmentRig(); await rig.Start();
        rig.Settings.Recruitment.Forums.PaidRecruitingId = 15;
        await rig.Restart(); Assert.Null(rig.Engine.State); Assert.Contains("stopped", rig.Engine.Status);
    }

    [Fact]
    public async Task SetupIsIdempotentAndKeepsTagIdentities()
    {
        using var rig = new RecruitmentRig(); await rig.Start();
        var ids = rig.State.Forums["paid-recruiting"].Tags.ToDictionary(t => t.Key, t => t.Value.Id);
        Assert.Equal(8, rig.Discord.ForumWrites); await rig.Sweep(); await rig.Restart();
        Assert.Equal(8, rig.Discord.ForumWrites);
        Assert.Equal(ids, rig.State.Forums["paid-recruiting"].Tags.ToDictionary(t => t.Key, t => t.Value.Id));
        Assert.All(rig.Discord.Forums.Values, f => Assert.True(f.RequireTag));
    }

    [Fact]
    public async Task UnexpectedGuidelinesEditsHoldActionsUntilExplicitRepair()
    {
        using var rig = new RecruitmentRig(); await rig.Start(); var post = await rig.Post();
        rig.Discord.Forums["11"] = rig.Discord.Forums["11"] with { Topic = "Moderator change" };
        rig.Clock.Advance(TimeSpan.FromHours(1)); await rig.Sweep();
        Assert.Contains("Guidelines changed", rig.Engine.Status); Assert.Empty(rig.Discord.Actions);
        Assert.Equal("Moderator change", rig.Discord.Forums["11"].Topic);
        await rig.Staff("repair"); Assert.DoesNotContain("Moderator change", rig.Discord.Forums["11"].Topic);
    }

    [Fact]
    public async Task DeletedManagedTagRequiresExplicitRepairAndPreservesOtherIdentities()
    {
        using var rig = new RecruitmentRig(); await rig.Start();
        var receipt = rig.State.Forums["paid-recruiting"];
        var missing = receipt.Tags["closed"].Id;
        var preserved = receipt.Tags["programming"].Id;
        rig.Discord.Forums["11"] = rig.Discord.Forums["11"] with { Tags = rig.Discord.Forums["11"].Tags.Where(t => t.Id != missing).ToArray() };
        await rig.Sweep(); Assert.Contains("managed tag is missing", rig.Engine.Status);
        await rig.Staff("repair");
        Assert.NotEqual(missing, receipt.Tags["closed"].Id);
        Assert.Equal(preserved, receipt.Tags["programming"].Id);
    }

    [Fact]
    public async Task FirstUseDoesNotOverwriteUnexpectedForumContent()
    {
        using var rig = new RecruitmentRig(); rig.Discord.Forums["11"] = rig.Discord.Forums["11"] with { Topic = "Existing Guidelines" };
        await rig.Start(); Assert.Contains("Guidelines changed", rig.Engine.Status);
        Assert.Equal(6, rig.Discord.ForumWrites); // Other forums can complete their independent setup.
        Assert.NotNull(rig.State.Forums["paid-recruiting"].Fault);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task InterruptedSetupRecoversAssignedTagIdsBeforeEnablingRequireTag(bool lostResponse)
    {
        using var rig = new RecruitmentRig();
        if (lostResponse) rig.Discord.LoseNextForumResponse = true;
        else { rig.Discord.FailForum = "11"; rig.Discord.FailForumRequireTag = true; }
        await rig.Start();
        var ids = rig.Discord.Forums["11"].Tags.Select(t => t.Id).ToArray();
        Assert.NotEmpty(ids);
        Assert.False(rig.Discord.Forums["11"].RequireTag);
        Assert.NotNull(rig.State.Forums["paid-recruiting"].PendingTopic);
        rig.Discord.FailForum = null;
        await rig.Restart();
        Assert.Equal(ids, rig.Discord.Forums["11"].Tags.Select(t => t.Id));
        Assert.True(rig.Discord.Forums["11"].RequireTag);
        Assert.Null(rig.State.Forums["paid-recruiting"].PendingTopic);
        Assert.Single(rig.Discord.ForumPublications, p => p.Id == "11" && p.Tags);
    }

    [Fact]
    public async Task UnchangedPermanentSetupHoldDoesNotReadOrWriteUntilExplicitRecovery()
    {
        using var rig = new RecruitmentRig();
        rig.Discord.FailForum = "11";
        await rig.Start();
        var fault = rig.State.Forums["paid-recruiting"].Fault;
        Assert.Null(fault!.RetryAt);
        var attempts = fault.Attempts;
        rig.Discord.FailForum = null;
        await rig.Engine.SweepAsync(default, retryHeld: false);
        Assert.Equal(attempts, rig.State.Forums["paid-recruiting"].Fault!.Attempts);
        Assert.False(rig.Discord.Forums["11"].RequireTag);
        await rig.Staff("repair");
        Assert.Null(rig.State.Forums["paid-recruiting"].Fault);
    }

    [Fact]
    public async Task PartialRotationRetriesSameCode_AndAcceptsPreviouslyIssuedCode()
    {
        using var rig = new RecruitmentRig(); await rig.Start();
        rig.Clock.Now = rig.State.RotateAt.AddMinutes(-5);
        var post = await rig.Post(); var issued = post.ValidCodes.Single();
        rig.Clock.Advance(TimeSpan.FromMinutes(6)); rig.Discord.FailForum = "12";
        await rig.Sweep(); var rotating = rig.State.RotationCode;
        Assert.Equal(rotating, rig.State.Forums["paid-recruiting"].VisibleCode);
        Assert.NotEqual(rotating, rig.State.Forums["paid-for-hire"].VisibleCode);
        rig.Discord.FailForum = null; await rig.Sweep();
        Assert.Equal(rotating, rig.State.RotationCode);
        Assert.Contains("accepted", await rig.Ack(code: issued));
        Assert.DoesNotContain(rotating, rig.Discord.Messages["99:recruitment:100:staff"].Message.Content);
        Assert.DoesNotContain(rotating, rig.Discord.Messages["100:recruitment:100:public"].Message.Content);
    }

    [Fact]
    public async Task RestartNeverClosesAcceptedListingBasedOnIncompleteHistoricalReplies()
    {
        using var rig = new RecruitmentRig(); await rig.Start(); await rig.Post(); await rig.Ack();
        rig.Clock.Advance(TimeSpan.FromDays(31)); await rig.Restart();
        Assert.Equal(ListingStatus.Open, rig.State.Listings["100"].Status);
        Assert.Contains("Unknown recruitment operation", await rig.Staff("review-no-reply"));
        await rig.Sweep();
        Assert.Equal(ListingStatus.Open, rig.State.Listings["100"].Status);
    }

    [Fact]
    public async Task RetentionExpiresTerminalDetailsButKeepsOpenAndReviewRecords()
    {
        using var rig = new RecruitmentRig(); await rig.Start();
        var terminal = await rig.Post(); await rig.Staff("close");
        var open = await rig.Post("101", "201"); await rig.Ack("101", "201");
        open.Exempt = true;
        rig.Clock.Advance(TimeSpan.FromDays(400)); await rig.Sweep();
        Assert.DoesNotContain("100", rig.State.Listings.Keys);
        Assert.Contains("101", rig.State.Listings.Keys);
        await rig.Sweep(); Assert.DoesNotContain("100", rig.State.Listings.Keys);
        Assert.True(rig.State.DiscoveryFloor > terminal.CreatedAt);
    }

    [Fact]
    public async Task ClosingOldListingRefreshesAuthorRetentionWithoutChangingCooldownAnchor()
    {
        using var rig = new RecruitmentRig(); await rig.Start(); var post = await rig.Post(); await rig.Ack();
        rig.Clock.Advance(TimeSpan.FromDays(800));
        await rig.Staff("close");
        Assert.Equal(rig.Clock.Now, rig.State.Authors[post.OwnerId].UpdatedAt);
        Assert.Equal(post.CreatedAt, rig.State.Authors[post.OwnerId].LastAcceptedCreation);
    }

    [Fact]
    public async Task DisabledServiceDoesNotCreateStateOrPublish_ButForumsStillExcludeXp()
    {
        using var rig = new RecruitmentRig(); rig.Settings.Recruitment.Enabled = false; await rig.Start();
        Assert.Null(rig.Engine.State); Assert.False(Directory.Exists(rig.Root));
        Assert.Empty(rig.Discord.Messages); Assert.Equal(0, rig.Discord.ForumWrites);
        Assert.True(rig.Settings.Recruitment.Forums.Contains(11));
        Assert.True(rig.Settings.Recruitment.Forums.Contains(12345, 14));
        Assert.False(rig.Settings.Recruitment.Forums.Contains(12345, 15));
        Assert.False(rig.Settings.Recruitment.Forums.Contains(0));
    }

    [Theory]
    [InlineData("USD 2000", "amount mentioned; compensation unclear")]
    [InlineData("$35/hour", "concrete rate or budget stated")]
    [InlineData("150 EUR per day", "concrete rate or budget stated")]
    [InlineData("Revenue-share", "revenue-share stated")]
    [InlineData("Competitive rates", "ambiguous / discuss payment terms")]
    [InlineData("A Unity hobby project", "no concrete rate or budget found")]
    [InlineData(null, "unknown")]
    public void PaymentIsNeutralAndAdvisory(string? text, string expected) => Assert.Equal(expected, RecruitmentContent.Payment(text));

}
