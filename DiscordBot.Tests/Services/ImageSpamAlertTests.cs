using Discord;
using DiscordBot.Services.Moderation;
using DiscordBot.Services.Server;

namespace DiscordBot.Tests.Services;

public class ImageSpamAlertTests
{
    private static readonly CardUser User = new(123456789012345678, "Spammer", "https://cdn.discordapp.com/embed/avatars/0.png");
    private static readonly DateTimeOffset Now = new(2026, 11, 28, 12, 0, 0, TimeSpan.Zero);

    private static MessageComponent Alert() => AuditLogCards.ImageSpamAlertCard(new AuditLogCards.ImageSpamAlert(
        User, Now.AddDays(-2), Now.AddDays(-90), "exactly 1 image in 4 channels", 4, 15, "✅ 24 hours",
        "https://discord.com/channels/1/2/3", ["**Message history**", "~12 messages"]));

    private static int ActionRows(IEnumerable<IMessageComponent> components) =>
        components.Sum(c => c switch
        {
            ActionRowComponent => 1,
            ContainerComponent container => ActionRows(container.Components),
            SectionComponent section => ActionRows(section.Components),
            _ => 0,
        });

    [Theory]
    [InlineData(ImageSpamAlertAction.Ban, "🔨 Ban by <@42>")]
    [InlineData(ImageSpamAlertAction.Unmute, "✅ False positive by <@42>")]
    [InlineData(ImageSpamAlertAction.Ignore, "👀 Ignore by <@42>")]
    public void ClosedLine_NamesWhoDidWhat(ImageSpamAlertAction action, string expected)
    {
        var line = AuditLogCards.ImageSpamClosedLine(action, 42, "banned", Now);

        Assert.Equal($"-# {expected} · banned · <t:{Now.ToUnixTimeSeconds()}:f>", line);
    }

    [Fact]
    public void ClosedCard_DropsEveryActionRowAndAppendsTheOutcome()
    {
        var alert = Alert();
        Assert.Equal(1, ActionRows(alert.Components));

        var closed = AuditLogCards.ImageSpamClosedCard(alert.Components, "-# 🔨 Ban by <@42> · banned · now");

        Assert.Equal(0, ActionRows(closed.Components));
        var text = AuditLogCardsTests.Text(closed);
        Assert.Contains("Image spam guard triggered", text);
        Assert.Contains("~12 messages", text);
        Assert.Contains("[See full report](https://discord.com/channels/1/2/3)", text);
        Assert.EndsWith("-# 🔨 Ban by <@42> · banned · now", text);
        var container = Assert.Single(closed.Components.OfType<ContainerComponent>());
        Assert.Equal(AuditLogCards.SpamGuardColor.RawValue, container.AccentColor?.RawValue);
        Assert.Single(container.Components.OfType<SectionComponent>());
    }

    [Fact]
    public void ClosedCard_FromAnAlreadyClosedCard_HasNoButtonsToHandle()
    {
        var alert = Alert();
        var closed = AuditLogCards.ImageSpamClosedCard(alert.Components, "-# done");

        Assert.True(ImageSpamAlertService.HasAlertButtons(alert.Components));
        Assert.False(ImageSpamAlertService.HasAlertButtons(closed.Components));
    }

    [Fact]
    public void ResultText_ReportsEachOutcome()
    {
        var until = Now.AddHours(20);

        Assert.Equal("banned", ImageSpamAlertService.ResultText(ImageSpamAlertAction.Ban, false, null, Now));
        Assert.Equal("banned", ImageSpamAlertService.ResultText(ImageSpamAlertAction.Ban, true, null, Now));
        Assert.Equal("timeout removed", ImageSpamAlertService.ResultText(ImageSpamAlertAction.Unmute, false, null, Now));
        Assert.Equal("member has left the server", ImageSpamAlertService.ResultText(ImageSpamAlertAction.Unmute, true, null, Now));
        Assert.Equal("member has left the server", ImageSpamAlertService.ResultText(ImageSpamAlertAction.Ignore, true, null, Now));
        Assert.Equal($"left timed out until <t:{until.ToUnixTimeSeconds()}:f>", ImageSpamAlertService.ResultText(ImageSpamAlertAction.Ignore, false, until, Now));
        Assert.Equal("member is not timed out", ImageSpamAlertService.ResultText(ImageSpamAlertAction.Ignore, false, Now.AddHours(-1), Now));
        Assert.Equal("member is not timed out", ImageSpamAlertService.ResultText(ImageSpamAlertAction.Ignore, false, null, Now));
    }

    [Fact]
    public void MayAct_NeedsAdministratorOrTheModeratorRole()
    {
        Assert.True(ImageSpamAlertService.MayAct(true, [], 0));
        Assert.True(ImageSpamAlertService.MayAct(false, [1, 7], 7));
        Assert.False(ImageSpamAlertService.MayAct(false, [1, 2], 7));
        Assert.False(ImageSpamAlertService.MayAct(false, [0], 0));
    }
}
