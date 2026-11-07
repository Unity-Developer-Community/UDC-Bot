using Discord;
using DiscordBot.Domain.UnityNews;
using DiscordBot.Services.Code.Unity;
using DiscordBot.Services.Server;
using DiscordBot.Tests.Domain.UnityNews;

namespace DiscordBot.Tests.Services;

public class UnityNewsCardsTests
{
    private static ReleaseSummary BigBeta() =>
        ReleaseSummary.From(UnityNewsFixtures.Release("6000.7.0b8", UnityStream.Beta), UnityNewsFixtures.BigBetaNotes());

    private static ReleaseSummary Lts() =>
        ReleaseSummary.From(UnityNewsFixtures.Release("6000.3.28f1", UnityStream.Lts), ReleaseNotesMarkdownParser.Parse(UnityNewsFixtures.Lts));

    private static int CountComponents(IEnumerable<IMessageComponent> components) =>
        components.Sum(c => 1 + c switch
        {
            ContainerComponent container => CountComponents(container.Components),
            ActionRowComponent row => CountComponents(row.Components),
            SectionComponent section => CountComponents(section.Components),
            _ => 0,
        });

    private static IEnumerable<ButtonComponent> Buttons(IEnumerable<IMessageComponent> components) =>
        components.SelectMany(c => c switch
        {
            ContainerComponent container => Buttons(container.Components),
            ActionRowComponent row => Buttons(row.Components),
            ButtonComponent button => [button],
            _ => [],
        });

    [Fact]
    public void ReleaseCards_AreComponentsV2WithinDiscordLimits()
    {
        var cards = UnityNewsCards.ReleaseCards(BigBeta(), "<@&123>");

        Assert.True(cards.Count > 1);
        Assert.All(cards, card =>
        {
            Assert.True(AuditLogCardsTests.Text(card).Length <= AuditCard.DiscordTextLimit);
            Assert.True(CountComponents(card.Components) <= 35);
            Assert.IsType<ContainerComponent>(Assert.Single(card.Components));
        });
    }

    [Fact]
    public void ReleaseCards_RoleMentionIsOnlyOnTheFirstCard_AndNullMeansNone()
    {
        var withRole = UnityNewsCards.ReleaseCards(BigBeta(), "<@&123>");
        var without = UnityNewsCards.ReleaseCards(BigBeta());

        Assert.Contains("<@&123>", AuditLogCardsTests.Text(withRole[0]));
        Assert.All(withRole.Skip(1), card => Assert.DoesNotContain("<@&", AuditLogCardsTests.Text(card)));
        Assert.All(without, card => Assert.DoesNotContain("<@&", AuditLogCardsTests.Text(card)));
    }

    [Fact]
    public void ReleaseCards_FirstCardHasTheReleaseNotesLinkButton()
    {
        var cards = UnityNewsCards.ReleaseCards(BigBeta());

        var button = Assert.Single(Buttons(cards[0].Components));
        Assert.Equal("Release notes", button.Label);
        Assert.Equal("https://unity.com/releases/editor/beta/6000.7.0b8", button.Url);
        Assert.Empty(cards.Skip(1).SelectMany(c => Buttons(c.Components)));
    }

    [Fact]
    public void ReleaseCards_LtsIsASingleCard()
    {
        var card = Assert.Single(UnityNewsCards.ReleaseCards(Lts(), "<@&9>"));
        var text = AuditLogCardsTests.Text(card);

        Assert.Contains("<@&9>", text);
        Assert.Contains("LTS · 6000.3.28f1", text);
        Assert.Contains("### Fixes · 3", text);
    }

    [Fact]
    public void AccentColor_DiffersPerStream()
    {
        var colors = Enum.GetValues<UnityStream>().Select(UnityNewsCards.AccentColor).Distinct().ToList();

        Assert.Equal(4, colors.Count);
        var card = Assert.Single(UnityNewsCards.ReleaseCards(Lts()));
        Assert.Equal(UnityNewsCards.LtsColor, Assert.IsType<ContainerComponent>(card.Components.Single()).AccentColor);
    }

    [Fact]
    public void ForumTitle_UsesTheShortTitleAndStaysUnderTheLimit()
    {
        Assert.Equal("LTS · 6000.3.28f1", UnityNewsCards.ForumTitle(UnityNewsFixtures.Release("6000.3.28f1", UnityStream.Lts)));
        Assert.True(UnityNewsCards.ForumTitle(UnityNewsFixtures.Release(new string('9', 200), UnityStream.Lts)).Length <= UnityNewsCards.MaxForumTitleLength);
    }

    [Fact]
    public void RoleOnlyMentions_AllowsOnlyThatRole_WithoutTheRolesFlag()
    {
        var mentions = UnityNewsCards.RoleOnlyMentions(42);

        Assert.Equal([42ul], mentions.RoleIds);
        // Discord.Net throws when the Roles flag is combined with a role id list.
        Assert.False(mentions.AllowedTypes is { } types && types.HasFlag(AllowedMentionTypes.Roles));
        Assert.False(mentions.MentionRepliedUser ?? false);
    }

    [Fact]
    public void RoleOnlyMentions_WithoutRole_AllowsNone()
    {
        Assert.Same(AllowedMentions.None, UnityNewsCards.RoleOnlyMentions(null));
    }
}
