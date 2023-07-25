using Discord.WebSocket;

namespace DiscordBot.Extensions;

public static class UserExtensions
{
    public static bool IsUserBotOrWebhook(this IUser user)
    {
        return user.IsBot || user.IsWebhook;
    }
    
    public static bool HasRoleGroup(this IUser user, SocketRole role) 
    {
        return HasRoleGroup(user, role.Id);
    }
    public static bool HasRoleGroup(this IUser user, ulong roleId)
    {
        return user is SocketGuildUser guildUser && guildUser.Roles.Any(x => x.Id == roleId);
    }
    
    public static bool IsNickAndNameEqual(this IUser user)
    {
        return user is SocketGuildUser guildUser && string.Equals(guildUser.Nickname, guildUser.Username, StringComparison.CurrentCultureIgnoreCase);
    }
    
    // Returns a simple string formatted as: "**user.Username** (aka **user.Nickname**)"
    // Nickname is only included if it's different from the username
    public static string UserNameReferenceForEmbed(this IUser user)
    {
        var reference = $"**{user.Username}**";
        if (!user.IsNickAndNameEqual())
            reference += $" (aka **{user}**)";
        return reference;
    }
    
    // Returns a simple string formatted as: "user.Username (aka user.Nickname)"
    // Nickname is only included if it's different from the username
    public static string UserNameReference(this IUser user)
    {
        var reference = $"{user.Username}";
        if (!user.IsNickAndNameEqual())
            reference += $" (aka {user})";
        return reference;
    }
}