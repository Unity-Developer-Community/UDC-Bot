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

    // Returns the users DisplayName (nickname) if it exists, otherwise returns the username
    public static string GetUserPreferredName(this IUser user)
    {
        var guildUser = user as SocketGuildUser;
        return guildUser?.DisplayName ?? user.Username;
    }
    
    public static string GetPreferredAndUsername(this IUser user)
    {
        var guildUser = user as SocketGuildUser;
        if (guildUser == null)
            return user.Username;
        if (guildUser.DisplayName == user.Username)
            return guildUser.DisplayName;
        return $"{guildUser.DisplayName} (aka {user.Username})";
    }
}