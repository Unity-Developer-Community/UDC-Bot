namespace DiscordBot.Domain.UnityNews;

/// <summary>When blog cards go to the discussion channel, which people are using for their own conversations.</summary>
public static class BlogDiscussion
{
    /// <summary>How long the channel must have had no human message before a card is posted into it.</summary>
    public static readonly TimeSpan IdleTime = TimeSpan.FromMinutes(10);

    /// <summary>The longest a card waits for a quiet channel; after that it is posted anyway, so a busy channel can't hold it forever.</summary>
    public static readonly TimeSpan MaxWait = TimeSpan.FromHours(3);

    /// <summary>How many of the channel's newest messages are read to find the last human one.</summary>
    public const int HistoryScanLimit = 20;

    /// <summary>
    /// True when a card queued at <paramref name="queuedAt"/> can be posted: no human message among the newest ones
    /// (<paramref name="lastHumanMessage"/> null), the last one is at least <see cref="IdleTime"/> old, or the card has waited <see cref="MaxWait"/>.
    /// </summary>
    public static bool CardDue(DateTimeOffset now, DateTimeOffset queuedAt, DateTimeOffset? lastHumanMessage) =>
        lastHumanMessage is not { } last || now - last >= IdleTime || now - queuedAt >= MaxWait;

    /// <summary>How long after the last card <c>!blog</c> can post it again.</summary>
    public static readonly TimeSpan RepostCooldown = TimeSpan.FromMinutes(60);

    /// <summary><c>!blog</c> doesn't post the card again while the last one is among this many of the channel's newest messages.</summary>
    public const int RepostRecentMessages = 10;

    /// <summary>
    /// True when <c>!blog</c> may post the latest card again: the last card (<paramref name="lastCardMessageId"/>, 0 when none) was sent at least
    /// <see cref="RepostCooldown"/> ago and is not among <paramref name="recentMessageIds"/>, the channel's newest messages.
    /// </summary>
    public static bool RepostAllowed(DateTimeOffset now, DateTimeOffset lastCardAt, ulong lastCardMessageId, IEnumerable<ulong> recentMessageIds) =>
        now - lastCardAt >= RepostCooldown && (lastCardMessageId == 0 || !recentMessageIds.Contains(lastCardMessageId));

    /// <summary>The least time between two cards in the discussion channel, shared ones included.</summary>
    public static readonly TimeSpan MinGap = TimeSpan.FromHours(4);

    /// <summary>A queued card this old is dropped: the post has had its day, and anyone could have shared it from the news channel.</summary>
    public static readonly TimeSpan MaxCardAge = TimeSpan.FromHours(24);

    /// <summary>
    /// The paced delivery of queued cards: <c>Expired</c> are queued at least <see cref="MaxCardAge"/> ago and are dropped; <c>Next</c>
    /// is the oldest other card when it can go out now, or null. A card waits <see cref="MinGap"/> after the last card
    /// (<paramref name="lastCardAt"/>, default when none), then for a quiet channel as in <see cref="CardDue"/>, counted from when the gap
    /// ended or it was queued, whichever is later.
    /// </summary>
    public static (IReadOnlyList<T> Expired, T? Next) NextCard<T>(DateTimeOffset now, IEnumerable<T> queue, Func<T, DateTimeOffset> queuedAt,
        DateTimeOffset lastCardAt, DateTimeOffset? lastHumanMessage) where T : class
    {
        var expired = new List<T>();
        var live = new List<T>();
        foreach (var card in queue)
            (now - queuedAt(card) >= MaxCardAge ? expired : live).Add(card);

        var gapEnds = lastCardAt == default ? DateTimeOffset.MinValue : lastCardAt + MinGap;
        var oldest = live.OrderBy(queuedAt).FirstOrDefault();
        if (oldest == null || now < gapEnds)
            return (expired, null);

        var waitingSince = queuedAt(oldest) > gapEnds ? queuedAt(oldest) : gapEnds;
        return (expired, CardDue(now, waitingSince, lastHumanMessage) ? oldest : null);
    }

    /// <summary>
    /// True when the last card (<paramref name="lastCardMessageId"/>, 0 when none) should be deleted as the next one is posted: it is
    /// among <paramref name="recentMessages"/>, the channel's newest messages, and no human wrote after it, so nobody is talking about it.
    /// </summary>
    public static bool ShouldReplace(ulong lastCardMessageId, IEnumerable<(ulong Id, bool Human)> recentMessages)
    {
        if (lastCardMessageId == 0)
            return false;
        var recent = recentMessages.ToList();
        return recent.Any(m => m.Id == lastCardMessageId) && !recent.Any(m => m.Human && m.Id > lastCardMessageId);
    }
}
