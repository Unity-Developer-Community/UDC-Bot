using DiscordBot.Settings;

namespace DiscordBot.Services.Moderation;

/// <summary>A message whose attachments are all images. <paramref name="ImageCount"/> is how many it has.</summary>
public sealed record ImageSpamHit(ulong ChannelId, ulong MessageId, DateTimeOffset At, string Text, IReadOnlyList<string> FileNames, int ImageCount);

public enum ImageSpamOutcome
{
    /// <summary>No rule matches yet: remembered, no action.</summary>
    Recorded,
    /// <summary>A rule matched within the window: act on every hit returned.</summary>
    Triggered,
    /// <summary>The user was already actioned in this window: delete this message only.</summary>
    AlreadyActioned,
}

/// <summary>
/// How many distinct channels trigger each rule. A value below 1 disables that rule.
/// <see cref="ImageCount"/> is the exact count of the primary rule (<see cref="ImageCountChannels"/>); a post with
/// exactly one image uses <see cref="SingleImageChannels"/>, any other count <see cref="OtherCountChannels"/>.
/// <see cref="MixedChannels"/> counts image posts of any count.
/// </summary>
public sealed record ImageSpamThresholds(int ImageCount, int ImageCountChannels, int SingleImageChannels, int OtherCountChannels, int MixedChannels)
{
    public static ImageSpamThresholds From(ImageSpamGuardSettings settings) => new(
        settings.ImageCount, settings.ImageCountChannels, settings.SingleImageChannels, settings.OtherCountChannels, settings.MixedChannels);

    /// <summary>Distinct channels needed for posts with exactly <paramref name="imageCount"/> images (below 1: disabled).</summary>
    public int ChannelsFor(int imageCount)
    {
        if (imageCount < 1)
            return 0;
        if (imageCount == ImageCount)
            return ImageCountChannels;
        return imageCount == 1 ? SingleImageChannels : OtherCountChannels;
    }
}

public enum ImageSpamRuleKind
{
    /// <summary>Posts with the same exact image count, spread over enough channels.</summary>
    ExactCount,
    /// <summary>Image posts of any count, spread over enough channels.</summary>
    Mixed,
}

/// <summary>The rule that fired. <paramref name="ImageCount"/> is 0 for <see cref="ImageSpamRuleKind.Mixed"/>.</summary>
public sealed record ImageSpamRule(ImageSpamRuleKind Kind, int ImageCount, int Channels)
{
    public string Description => Kind == ImageSpamRuleKind.ExactCount
        ? $"exactly {ImageCount} image{(ImageCount == 1 ? "" : "s")} in {Channels} channels"
        : $"image posts in {Channels} different channels";
}

public sealed record ImageSpamResult(ImageSpamOutcome Outcome, IReadOnlyList<ImageSpamHit> Hits, ImageSpamRule? Rule = null);

/// <summary>
/// Remembers all-image messages per user in a sliding window and triggers once they span enough
/// distinct channels (see <see cref="ImageSpamThresholds"/>), so repeated screenshots in one help
/// thread don't count. Thread-safe, in memory only.
/// </summary>
public sealed class ImageSpamTracker
{
    private readonly object _lock = new();
    private readonly Dictionary<ulong, List<ImageSpamHit>> _hits = new();
    private readonly Dictionary<ulong, DateTimeOffset> _actionedUntil = new();
    private readonly ImageSpamThresholds _thresholds;

    public TimeSpan Window { get; }

    public ImageSpamTracker(TimeSpan window, ImageSpamThresholds thresholds)
    {
        Window = window;
        _thresholds = thresholds;
    }

    public ImageSpamResult Record(ulong userId, ImageSpamHit hit)
    {
        lock (_lock)
        {
            Prune(hit.At);

            if (_actionedUntil.TryGetValue(userId, out var until) && hit.At < until)
                return new ImageSpamResult(ImageSpamOutcome.AlreadyActioned, [hit]);

            if (!_hits.TryGetValue(userId, out var hits))
                _hits[userId] = hits = [];
            hits.Add(hit);

            if (Evaluate(hits, hit) is not { } rule)
                return new ImageSpamResult(ImageSpamOutcome.Recorded, [hit]);

            _hits.Remove(userId);
            _actionedUntil[userId] = hit.At + Window;
            return new ImageSpamResult(ImageSpamOutcome.Triggered, hits.ToList(), rule);
        }
    }

    /// <summary>Only the new hit can have made a rule match, so the exact-count rule checked is the hit's own.</summary>
    private ImageSpamRule? Evaluate(List<ImageSpamHit> hits, ImageSpamHit latest)
    {
        var needed = _thresholds.ChannelsFor(latest.ImageCount);
        if (needed >= 1)
        {
            var channels = hits.Where(h => h.ImageCount == latest.ImageCount).Select(h => h.ChannelId).Distinct().Count();
            if (channels >= needed)
                return new ImageSpamRule(ImageSpamRuleKind.ExactCount, latest.ImageCount, channels);
        }

        if (_thresholds.MixedChannels >= 1)
        {
            var channels = hits.Select(h => h.ChannelId).Distinct().Count();
            if (channels >= _thresholds.MixedChannels)
                return new ImageSpamRule(ImageSpamRuleKind.Mixed, 0, channels);
        }
        return null;
    }

    private void Prune(DateTimeOffset now)
    {
        foreach (var (userId, hits) in _hits.ToList())
        {
            hits.RemoveAll(h => now - h.At > Window);
            if (hits.Count == 0)
                _hits.Remove(userId);
        }
        foreach (var (userId, until) in _actionedUntil.ToList())
        {
            if (now >= until)
                _actionedUntil.Remove(userId);
        }
    }
}

public sealed record AttachmentShape(string Filename, string? ContentType);

/// <summary>The scam shape: a message whose attachments are all images (or a forward of such a message).</summary>
public static class ImageSpamShape
{
    private static readonly string[] ImageExtensions = [".png", ".jpg", ".jpeg", ".gif", ".webp"];

    public static bool IsImage(AttachmentShape attachment) =>
        attachment.ContentType != null
            ? attachment.ContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase)
            : ImageExtensions.Any(ext => attachment.Filename.EndsWith(ext, StringComparison.OrdinalIgnoreCase));

    /// <param name="attachments">The message's own attachments.</param>
    /// <param name="forwarded">Attachments of the first forwarded message, used when the message has none.</param>
    /// <returns>The number of images, or 0 when there are no attachments or any of them isn't an image.</returns>
    public static int ImageCount(IReadOnlyList<AttachmentShape> attachments, IReadOnlyList<AttachmentShape>? forwarded)
    {
        var set = attachments.Count > 0 ? attachments : forwarded ?? [];
        return set.All(IsImage) ? set.Count : 0;
    }
}
