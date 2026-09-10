using Discord.WebSocket;

namespace DiscordBot.Extensions;

public static class ChannelExtensions
{
    public static bool IsThreadInForumChannel(this IMessageChannel channel)
    {
        if (channel is not SocketThreadChannel threadChannel)
            return false;
        if (threadChannel.ParentChannel is not SocketForumChannel parentChannel)
            return false;
        return true;
    }

    public static bool IsThreadInChannel(this IMessageChannel channel, ulong channelId)
    {
        if (!channel.IsThreadInForumChannel())
            return false;
        return ((SocketThreadChannel)channel).ParentChannel.Id == channelId;
    }

    public static bool IsPinned(this IThreadChannel channel)
    {
        return channel.Flags.HasFlag(ChannelFlags.Pinned);
    }
}