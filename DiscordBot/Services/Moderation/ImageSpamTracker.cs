namespace DiscordBot.Services.Moderation;

/// <summary>A message that matched the scam shape.</summary>
public sealed record ImageSpamHit(ulong ChannelId, ulong MessageId, DateTimeOffset At, string Text, IReadOnlyList<string> FileNames);

public enum ImageSpamOutcome
{
    /// <summary>First match in the window: remembered, no action.</summary>
    Recorded,
    /// <summary>Second match in the window: act on every hit returned.</summary>
    Triggered,
    /// <summary>The user was already actioned in this window: delete this message only.</summary>
    AlreadyActioned,
}

public sealed record ImageSpamResult(ImageSpamOutcome Outcome, IReadOnlyList<ImageSpamHit> Hits);

/// <summary>
/// Counts scam-shaped messages per user in a sliding window. Thread-safe, in memory only.
/// </summary>
public sealed class ImageSpamTracker
{
    private readonly object _lock = new();
    private readonly Dictionary<ulong, List<ImageSpamHit>> _hits = new();
    private readonly Dictionary<ulong, DateTimeOffset> _actionedUntil = new();

    public TimeSpan Window { get; }

    public ImageSpamTracker(TimeSpan window) => Window = window;

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

            if (hits.Count < 2)
                return new ImageSpamResult(ImageSpamOutcome.Recorded, [hit]);

            _hits.Remove(userId);
            _actionedUntil[userId] = hit.At + Window;
            return new ImageSpamResult(ImageSpamOutcome.Triggered, hits.ToList());
        }
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

/// <summary>The scam shape: exactly N attachments, all images (or a forward of such a message).</summary>
public static class ImageSpamShape
{
    private static readonly string[] ImageExtensions = [".png", ".jpg", ".jpeg", ".gif", ".webp"];

    public static bool IsImage(AttachmentShape attachment) =>
        attachment.ContentType != null
            ? attachment.ContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase)
            : ImageExtensions.Any(ext => attachment.Filename.EndsWith(ext, StringComparison.OrdinalIgnoreCase));

    /// <param name="attachments">The message's own attachments.</param>
    /// <param name="forwarded">Attachments of the first forwarded message, used when the message has none.</param>
    /// <param name="imageCount">Exact number of images that makes a match.</param>
    public static bool Matches(IReadOnlyList<AttachmentShape> attachments, IReadOnlyList<AttachmentShape>? forwarded, int imageCount)
    {
        var set = attachments.Count > 0 ? attachments : forwarded ?? [];
        return set.Count == imageCount && set.All(IsImage);
    }
}
