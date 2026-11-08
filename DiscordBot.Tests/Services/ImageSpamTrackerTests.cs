using DiscordBot.Services.Moderation;
using DiscordBot.Settings;

namespace DiscordBot.Tests.Services;

public class ImageSpamTrackerTests
{
    private static readonly DateTimeOffset Start = new(2026, 10, 25, 12, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Window = TimeSpan.FromMinutes(15);

    private static readonly ImageSpamThresholds Defaults = new(SingleImageChannels: 4, FourImageChannels: 2, OtherCountChannels: 3, MixedChannels: 6);

    private static ImageSpamTracker Tracker(ImageSpamThresholds? thresholds = null) => new(Window, thresholds ?? Defaults);

    private static ImageSpamHit Hit(ulong messageId, DateTimeOffset at, ulong channelId = 1, int images = 4) =>
        new(channelId, messageId, at, "", Enumerable.Range(0, images).Select(i => $"{i}.png").ToList(), images);

    [Fact]
    public void FirstHit_Recorded()
    {
        Assert.Equal(ImageSpamOutcome.Recorded, Tracker().Record(7, Hit(1, Start)).Outcome);
    }

    [Fact]
    public void FourImagesInTwoChannels_TriggeredWithBothMessages()
    {
        var tracker = Tracker();
        tracker.Record(7, Hit(1, Start, channelId: 10));
        var result = tracker.Record(7, Hit(2, Start.AddMinutes(14), channelId: 20));

        Assert.Equal(ImageSpamOutcome.Triggered, result.Outcome);
        Assert.Equal([1UL, 2UL], result.Hits.Select(h => h.MessageId));
        Assert.Equal(new ImageSpamRule(ImageSpamRuleKind.ExactCount, 4, 2), result.Rule);
        Assert.Equal("exactly 4 images in 2 channels", result.Rule!.Description);
    }

    [Fact]
    public void SecondHitAfterWindow_RecordedAgain()
    {
        var tracker = Tracker();
        tracker.Record(7, Hit(1, Start, channelId: 10));
        var result = tracker.Record(7, Hit(2, Start.AddMinutes(16), channelId: 20));

        Assert.Equal(ImageSpamOutcome.Recorded, result.Outcome);
    }

    [Fact]
    public void HitsInSameChannel_DoNotTrigger()
    {
        var tracker = Tracker();
        tracker.Record(7, Hit(1, Start, channelId: 10));
        Assert.Equal(ImageSpamOutcome.Recorded, tracker.Record(7, Hit(2, Start.AddMinutes(1), channelId: 10)).Outcome);
        Assert.Equal(ImageSpamOutcome.Recorded, tracker.Record(7, Hit(3, Start.AddMinutes(2), channelId: 10)).Outcome);
    }

    [Fact]
    public void SameChannelHitsThenOtherChannel_TriggeredWithAllMessages()
    {
        var tracker = Tracker();
        tracker.Record(7, Hit(1, Start, channelId: 10));
        tracker.Record(7, Hit(2, Start.AddMinutes(1), channelId: 10));
        var result = tracker.Record(7, Hit(3, Start.AddMinutes(2), channelId: 20));

        Assert.Equal(ImageSpamOutcome.Triggered, result.Outcome);
        Assert.Equal([1UL, 2UL, 3UL], result.Hits.Select(h => h.MessageId));
    }

    [Fact]
    public void HitsFromDifferentUsers_DoNotCombine()
    {
        var tracker = Tracker();
        tracker.Record(7, Hit(1, Start, channelId: 10));
        Assert.Equal(ImageSpamOutcome.Recorded, tracker.Record(8, Hit(2, Start.AddMinutes(1), channelId: 20)).Outcome);
    }

    [Fact]
    public void HitsAfterTrigger_AlreadyActionedUntilWindowEnds()
    {
        var tracker = Tracker();
        tracker.Record(7, Hit(1, Start, channelId: 10));
        tracker.Record(7, Hit(2, Start.AddMinutes(1), channelId: 20));

        var third = tracker.Record(7, Hit(3, Start.AddMinutes(2), channelId: 30));
        Assert.Equal(ImageSpamOutcome.AlreadyActioned, third.Outcome);
        Assert.Equal([3UL], third.Hits.Select(h => h.MessageId));

        // After the actioned period, the user starts from a clean slate.
        Assert.Equal(ImageSpamOutcome.Recorded, tracker.Record(7, Hit(4, Start.AddMinutes(17))).Outcome);
    }

    /// <summary>Records one hit per channel (10, 11, ...) with the given image count and returns the last result.</summary>
    private static ImageSpamResult RecordAcrossChannels(ImageSpamTracker tracker, int images, int channels)
    {
        ImageSpamResult? result = null;
        for (var i = 0; i < channels; i++)
            result = tracker.Record(7, Hit((ulong)(i + 1), Start.AddSeconds(i), channelId: (ulong)(10 + i), images: images));
        return result!;
    }

    [Theory]
    [InlineData(1, 4)]
    [InlineData(2, 3)]
    [InlineData(3, 3)]
    [InlineData(5, 3)]
    public void ExactCountRule_TriggersAtItsChannelCount(int images, int channels)
    {
        var tracker = Tracker();
        var before = RecordAcrossChannels(tracker, images, channels - 1);
        Assert.Equal(ImageSpamOutcome.Recorded, before.Outcome);

        var result = tracker.Record(7, Hit(99, Start.AddMinutes(1), channelId: 99, images: images));
        Assert.Equal(ImageSpamOutcome.Triggered, result.Outcome);
        Assert.Equal(new ImageSpamRule(ImageSpamRuleKind.ExactCount, images, channels), result.Rule);
        Assert.Equal(channels, result.Hits.Count);
    }

    [Fact]
    public void SingleImage_ThreeChannels_DoesNotTrigger()
    {
        Assert.Equal(ImageSpamOutcome.Recorded, RecordAcrossChannels(Tracker(), images: 1, channels: 3).Outcome);
    }

    [Fact]
    public void SingleImage_DescriptionIsSingular()
    {
        Assert.Equal("exactly 1 image in 4 channels", RecordAcrossChannels(Tracker(), images: 1, channels: 4).Rule!.Description);
    }

    [Fact]
    public void ExactCountRule_ChannelsAreCountedPerImageCount()
    {
        // 1, 2 and 3 images in three channels each of one: no exact count reaches its limit and the mixed limit is 6.
        var tracker = Tracker();
        tracker.Record(7, Hit(1, Start, channelId: 10, images: 1));
        tracker.Record(7, Hit(2, Start, channelId: 11, images: 2));
        Assert.Equal(ImageSpamOutcome.Recorded, tracker.Record(7, Hit(3, Start, channelId: 12, images: 3)).Outcome);
    }

    [Fact]
    public void Mixed_SixChannelsWithVariedCounts_Triggers()
    {
        var tracker = Tracker();
        // At most two channels per count, so no exact-count rule fires first.
        int[] counts = [1, 1, 2, 2, 3, 3];
        var results = counts
            .Select((count, i) => tracker.Record(7, Hit((ulong)(i + 1), Start.AddSeconds(i), channelId: (ulong)(10 + i), images: count)))
            .ToList();
        var result = results[^1];

        Assert.Equal(ImageSpamOutcome.Triggered, result.Outcome);
        Assert.Equal(new ImageSpamRule(ImageSpamRuleKind.Mixed, 0, 6), result.Rule);
        Assert.Equal("image posts in 6 different channels", result.Rule!.Description);
        Assert.Equal(6, result.Hits.Count);
    }

    [Fact]
    public void Mixed_FiveChannels_DoesNotTrigger()
    {
        var tracker = Tracker();
        int[] counts = [1, 1, 2, 2, 3];
        ImageSpamResult? result = null;
        for (var i = 0; i < counts.Length; i++)
            result = tracker.Record(7, Hit((ulong)(i + 1), Start.AddSeconds(i), channelId: (ulong)(10 + i), images: counts[i]));

        Assert.Equal(ImageSpamOutcome.Recorded, result!.Outcome);
    }

    [Fact]
    public void Mixed_ManyPostsInFiveChannels_DoesNotTrigger()
    {
        // Six posts, but only five distinct channels, and no count reaches its own channel limit.
        var tracker = Tracker();
        (ulong Channel, int Images)[] posts = [(10, 1), (10, 1), (11, 1), (12, 2), (13, 2), (14, 3)];
        ImageSpamResult? result = null;
        for (var i = 0; i < posts.Length; i++)
            result = tracker.Record(7, Hit((ulong)(i + 1), Start.AddSeconds(i), posts[i].Channel, posts[i].Images));

        Assert.Equal(ImageSpamOutcome.Recorded, result!.Outcome);
    }

    [Fact]
    public void ManyPostsInOneChannel_NeverTrigger()
    {
        var tracker = Tracker();
        for (var i = 0; i < 20; i++)
            Assert.Equal(ImageSpamOutcome.Recorded, tracker.Record(7, Hit((ulong)(i + 1), Start.AddSeconds(i), channelId: 10, images: i % 5 + 1)).Outcome);
    }

    [Fact]
    public void ExactCountRule_IgnoresHitsOutsideWindow()
    {
        var tracker = Tracker();
        tracker.Record(7, Hit(1, Start, channelId: 10, images: 1));
        tracker.Record(7, Hit(2, Start.AddMinutes(1), channelId: 11, images: 1));
        tracker.Record(7, Hit(3, Start.AddMinutes(2), channelId: 12, images: 1));
        Assert.Equal(ImageSpamOutcome.Recorded, tracker.Record(7, Hit(4, Start.AddMinutes(16.5), channelId: 13, images: 1)).Outcome);
    }

    [Fact]
    public void Trigger_ReturnsAllTrackedPostsOfAnyCount()
    {
        var tracker = Tracker();
        tracker.Record(7, Hit(1, Start, channelId: 10, images: 2));
        tracker.Record(7, Hit(2, Start, channelId: 11, images: 3));
        tracker.Record(7, Hit(3, Start, channelId: 12, images: 4));
        var result = tracker.Record(7, Hit(4, Start, channelId: 13, images: 4));

        Assert.Equal(ImageSpamOutcome.Triggered, result.Outcome);
        Assert.Equal([1UL, 2UL, 3UL, 4UL], result.Hits.Select(h => h.MessageId));
    }

    [Fact]
    public void DisabledRule_NeverFires()
    {
        var noSingle = Defaults with { SingleImageChannels = 0 };
        Assert.Equal(ImageSpamOutcome.Recorded, RecordAcrossChannels(Tracker(noSingle), images: 1, channels: 5).Outcome);

        var noOther = Defaults with { OtherCountChannels = 0 };
        Assert.Equal(ImageSpamOutcome.Recorded, RecordAcrossChannels(Tracker(noOther), images: 2, channels: 5).Outcome);

        var noPrimary = Defaults with { FourImageChannels = 0 };
        Assert.Equal(ImageSpamOutcome.Recorded, RecordAcrossChannels(Tracker(noPrimary), images: 4, channels: 5).Outcome);

        var noMixed = Defaults with { MixedChannels = 0 };
        var tracker = Tracker(noMixed);
        int[] counts = [1, 1, 2, 2, 3, 3, 5, 5];
        ImageSpamResult? result = null;
        for (var i = 0; i < counts.Length; i++)
            result = tracker.Record(7, Hit((ulong)(i + 1), Start.AddSeconds(i), channelId: (ulong)(10 + i), images: counts[i]));
        Assert.Equal(ImageSpamOutcome.Recorded, result!.Outcome);
    }

    [Fact]
    public void FromSettings_DisabledOrInvalidRuleIsOff()
    {
        var settings = new ImageSpamGuardSettings { Enabled = true };
        Assert.Equal(Defaults, ImageSpamThresholds.From(settings));

        settings.SingleImage.Enabled = false;
        settings.Mixed.Channels = 0;
        Assert.Equal(Defaults with { SingleImageChannels = 0, MixedChannels = 0 }, ImageSpamThresholds.From(settings));
    }

    private static AttachmentShape Img(string name, string? type = "image/png") => new(name, type);

    [Fact]
    public void Shape_AllImages_CountsThem()
    {
        Assert.Equal(4, ImageSpamShape.ImageCount([Img("1.png"), Img("2.png"), Img("3.jpg", "image/jpeg"), Img("4.webp", "image/webp")], null));
        Assert.Equal(1, ImageSpamShape.ImageCount([Img("1.png")], null));
        Assert.Equal(7, ImageSpamShape.ImageCount(Enumerable.Range(0, 7).Select(i => Img($"{i}.png")).ToList(), null));
    }

    [Fact]
    public void Shape_NoAttachments_IsZero()
    {
        // A text-only message must never be counted.
        Assert.Equal(0, ImageSpamShape.ImageCount([], null));
        Assert.Equal(0, ImageSpamShape.ImageCount([], []));
    }

    [Fact]
    public void Shape_MixedImagesAndZip_IsZero()
    {
        Assert.Equal(0, ImageSpamShape.ImageCount([Img("1.png"), Img("2.png"), Img("3.png"), Img("build.zip", "application/zip")], null));
    }

    [Fact]
    public void Shape_NoContentType_UsesExtension()
    {
        Assert.Equal(4, ImageSpamShape.ImageCount([Img("1.PNG", null), Img("2.jpeg", null), Img("3.gif", null), Img("4.webp", null)], null));
        Assert.Equal(0, ImageSpamShape.ImageCount([Img("1.png", null), Img("2.png", null), Img("3.png", null), Img("4.txt", null)], null));
    }

    [Fact]
    public void Shape_ForwardOfImages_CountsThem()
    {
        var forwarded = Enumerable.Range(0, 4).Select(i => Img($"{i}.png")).ToList();
        Assert.Equal(4, ImageSpamShape.ImageCount([], forwarded));
        Assert.Equal(0, ImageSpamShape.ImageCount([], [Img("a.png"), Img("b.zip", "application/zip")]));
    }

    [Fact]
    public void Shape_OwnAttachmentsTakePriorityOverForward()
    {
        var forwarded = Enumerable.Range(0, 4).Select(i => Img($"{i}.png")).ToList();
        Assert.Equal(1, ImageSpamShape.ImageCount([Img("only.png")], forwarded));
        Assert.Equal(0, ImageSpamShape.ImageCount([Img("notes.txt", "text/plain")], forwarded));
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
