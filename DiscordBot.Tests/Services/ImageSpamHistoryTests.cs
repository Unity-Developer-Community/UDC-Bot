using DiscordBot.Services.Moderation;

namespace DiscordBot.Tests.Services;

public class ImageSpamHistoryTests
{
    private const ulong GuildId = 555;
    private static readonly DateTimeOffset Now = new(2026, 11, 28, 12, 0, 0, TimeSpan.Zero);

    private static HistoryMessage Message(string content, int daysAgo = 1, ulong channel = 10, ulong id = 1, int attachments = 0, int embeds = 0, int stickers = 0) =>
        new(channel, id, Now.AddDays(-daysAgo), content, attachments, embeds, stickers);

    private static string Joined(HistorySearch search) => string.Join('\n', ImageSpamHistory.Lines(search, Now, GuildId));

    [Fact]
    public void NoResults_SaysNoEarlierMessages()
    {
        var text = Joined(new HistorySearch(0, []));

        Assert.Contains("No earlier messages", text);
        Assert.DoesNotContain("Last post", text);
    }

    [Fact]
    public void OnlyOldHistory_ShowsCountAndLastPostButNoExcerpts()
    {
        var old = Message("ancient hello", daysAgo: 400);
        var text = Joined(new HistorySearch(7, [old]));

        Assert.Contains("~7 earlier messages", text);
        Assert.Contains($"Last post: <t:{old.At.ToUnixTimeSeconds()}:R>", text);
        Assert.Contains("Nothing in the last 12 months", text);
        Assert.DoesNotContain("ancient hello", text);
    }

    [Fact]
    public void RecentMessages_AreShownNewestFirstAndCappedAtFour()
    {
        var messages = Enumerable.Range(1, 6).Select(i => Message($"message {i}", daysAgo: i, id: (ulong)i)).ToList();
        var lines = ImageSpamHistory.Lines(new HistorySearch(40, messages), Now, GuildId);

        var excerpts = lines.Where(l => l.StartsWith("- ")).ToList();
        Assert.Equal(4, excerpts.Count);
        Assert.Contains("message 1", excerpts[0]);
        Assert.Contains("message 4", excerpts[3]);
        Assert.Contains("~40 earlier messages", lines);
        Assert.DoesNotContain("Nothing in the last 12 months", lines);
    }

    [Fact]
    public void Excerpt_HasChannelTimeAndJumpLink()
    {
        var message = Message("hi", channel: 10, id: 99);
        var line = Assert.Single(ImageSpamHistory.Lines(new HistorySearch(1, [message]), Now, GuildId), l => l.StartsWith("- "));

        Assert.Contains("<#10>", line);
        Assert.Contains($"<t:{message.At.ToUnixTimeSeconds()}:R>", line);
        Assert.Contains("hi", line);
        Assert.Contains($"(https://discord.com/channels/{GuildId}/10/99)", line);
    }

    [Fact]
    public void LongMessage_IsCutToEightyCharactersWithEllipsis()
    {
        var line = Assert.Single(ImageSpamHistory.Lines(new HistorySearch(1, [Message(new string('a', 200))]), Now, GuildId), l => l.StartsWith("- "));

        var excerpt = line.Split(" · ")[2];
        Assert.Equal(ImageSpamHistory.MaxExcerptLength, excerpt.Length);
        Assert.EndsWith("…", excerpt);
    }

    [Fact]
    public void Newlines_AreCollapsedAndMarkdownIsEscaped()
    {
        var line = Assert.Single(ImageSpamHistory.Lines(new HistorySearch(1, [Message("first *bold*\nsecond _line_\n\n")]), Now, GuildId),
            l => l.StartsWith("- "));

        Assert.DoesNotContain("\n", line);
        Assert.Contains(@"first \*bold\* second \_line\_", line);
    }

    [Theory]
    [InlineData(2, 0, 0, "[2 attachments]")]
    [InlineData(1, 0, 0, "[1 attachment]")]
    [InlineData(0, 1, 0, "[embed]")]
    [InlineData(0, 0, 1, "[sticker]")]
    [InlineData(0, 0, 0, "[no text]")]
    public void EmptyContent_ShowsPlaceholder(int attachments, int embeds, int stickers, string placeholder)
    {
        var message = Message("  \n ", attachments: attachments, embeds: embeds, stickers: stickers);
        var line = Assert.Single(ImageSpamHistory.Lines(new HistorySearch(1, [message]), Now, GuildId), l => l.StartsWith("- "));

        Assert.Contains($" · {placeholder} · [jump]", line);
    }

    [Fact]
    public void Unavailable_ShowsReasonAndNoCounts()
    {
        var text = Joined(HistorySearch.Unavailable("Discord is still indexing this server's messages"));

        Assert.Contains("History unavailable: Discord is still indexing", text);
        Assert.DoesNotContain("earlier message", text);
    }

    [Theory]
    [InlineData(ImageSpamAlertAction.Ignore, "imgspam:ignore:42")]
    [InlineData(ImageSpamAlertAction.Ban, "imgspam:ban:42")]
    [InlineData(ImageSpamAlertAction.Unmute, "imgspam:unmute:42")]
    public void AlertIds_RoundTrip(ImageSpamAlertAction action, string expected)
    {
        var id = ImageSpamAlertIds.Build(action, 42);

        Assert.Equal(expected, id);
        Assert.True(ImageSpamAlertIds.TryParse(id, out var parsed, out var userId));
        Assert.Equal(action, parsed);
        Assert.Equal(42UL, userId);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("imgspam")]
    [InlineData("imgspam:ban")]
    [InlineData("imgspam:ban:abc")]
    [InlineData("imgspam:ban:0")]
    [InlineData("imgspam:kick:42")]
    [InlineData("imgspam:ban:42:extra")]
    [InlineData("other:ban:42")]
    [InlineData("share:ban:42")]
    public void AlertIds_RejectForeignOrMalformed(string? customId)
    {
        Assert.False(ImageSpamAlertIds.TryParse(customId, out _, out _));
    }
}
