namespace DiscordBot.Services.Server;

/// <summary>Plain snapshot of a message version, for comparing before and after an update.</summary>
public sealed record MessageVersion(string Content, IReadOnlyList<CardAttachment> Attachments, DateTimeOffset? EditedAt)
{
    public static MessageVersion From(IMessage message) =>
        new(message.Content,
            message.Attachments.Select(a => new CardAttachment(a.Filename, a.Size, a.Id)).ToList(),
            message.EditedTimestamp);
}

public enum EditKind
{
    /// <summary>Not an author edit (link unfurl, pin, thread created, embeds suppressed, ...).</summary>
    Ignore,
    /// <summary>Author edit with the previous version known.</summary>
    Edited,
    /// <summary>Recent author edit, but the previous version wasn't cached.</summary>
    EditedUncached,
}

public sealed record EditDecision(EditKind Kind, bool ContentChanged, IReadOnlyList<CardAttachment> Removed, IReadOnlyList<CardAttachment> Added)
{
    public static readonly EditDecision Ignore = new(EditKind.Ignore, false, [], []);
}

/// <summary>
/// Decides whether a message update is a real edit by its author. Discord also sends updates for
/// embed unfurls, pins and flag changes; those don't change the content or set a new edited time.
/// </summary>
public static class MessageEditClassifier
{
    /// <summary>
    /// Without the previous version, an update only counts when it was edited this recently.
    /// Older edited timestamps come from updates like pinning a message that was edited long ago.
    /// </summary>
    public static readonly TimeSpan UncachedEditMaxAge = TimeSpan.FromMinutes(2);

    /// <param name="before">The cached previous version, or null when it wasn't cached.</param>
    /// <param name="after">The version after the update.</param>
    /// <param name="now">Current time.</param>
    /// <param name="lastLoggedEditedAt">Edited time already logged for this message when uncached, to skip the unfurl update that follows an edit.</param>
    public static EditDecision Classify(MessageVersion? before, MessageVersion after, DateTimeOffset now, DateTimeOffset? lastLoggedEditedAt = null)
    {
        if (after.EditedAt == null)
            return EditDecision.Ignore;

        if (before == null)
        {
            if (now - after.EditedAt.Value > UncachedEditMaxAge || after.EditedAt == lastLoggedEditedAt)
                return EditDecision.Ignore;
            return new EditDecision(EditKind.EditedUncached, true, [], []);
        }

        var contentChanged = before.Content != after.Content;
        var afterIds = after.Attachments.Select(a => a.Id).ToHashSet();
        var beforeIds = before.Attachments.Select(a => a.Id).ToHashSet();
        var removed = before.Attachments.Where(a => !afterIds.Contains(a.Id)).ToList();
        var added = after.Attachments.Where(a => !beforeIds.Contains(a.Id)).ToList();

        if (!contentChanged && removed.Count == 0 && added.Count == 0)
            return EditDecision.Ignore;
        return new EditDecision(EditKind.Edited, contentChanged, removed, added);
    }
}
