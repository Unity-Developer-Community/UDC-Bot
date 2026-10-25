using Discord;
using DiscordBot.Services.Server;

namespace DiscordBot.Tests.Services;

public class AuditLogCardsTests
{
    private static readonly CardUser User = new(123456789012345678, "Display (aka username)", "https://cdn.discordapp.com/embed/avatars/0.png");
    private static readonly CardChannel Channel = new(987654321098765432, "general");
    private static readonly DateTimeOffset Now = new(2026, 10, 25, 12, 0, 0, TimeSpan.Zero);

    /// <summary>All text Discord counts toward the 4000-character message limit.</summary>
    internal static IEnumerable<string> AllText(IEnumerable<IMessageComponent> components)
    {
        foreach (var component in components)
        {
            switch (component)
            {
                case TextDisplayComponent text:
                    yield return text.Content;
                    break;
                case ContainerComponent container:
                    foreach (var t in AllText(container.Components)) yield return t;
                    break;
                case SectionComponent section:
                    foreach (var t in AllText(section.Components)) yield return t;
                    break;
            }
        }
    }

    internal static string Text(MessageComponent message) => string.Join("\n", AllText(message.Components));

    [Fact]
    public void Fit_ShortText_Unchanged()
    {
        Assert.Equal("hello", AuditCard.Fit("hello", 10));
    }

    [Fact]
    public void Fit_LongText_CutWithMarker()
    {
        var result = AuditCard.Fit(new string('a', 500), 100);
        Assert.Equal(100, result.Length);
        Assert.EndsWith(AuditCard.TruncatedMarker, result);
    }

    [Fact]
    public void Fit_MaxSmallerThanMarker_Empty()
    {
        Assert.Equal(string.Empty, AuditCard.Fit(new string('a', 500), 5));
    }

    [Fact]
    public void Fit_DoesNotSplitSurrogatePair()
    {
        var text = new string('a', 9) + "😀😀😀😀😀😀😀😀😀😀";
        var result = AuditCard.Fit(text, 10, "…");
        Assert.False(char.IsHighSurrogate(result[^2]));
        Assert.True(result.Length <= 10);
    }

    [Fact]
    public void FitLines_AllFit_JoinedUnchanged()
    {
        Assert.Equal("a\nb\nc", AuditCard.FitLines(["a", "b", "c"], 100));
    }

    [Fact]
    public void FitLines_Overflow_KeepsWholeLinesAndNotesRest()
    {
        var lines = Enumerable.Range(0, 50).Select(i => $"line {i} " + new string('x', 40)).ToList();
        var result = AuditCard.FitLines(lines, 300);
        Assert.True(result.Length <= 300);
        Assert.StartsWith("line 0 ", result);
        Assert.Contains("more lines not shown", result);
    }

    [Fact]
    public void FitLines_FirstLineTooLong_ShowsPartOfIt()
    {
        var result = AuditCard.FitLines([new string('x', 1000), "second"], 200);
        Assert.True(result.Length <= 200);
        Assert.StartsWith("xxx", result);
        Assert.Contains("1 more line not shown", result);
    }

    [Fact]
    public void DeletedMessageCard_MaximalInput_StaysWithinLimitAndKeepsIdentity()
    {
        var message = new AuditLogCards.DeletedMessage(
            User with { Name = new string('n', 300), Note = "no longer in the server" },
            Channel with { Name = new string('c', 100), ParentName = "forum", Kind = "forum post" },
            111111111111111111,
            Now.AddMinutes(-5),
            string.Join('\n', Enumerable.Repeat(new string('z', 99), 60)),
            Enumerable.Range(0, 10).Select(i => new CardAttachment(new string('f', 200) + i + ".png", 5_000_000)).ToList(),
            ["sticker one", "sticker two", "sticker three"],
            "https://discord.com/channels/1/2/3");

        var card = AuditLogCards.DeletedMessageCard(message, Now);
        var text = Text(card);

        Assert.True(text.Length <= AuditCard.DiscordTextLimit, $"Card text was {text.Length} chars");
        Assert.Contains("User ID: `123456789012345678`", text);
        Assert.Contains("<@123456789012345678>", text);
        Assert.Contains("(truncated)", text);
        Assert.Contains("Message ID: 111111111111111111", text);
    }

    [Fact]
    public void DeletedMessageCard_ForwardWithoutText_ShowsForwardedContent()
    {
        var message = new AuditLogCards.DeletedMessage(User, Channel, 1, Now, "", [], [], ForwardedContent: "free nitro here");
        var text = Text(AuditLogCards.DeletedMessageCard(message, Now));
        Assert.Contains("Forwarded message", text);
        Assert.Contains("> free nitro here", text);
    }

    [Fact]
    public void UncachedDeletedMessageCard_Builds()
    {
        var text = Text(AuditLogCards.UncachedDeletedMessageCard(1234567890123, new CardChannel(5, null), Now));
        Assert.Contains("Message ID: 1234567890123", text);
        Assert.Contains("<#5>", text);
    }

    [Fact]
    public void BulkDeletedCard_ManyLongMessages_StaysWithinLimit()
    {
        var authors = Enumerable.Range(1, 5).Select(i => User with { Id = (ulong)i, Name = $"user{i}" }).ToList();
        var messages = Enumerable.Range(0, 100)
            .Select(i => new AuditLogCards.BulkDeletedMessage(authors[i % 5], new string('m', 2000), i % 3))
            .ToList();

        var text = Text(AuditLogCards.BulkDeletedCard(Channel, messages, 7, Now));

        Assert.True(text.Length <= AuditCard.DiscordTextLimit, $"Card text was {text.Length} chars");
        Assert.Contains("107 messages deleted at once (7 not cached", text);
        Assert.Contains("more lines not shown", text);
        Assert.Contains("**user1** <@1> `1` · 20 messages", text);
    }

    [Fact]
    public void EditedMessageCard_LongVersions_StaysWithinLimitAndShowsBoth()
    {
        var edit = new AuditLogCards.EditedMessage(User, Channel, 42, "https://discord.com/channels/1/2/42", Now.AddHours(-1), Now,
            new string('b', 4000), new string('a', 4000),
            Enumerable.Range(0, 10).Select(i => new CardAttachment(new string('r', 150) + i, 1024, (ulong)i)).ToList(), []);

        var text = Text(AuditLogCards.EditedMessageCard(edit));

        Assert.True(text.Length <= AuditCard.DiscordTextLimit, $"Card text was {text.Length} chars");
        Assert.Contains("**Before**", text);
        Assert.Contains("**After**", text);
        Assert.Contains("[Jump to message](https://discord.com/channels/1/2/42)", text);
    }

    [Fact]
    public void EditedMessageCard_Uncached_ShowsAfterOnly()
    {
        var edit = new AuditLogCards.EditedMessage(User, Channel, 42, "https://x", Now, Now, null, "now this", [], []);
        var text = Text(AuditLogCards.EditedMessageCard(edit));
        Assert.Contains("previous version not cached", text);
        Assert.DoesNotContain("**Before**", text);
        Assert.Contains("> now this", text);
    }

    [Fact]
    public void DeletedThreadCard_Cached_ShowsOwnerTagsAndStarter()
    {
        var thread = new AuditLogCards.DeletedThread(
            1234567890123456789, "Help with shaders", true, new CardChannel(5, "unity-help"),
            User, User.Id, ["Shaders", "URP"], 12, "My shader is pink", [new CardAttachment("pink.png", 2048)],
            User with { Id = 99, Name = "Mod" }, true);

        var text = Text(AuditLogCards.DeletedThreadCard(thread, Now));

        Assert.Contains("Forum post deleted", text);
        Assert.Contains("Owner: <@123456789012345678>", text);
        Assert.Contains("`Shaders`, `URP`", text);
        Assert.Contains("Deleted by: <@99>", text);
        Assert.Contains("> My shader is pink", text);
        Assert.Contains("12 messages", text);
    }

    [Fact]
    public void DeletedThreadCard_UncachedNoAudit_Builds()
    {
        var thread = new AuditLogCards.DeletedThread(77, null, false, null, null, null, [], null, null, [], null, false);
        var text = Text(AuditLogCards.DeletedThreadCard(thread, Now));
        Assert.Contains("Thread deleted", text);
        Assert.Contains("Thread ID: 77", text);
        Assert.Contains("not have this thread cached", text);
    }
}
