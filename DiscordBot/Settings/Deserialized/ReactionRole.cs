namespace DiscordBot.Settings;

public class ReactRoleSettings
{
    public bool LogUpdates = false;
    public uint RoleAddDelay = 5000; // Delay in ms
    public List<UserReactMessage> UserReactRoleList;
}

public class UserReactMessage
{
    public ulong ChannelId;
    public ulong MessageId;
    public List<ReactRole> Reactions;
    public string Description { get; set; }

    public int RoleCount() => Reactions?.Count ?? 0;
}

public class ReactRole
{
    public string Name;

    public ReactRole(string name, ulong roleId, ulong emojiId)
    {
        Name = name;
        RoleId = roleId;
        EmojiId = emojiId;
    }

    public ulong RoleId { get; set; }
    public ulong EmojiId { get; set; }
}