using System;
using System.Collections.Generic;
using System.Text;

namespace DiscordBot.Settings.Deserialized
{
    public class ReactRoleSettings
    {
        public uint RoleAddDelay = 5000; // Delay in ms
        public bool LogUpdates = false;
        public List<UserReactMessage> UserReactRoleList;
    }

    public class UserReactMessage
    {
        public ulong ChannelId;
        public ulong MessageId;
        public string Description { get; set; }
        public List<ReactRole> Reactions;

        public int RoleCount() { return Reactions?.Count ?? 0; }
    }

    public class ReactRole
    {
        public string Name;
        public ulong RoleId { get; set; }
        public ulong EmojiId { get; set; }

        public ReactRole(string _name, ulong _role_id, ulong _emoji_id)
        {
            Name = _name;
            RoleId = _role_id;
            EmojiId = _emoji_id;
        }
    }
}
