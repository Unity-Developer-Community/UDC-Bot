using DiscordBot.Domain.UnityNews;
using DiscordBot.Services.Server;

namespace DiscordBot.Services.Code.Unity;

/// <summary>Builds the Components V2 messages of the Unity news posts.</summary>
public static class UnityNewsCards
{
    public const int MaxForumTitleLength = 100;

    public static readonly Color LtsColor = new(70, 130, 230);
    public static readonly Color SupportedColor = new(80, 190, 120);
    public static readonly Color BetaColor = new(240, 160, 60);
    public static readonly Color AlphaColor = new(170, 110, 220);

    public static Color AccentColor(UnityStream stream) => stream switch
    {
        UnityStream.Alpha => AlphaColor,
        UnityStream.Beta => BetaColor,
        UnityStream.Supported => SupportedColor,
        _ => LtsColor,
    };

    /// <summary>Forum post title, for example <c>LTS · 6000.3.28f1</c>.</summary>
    public static string ForumTitle(UnityRelease release) => AuditCard.Fit(release.Title, MaxForumTitleLength, "…");

    /// <summary>
    /// The messages of a release post: the first one opens the forum thread, the others are replies.
    /// <paramref name="roleMention"/> (for example <c>&lt;@&amp;123&gt;</c>) is the first line of the first message; null adds none.
    /// </summary>
    public static IReadOnlyList<MessageComponent> ReleaseCards(ReleaseSummary summary, string? roleMention = null)
    {
        var messages = ReleaseSummaryFormatter.Format(summary, roleMention);
        var accent = AccentColor(summary.Latest.Stream);
        var cards = new List<MessageComponent>(messages.Count);
        for (var i = 0; i < messages.Count; i++)
            cards.Add(ReleaseCard(messages[i], accent, i == 0 ? summary.Latest.PageUrl : null));
        return cards;
    }

    private static MessageComponent ReleaseCard(ReleaseMessage message, Color accent, string? linkUrl)
    {
        var container = new ContainerBuilder().WithAccentColor(accent);
        var blocks = message.Blocks;
        for (var i = 0; i < blocks.Count; i++)
        {
            // Keeps a message under Discord's text limit even if a block grew past its budget.
            container.WithTextDisplay(AuditCard.Fit(blocks[i], ReleaseSummaryFormatter.MessageBudget));
            // The header of the first message is set apart from the sections.
            if (linkUrl != null && i == 0 && blocks.Count > 1)
                container.WithSeparator();
        }

        if (linkUrl != null)
            container.WithActionRow(new ActionRowBuilder().WithButton(ButtonBuilder.CreateLinkButton("Release notes", linkUrl)));

        return new ComponentBuilderV2().WithContainer(container).Build();
    }
}
