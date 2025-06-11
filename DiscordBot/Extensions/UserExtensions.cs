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
        var guildUser = user as IGuildUser;
        if (guildUser == null)
            return false;
        return guildUser.RoleIds.Any(x => x == roleId);
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
        if (string.Equals(guildUser.DisplayName, user.Username, StringComparison.CurrentCultureIgnoreCase))
            return guildUser.DisplayName;
        return $"{guildUser.DisplayName} (aka {user.Username})";
    }

    public static string GetUserLoggingString(this IUser user)
    {
        var guildUser = user as SocketGuildUser;
        if (guildUser == null)
            return $"{user.Username} `{user.Id}`";
        return $"{guildUser.GetPreferredAndUsername()} `{guildUser.Id}`";
    }

    public static string[] ToUserPreferredNameArray(this IUser[] users)
    {
        var names = new string[users.Length];
        for (int i = 0; i < users.Length; i++)
            names[i] = GetUserPreferredName(users[i]);
        return names;
    }

    public static string[] ToMentionArray(this IUser[] users)
    {
        var mentions = new string[users.Length];
        for (int i = 0; i < users.Length; i++)
            mentions[i] = users[i].Mention;
        return mentions;
    }
}
