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
}
