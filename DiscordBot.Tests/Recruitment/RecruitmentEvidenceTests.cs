using DiscordBot.Services.Recruitment;

namespace DiscordBot.Tests.Recruitment;

public class RecruitmentEvidenceTests
{
    [Fact]
    public async Task IdentityOnlyRefreshUpdatesSameMessageWithoutInventoriesOrEnforcementAndSurvivesRestart()
    {
        using var rig = new RecruitmentRig(); await rig.Start(); var post = await rig.Post(); await rig.Ack();
        var id = post.PublicMessageId; var accepted = post.AcceptedAt; var inventories = rig.Discord.Inventories;
        rig.Discord.Threads[post.Id] = rig.Discord.Threads[post.Id] with { ServerNickname = "New @nickname", GlobalName = "Global", Username = "member", AvatarUrl = "https://cdn.discordapp.com/avatars/200/hash.png", AvatarIdentity = "hash" };
        await rig.Engine.FactsChangedAsync(post.Id, default);
        Assert.Equal("New  nickname", post.Facts.ServerNickname);
        Assert.Contains("New  nickname", rig.Discord.Messages["100:recruitment:100:public"].Message.TextFallback);
        Assert.Equal(id, post.PublicMessageId); Assert.Equal(inventories, rig.Discord.Inventories);
        Assert.Equal(accepted, post.AcceptedAt); Assert.Empty(rig.Discord.Actions);
        await rig.Restart();
        var facts = rig.State.Listings[post.Id].Facts;
        Assert.Equal("member", facts.Username); Assert.Equal("hash", facts.AvatarIdentity); Assert.Equal(9, facts.Checks.Count);
        Assert.Equal("https://cdn.discordapp.com/avatars/200/hash.png", facts.AvatarUrl);
    }

    [Fact]
    public async Task TermsChangesBeyondDisplayedExcerptAreStillRecordedAndBounded()
    {
        using var rig = new RecruitmentRig(); await rig.Start(); var post = await rig.Post();
        rig.Discord.Threads[post.Id] = rig.Discord.Threads[post.Id] with { Starter = "Budget " + new string('a', 2400) + " USD 500" };
        await rig.Engine.FactsChangedAsync(post.Id, default); await rig.Ack();
        var evidence = post.Facts.PaymentEvidence;
        rig.Discord.Threads[post.Id] = rig.Discord.Threads[post.Id] with { Starter = "Budget " + new string('a', 2400) + " USD 50" };
        await rig.Engine.FactsChangedAsync(post.Id, default);
        Assert.Equal(evidence, post.Facts.PaymentEvidence); Assert.Equal(1, post.MaterialChanges);
        Assert.Equal(2000, post.LatestStarter!.Content.Length); Assert.True(post.LatestStarter.Truncated);
    }

    [Fact]
    public async Task MaterialEditsKeepOriginalAndBoundedHistoryWithoutClosingListing()
    {
        using var rig = new RecruitmentRig(); await rig.Start(); var post = await rig.Post(); await rig.Ack();
        var original = post.OriginalStarter;
        for (var i = 0; i < 6; i++)
        {
            rig.Clock.Advance(TimeSpan.FromMinutes(1));
            rig.Discord.Threads[post.Id] = rig.Discord.Threads[post.Id] with { Starter = $"Budget AUD {100 + i}. Portfolio: https://example.org/works/{i}" };
            await rig.Engine.FactsChangedAsync(post.Id, default);
        }
        Assert.Equal(original, post.OriginalStarter); Assert.Equal(6, post.MaterialChanges);
        Assert.Equal(3, post.EarlierMaterialVersions.Count); Assert.Equal(ListingStatus.Open, post.Status);
        Assert.Empty(rig.Discord.Actions); Assert.Single(post.Attention);
        Assert.Contains("changed terms/links", rig.Engine.Health().Text());
        await rig.Restart();
        Assert.Contains("AUD 105", await rig.Staff("lookup")); Assert.Contains("AUD 500", await rig.Staff("lookup"));
        await rig.Staff("review-changes");
        Assert.DoesNotContain("changed terms/links", rig.Engine.Health().Text());
        Assert.Equal(6, rig.State.Listings[post.Id].ReviewedMaterialChanges);
    }

    [Fact]
    public async Task CosmeticEditsAndUnavailableStarterDoNotInventMaterialChangesOrEraseEvidence()
    {
        using var rig = new RecruitmentRig(); await rig.Start(); var post = await rig.Post(); await rig.Ack();
        var original = post.OriginalStarter;
        rig.Discord.Threads[post.Id] = rig.Discord.Threads[post.Id] with { Starter = "Budget AUD 500 for a short Unity project\n\nExtra description", HasCustomAvatar = false };
        await rig.Engine.FactsChangedAsync(post.Id, default);
        Assert.Equal(0, post.MaterialChanges); Assert.False(post.Facts.HasCustomAvatar);
        var latest = post.LatestStarter;
        rig.Discord.Threads[post.Id] = rig.Discord.Threads[post.Id] with { Starter = null, HasCustomAvatar = null };
        await rig.Engine.FactsChangedAsync(post.Id, default);
        Assert.Equal(original, post.OriginalStarter); Assert.Equal(latest, post.LatestStarter);
        Assert.Equal("unknown", post.Facts.Payment); Assert.Null(post.Facts.HasCustomAvatar);
        Assert.Contains("unavailable", RecruitmentPresentation.Details(post));
    }

    [Fact]
    public async Task SharedContactHintsArePrivateAndRequireSpecificLinks()
    {
        using var rig = new RecruitmentRig(); await rig.Start(); await rig.Post(); await rig.Post("101", "201");
        foreach (var id in new[] { "100", "101" })
        {
            rig.Discord.Threads[id] = rig.Discord.Threads[id] with { Starter = "Contact https://example.org/team" };
            await rig.Engine.FactsChangedAsync(id, default);
        }
        Assert.Contains("<#101>", await rig.Staff("lookup"));
        Assert.DoesNotContain("101", await rig.Engine.DetailsAsync("1", "100", "200", default));
        Assert.DoesNotContain("101", rig.Discord.Messages["100:recruitment:100:public"].Message.TextFallback);
    }
}
