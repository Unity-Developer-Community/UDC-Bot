using Discord.Commands;

namespace DiscordBot.Extensions;

public static class ContextExtension
{
    /// <summary>
    /// Sanity test to confirm a Context doesn't contain role or everyone mentions.
    /// </summary>
    /// <remarks>Use `HasAnyPingableMention` to also include user mentions.</remarks>
    public static bool HasRoleOrEveryoneMention(this ICommandContext context)
    {
        return context.Message.MentionedRoleIds.Count != 0 || context.Message.MentionedEveryone;
    }
    
    /// <summary>
    /// True if the context includes a RoleID, UserID or Mentions Everyone (Should include @here, unsure)
    /// </summary>
    /// <remarks>Use `HasRoleOrEveryoneMention` to check for ONLY RoleIDs or Everyone mentions.</remarks>
    public static bool HasAnyPingableMention(this ICommandContext context)
    {
        return context.Message.MentionedUserIds.Count > 0 || context.HasRoleOrEveryoneMention();
    }
    
    /// <summary>
    /// True if the Context contains a message that is a reply and only mentions the user that sent the message.
    /// ie; the message is a reply to the user but doesn't contain any other mentions.
    /// </summary>
    public static bool IsOnlyReplyingToAuthor(this ICommandContext context)
    {
        if (!context.IsReply())
            return false;
        if (context.Message.MentionedUserIds.Count != 1)
            return false;
        return context.Message.MentionedUserIds.First() == context.Message.ReferencedMessage.Author.Id;
    }
    
    /// <summary>
    /// Returns true if the Context has a reference to another message.
    /// ie; the message is a reply to another message.
    /// </summary>
    public static bool IsReply(this ICommandContext context)
    {
        return context.Message.ReferencedMessage != null;
    }
}