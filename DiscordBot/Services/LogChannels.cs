using Discord.WebSocket;
using DiscordBot.Settings;

namespace DiscordBot.Services.Logging;

/// <summary>What a log message is about, which picks the channel it goes to.</summary>
public enum LogTopic
{
    /// <summary>Everything else: always <c>BotAnnouncement</c>.</summary>
    General,
    /// <summary>Message edits, deletes, bulk deletes and thread deletes (<c>Channels.MessageAudit</c>).</summary>
    MessageAudit,
    /// <summary>Joins, leaves and name changes (<c>Channels.MemberLog</c>).</summary>
    MemberLog,
    /// <summary>The startup report (<c>Channels.Startup</c>).</summary>
    Startup,
    /// <summary>Moderation reports such as image spam (<c>Channels.ModerationLog</c>).</summary>
    Moderation,
}

/// <summary>
/// Picks the channel for a <see cref="LogTopic"/>: the topic's optional channel when it is set and found,
/// otherwise <c>BotAnnouncement</c>. Channels are looked up on each call because the cache may not be ready
/// when a service is created.
/// </summary>
public sealed class LogChannels(DiscordSocketClient client, BotSettings settings)
{
    private readonly ChannelSettings _channels = settings.Channels;

    /// <summary>The channel for <paramref name="topic"/>, or null when neither it nor <c>BotAnnouncement</c> is available.</summary>
    public IMessageChannel? Resolve(LogTopic topic)
    {
        var id = ResolveId(_channels, topic, channelId => client.GetChannel(channelId) is IMessageChannel);
        return id != 0 ? client.GetChannel(id) as IMessageChannel : null;
    }

    /// <summary>True for <c>BotAnnouncement</c> and every configured topic channel: the bot's own log channels, whose messages are never logged.</summary>
    public bool IsLogChannel(ulong channelId) => IsLogChannel(_channels, channelId);

    /// <summary>The configured ID for <paramref name="topic"/> when <paramref name="exists"/> finds it, else the <c>BotAnnouncement</c> ID, else 0.</summary>
    internal static ulong ResolveId(ChannelSettings channels, LogTopic topic, Func<ulong, bool> exists)
    {
        var topicId = TopicId(channels, topic);
        if (topicId != 0 && exists(topicId))
            return topicId;
        return channels.BotAnnouncement?.Id ?? 0;
    }

    internal static bool IsLogChannel(ChannelSettings channels, ulong channelId)
    {
        if (channelId == 0)
            return false;
        return channelId == channels.BotAnnouncement?.Id
            || channelId == channels.MemberLog?.Id
            || channelId == channels.MessageAudit?.Id
            || channelId == channels.Startup?.Id
            || channelId == channels.ModerationLog?.Id;
    }

    private static ulong TopicId(ChannelSettings channels, LogTopic topic) => topic switch
    {
        LogTopic.MessageAudit => channels.MessageAudit?.Id ?? 0,
        LogTopic.MemberLog => channels.MemberLog?.Id ?? 0,
        LogTopic.Startup => channels.Startup?.Id ?? 0,
        LogTopic.Moderation => channels.ModerationLog?.Id ?? 0,
        _ => 0,
    };
}
