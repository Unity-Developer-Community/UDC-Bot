using System.Collections.Generic;

namespace DiscordBot.Settings.Deserialized
{
    public class Settings
    {
        public string Token { get; set; }
        public string Invite { get; set; }
        
        // Used for Asset Publisher
        public string Gmail { get; set; }
        public string GmailUsername { get; set; }
        public string GmailPassword { get; set; }
        
        public string DbConnectionString { get; set; }

        public string ServerRootPath { get; set; }

        public char Prefix { get; set; }
        public ulong guildId { get; set; }
        
        public UserAssignableRoles AllRoles { get; set; }
        
        public GeneralChannel GeneralChannel { get; set; }
        
        public BotAnnouncementChannel BotAnnouncementChannel { get; set; }
        
        public AnnouncementsChannel AnnouncementsChannel { get; set; }

        public BotCommandsChannel BotCommandsChannel { get; set; }

        public UnityNewsChannel UnityNewsChannel { get; set; }

        public UnityReleasesChannel UnityReleasesChannel { get; set; }

        // Recruitment Channels
        public WorkForHireChannel LookingToHire { get; set; }
        public WorkForHireChannel LookingForWork { get; set; }
        public CollaborationChannel CollaborationChannel { get; set; }
        
        public ulong MutedRoleId { get; set; }
        public ulong SubsReleasesRoleId { get; set; }
        public ulong SubsNewsRoleId { get; set; }
        public ulong PublisherRoleId { get; set; }
        public ulong ModeratorRoleId { get; set; }

        public string AssetStoreFrontPage { get; set; }

        public string WikipediaSearchPage { get; set; }

        public ulong ComplaintCategoryId { get; set; }
        public string ComplaintChannelPrefix { get; set; }
        public ulong ClosedComplaintCategoryId { get; set; }
        public string ClosedComplaintChannelPrefix { get; set; }

        public ulong UpdateNewsChannel { get; set; } = 0;
    }

    #region Role Group Collections
    // Classes used to hold information regarding a collection of role ids with a description.
    public class RoleGroup
    {
        public string Desc { get; set; }
        public List<string> Roles { get; set; }
    }
    
    public class UserAssignableRoles : RoleGroup {};
    #endregion
    
    #region Channel Information
    // Channel Information. Description and Channel ID
    public class ChannelInfo
    {
        public string Desc { get; set; }
        public ulong Id { get; set; }
    }
    
    public class GeneralChannel : ChannelInfo {};
    public class BotAnnouncementChannel : ChannelInfo {};
    public class AnnouncementsChannel : ChannelInfo {};
    public class BotCommandsChannel : ChannelInfo {};
    public class UnityNewsChannel : ChannelInfo {};
    public class UnityReleasesChannel : ChannelInfo {};
    public class WorkForHireChannel : ChannelInfo {};
    public class CollaborationChannel : ChannelInfo {};
    #endregion
}