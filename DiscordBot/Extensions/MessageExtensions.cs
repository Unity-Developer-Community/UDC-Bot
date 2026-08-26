using System.Text.RegularExpressions;

namespace DiscordBot.Extensions;

public static class MessageExtensions
{
    private const string InviteLinkPattern = @"(https?:\/\/)?(www\.)?(discord\.gg\/[a-zA-Z0-9]+)";

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

    /// <summary>
    /// Returns true if the message contains any discord invite links, ie; discord.gg/invite
    /// </summary>
    public static bool ContainsInviteLink(this IUserMessage message)
    {
        return Regex.IsMatch(message.Content, InviteLinkPattern, RegexOptions.IgnoreCase);
    }

    /// <summary>
    /// Returns true if the message contains any discord invite links, ie; discord.gg/invite
    /// </summary>
    public static bool ContainsInviteLink(this string message)
    {
        return Regex.IsMatch(message, InviteLinkPattern, RegexOptions.IgnoreCase);
    }

    /// <summary>
    /// Returns true if the message contains any discord invite links, ie; discord.gg/invite
    /// </summary>
    public static bool ContainsInviteLink(this IMessage message)
    {
        return Regex.IsMatch(message.Content, InviteLinkPattern, RegexOptions.IgnoreCase);
    }

    /// <summary>
    /// Formats message text and attachments for a bounded audit-log field.
    /// </summary>
    public static (string Value, bool IsTruncated) FormatForAuditLog(
        this IMessage message,
        int maxLength)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(message.Content))
            parts.Add(message.Content);

        if (message.Attachments.Count > 0)
        {
            parts.Add(
                $"Attachments ({message.Attachments.Count}):\n" +
                string.Join(
                    "\n",
                    message.Attachments.Select(attachment => $"[{attachment.Filename}]({attachment.Url})")));
        }

        if (parts.Count == 0)
            parts.Add("*No text content*");

        var value = string.Join("\n\n", parts);
        return (value.TruncateWithEllipsis(maxLength), value.Length > maxLength);
    }

}
