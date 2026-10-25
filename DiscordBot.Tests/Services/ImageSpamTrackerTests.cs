using DiscordBot.Services.Moderation;

namespace DiscordBot.Tests.Services;

public class ImageSpamTrackerTests
{
    private static readonly DateTimeOffset Start = new(2026, 10, 25, 12, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Window = TimeSpan.FromMinutes(15);

    private static ImageSpamHit Hit(ulong messageId, DateTimeOffset at, ulong channelId = 1) =>
        new(channelId, messageId, at, "", ["a.png", "b.png", "c.png", "d.png"]);

    [Fact]
    public void FirstHit_Recorded()
    {
        var tracker = new ImageSpamTracker(Window);
        Assert.Equal(ImageSpamOutcome.Recorded, tracker.Record(7, Hit(1, Start)).Outcome);
    }

    [Fact]
    public void SecondHitInsideWindow_TriggeredWithBothMessages()
    {
        var tracker = new ImageSpamTracker(Window);
        tracker.Record(7, Hit(1, Start, channelId: 10));
        var result = tracker.Record(7, Hit(2, Start.AddMinutes(14), channelId: 20));

        Assert.Equal(ImageSpamOutcome.Triggered, result.Outcome);
        Assert.Equal([1UL, 2UL], result.Hits.Select(h => h.MessageId));
    }

    [Fact]
    public void SecondHitAfterWindow_RecordedAgain()
    {
        var tracker = new ImageSpamTracker(Window);
        tracker.Record(7, Hit(1, Start, channelId: 10));
        var result = tracker.Record(7, Hit(2, Start.AddMinutes(16), channelId: 20));

        Assert.Equal(ImageSpamOutcome.Recorded, result.Outcome);
    }

    [Fact]
    public void HitsInSameChannel_DoNotTrigger()
    {
        var tracker = new ImageSpamTracker(Window);
        tracker.Record(7, Hit(1, Start, channelId: 10));
        Assert.Equal(ImageSpamOutcome.Recorded, tracker.Record(7, Hit(2, Start.AddMinutes(1), channelId: 10)).Outcome);
        Assert.Equal(ImageSpamOutcome.Recorded, tracker.Record(7, Hit(3, Start.AddMinutes(2), channelId: 10)).Outcome);
    }

    [Fact]
    public void SameChannelHitsThenOtherChannel_TriggeredWithAllMessages()
    {
        var tracker = new ImageSpamTracker(Window);
        tracker.Record(7, Hit(1, Start, channelId: 10));
        tracker.Record(7, Hit(2, Start.AddMinutes(1), channelId: 10));
        var result = tracker.Record(7, Hit(3, Start.AddMinutes(2), channelId: 20));

        Assert.Equal(ImageSpamOutcome.Triggered, result.Outcome);
        Assert.Equal([1UL, 2UL, 3UL], result.Hits.Select(h => h.MessageId));
    }

    [Fact]
    public void HitsFromDifferentUsers_DoNotCombine()
    {
        var tracker = new ImageSpamTracker(Window);
        tracker.Record(7, Hit(1, Start));
        Assert.Equal(ImageSpamOutcome.Recorded, tracker.Record(8, Hit(2, Start.AddMinutes(1))).Outcome);
    }

    [Fact]
    public void HitsAfterTrigger_AlreadyActionedUntilWindowEnds()
    {
        var tracker = new ImageSpamTracker(Window);
        tracker.Record(7, Hit(1, Start, channelId: 10));
        tracker.Record(7, Hit(2, Start.AddMinutes(1), channelId: 20));

        var third = tracker.Record(7, Hit(3, Start.AddMinutes(2), channelId: 30));
        Assert.Equal(ImageSpamOutcome.AlreadyActioned, third.Outcome);
        Assert.Equal([3UL], third.Hits.Select(h => h.MessageId));

        // After the actioned period, the user starts from a clean slate.
        Assert.Equal(ImageSpamOutcome.Recorded, tracker.Record(7, Hit(4, Start.AddMinutes(17))).Outcome);
    }

    private static AttachmentShape Img(string name, string? type = "image/png") => new(name, type);

    [Fact]
    public void Shape_FourImages_Matches()
    {
        Assert.True(ImageSpamShape.Matches([Img("1.png"), Img("2.png"), Img("3.jpg", "image/jpeg"), Img("4.webp", "image/webp")], null, 4));
    }

    [Theory]
    [InlineData(3)]
    [InlineData(5)]
    public void Shape_WrongCount_DoesNotMatch(int count)
    {
        var attachments = Enumerable.Range(0, count).Select(i => Img($"{i}.png")).ToList();
        Assert.False(ImageSpamShape.Matches(attachments, null, 4));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Shape_NonPositiveCount_NeverMatches(int imageCount)
    {
        // A text-only message has zero attachments, so a count of 0 must not match it.
        Assert.False(ImageSpamShape.Matches([], null, imageCount));
        Assert.False(ImageSpamShape.Matches([], [], imageCount));
    }

    [Fact]
    public void Shape_MixedImagesAndZip_DoesNotMatch()
    {
        Assert.False(ImageSpamShape.Matches([Img("1.png"), Img("2.png"), Img("3.png"), Img("build.zip", "application/zip")], null, 4));
    }

    [Fact]
    public void Shape_NoContentType_UsesExtension()
    {
        Assert.True(ImageSpamShape.Matches([Img("1.PNG", null), Img("2.jpeg", null), Img("3.gif", null), Img("4.webp", null)], null, 4));
        Assert.False(ImageSpamShape.Matches([Img("1.png", null), Img("2.png", null), Img("3.png", null), Img("4.txt", null)], null, 4));
    }

    [Fact]
    public void Shape_ForwardWithFourImages_Matches()
    {
        var forwarded = Enumerable.Range(0, 4).Select(i => Img($"{i}.png")).ToList();
        Assert.True(ImageSpamShape.Matches([], forwarded, 4));
    }

    [Fact]
    public void Shape_OwnAttachmentsTakePriorityOverForward()
    {
        var forwarded = Enumerable.Range(0, 4).Select(i => Img($"{i}.png")).ToList();
        Assert.False(ImageSpamShape.Matches([Img("only.png")], forwarded, 4));
    }

    [Fact]
    public void Registry_ContainsUntilExpiry()
    {
        var registry = new BotDeletionRegistry();
        registry.Register([1, 2], Start);

        Assert.True(registry.Contains(1, Start.AddMinutes(1)));
        Assert.False(registry.Contains(3, Start.AddMinutes(1)));
        Assert.False(registry.Contains(1, Start + BotDeletionRegistry.Lifetime));
    }
}
