using DiscordBot.Settings.Deserialized;

namespace DiscordBot.Extensions
{
    public static class ReactMessageExtensions
    {
        public static string MessageLinkBack(this UserReactMessage message, ulong guildId)
        {
            if (message == null) return "";
            return $"https://discordapp.com/channels/{guildId.ToString()}/{message.ChannelId.ToString()}/{message.MessageId.ToString()}";
        }
    }
}