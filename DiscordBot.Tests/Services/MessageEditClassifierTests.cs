using DiscordBot.Services.Server;

namespace DiscordBot.Tests.Services;

public class MessageEditClassifierTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 25, 12, 0, 0, TimeSpan.Zero);
    private static readonly CardAttachment Image = new("shot.png", 1000, 11);
    private static readonly CardAttachment Zip = new("project.zip", 5000, 12);

    private static MessageVersion Version(string content, DateTimeOffset? editedAt, params CardAttachment[] attachments) =>
        new(content, attachments, editedAt);

    [Fact]
    public void EmbedUnfurl_NoEditedTimestamp_Ignored()
    {
        var before = Version("look https://unity.com", null);
        var after = Version("look https://unity.com", null);
        Assert.Equal(EditKind.Ignore, MessageEditClassifier.Classify(before, after, Now).Kind);
    }

    [Fact]
    public void AttachmentMessageUnfurl_UncachedBefore_NoEditedTimestamp_Ignored()
    {
        // The old code logged this case as an empty edit.
        var after = Version("my build https://example.com", null, Image);
        Assert.Equal(EditKind.Ignore, MessageEditClassifier.Classify(null, after, Now).Kind);
    }

    [Fact]
    public void PinOfOldEditedMessage_Unchanged_Ignored()
    {
        var edited = Now.AddDays(-3);
        var before = Version("text", edited, Image);
        var after = Version("text", edited, Image);
        Assert.Equal(EditKind.Ignore, MessageEditClassifier.Classify(before, after, Now).Kind);
    }

    [Fact]
    public void PinOfOldEditedMessage_Uncached_Ignored()
    {
        var after = Version("text", Now.AddDays(-3), Image);
        Assert.Equal(EditKind.Ignore, MessageEditClassifier.Classify(null, after, Now).Kind);
    }

    [Fact]
    public void UnfurlAfterEdit_SameContent_Ignored()
    {
        var edited = Now.AddSeconds(-1);
        var before = Version("edited https://unity.com", edited);
        var after = Version("edited https://unity.com", edited);
        Assert.Equal(EditKind.Ignore, MessageEditClassifier.Classify(before, after, Now).Kind);
    }

    [Fact]
    public void RealTextEdit_Edited()
    {
        var before = Version("helo", null);
        var after = Version("hello", Now);
        var decision = MessageEditClassifier.Classify(before, after, Now);
        Assert.Equal(EditKind.Edited, decision.Kind);
        Assert.True(decision.ContentChanged);
        Assert.Empty(decision.Removed);
    }

    [Fact]
    public void AttachmentRemoved_ReportsRemovedOnly()
    {
        var before = Version("files", null, Image, Zip);
        var after = Version("files", Now, Image);
        var decision = MessageEditClassifier.Classify(before, after, Now);
        Assert.Equal(EditKind.Edited, decision.Kind);
        Assert.False(decision.ContentChanged);
        Assert.Equal([Zip], decision.Removed);
        Assert.Empty(decision.Added);
    }

    [Fact]
    public void UncachedRecentEdit_EditedUncached()
    {
        var after = Version("new text", Now.AddSeconds(-5));
        Assert.Equal(EditKind.EditedUncached, MessageEditClassifier.Classify(null, after, Now).Kind);
    }

    [Fact]
    public void UncachedEdit_AlreadyLogged_Ignored()
    {
        var edited = Now.AddSeconds(-5);
        var after = Version("new text", edited);
        Assert.Equal(EditKind.Ignore, MessageEditClassifier.Classify(null, after, Now, lastLoggedEditedAt: edited).Kind);
    }

    [Fact]
    public void UncachedEdit_NewerEditThanLogged_EditedUncached()
    {
        var after = Version("newer text", Now.AddSeconds(-1));
        Assert.Equal(EditKind.EditedUncached, MessageEditClassifier.Classify(null, after, Now, lastLoggedEditedAt: Now.AddSeconds(-30)).Kind);
    }
}
