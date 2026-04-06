namespace DiscordBot.Extensions;

public static class MessageExtensions
{
    public static async Task<bool> TrySendMessage(this IDMChannel channel, string message = "", Embed embed = null)
    {
        try
        {
            await channel.SendMessageAsync(message, embed: embed);
        }
        catch (Exception)
        {
            return false;
        }
        return true;
    }

    /// <summary>
    /// Returns true if the message includes any RoleID's, UserID's or Mentions Everyone
    /// </summary>
    public static bool HasAnyPingableMention(this IUserMessage message)
    {
        return message.MentionedUserIds.Count > 0 || message.MentionedRoleIds.Count > 0 || message.MentionedEveryone;
    }
}