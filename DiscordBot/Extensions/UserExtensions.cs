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
        var guildUser = user as SocketGuildUser;
        if (guildUser == null)
            return true;
        return string.Equals(guildUser.Nickname, guildUser.Username, StringComparison.CurrentCultureIgnoreCase);
    }
    
    // Returns a simple string formatted as: "**user.Username** (aka **user.Nickname**)"
    // Nickname is only included if it's different from the username
    public static string UserNameReferenceForEmbed(this IUser user)
    {
        var reference = $"**{user.GetNickName()}**!";
        if (!user.IsNickAndNameEqual())
            reference += $" (aka **{user.Username}**)";
        return reference;
    }
    
    // Returns a simple string formatted as: "user.Username (aka user.Nickname)"
    // Nickname is only included if it's different from the username
    public static string UserNameReference(this IUser user)
    {
        var reference = $"{user.GetNickName()}!";
        if (!user.IsNickAndNameEqual())
            reference += $" (aka {user.Username})";
        return reference;
    }
    
    // Returns the nickname of the user if the IUser can be cast to it exists, otherwise returns the username
    public static string GetNickName(this IUser user)
    {
        return (user as SocketGuildUser)?.Nickname ?? user.Username;
    }
}