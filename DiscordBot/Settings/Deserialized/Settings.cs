using System.Collections.Generic;

namespace DiscordBot.Settings.Deserialized
{
    public class Settings
    {
        public string Token { get; set; }
        public string Invite { get; set; }
        public string Gmail { get; set; }
        public string GmailPassword { get; set; }

        public string DbConnectionString { get; set; }

        public string ServerRootPath { get; set; }

        public char Prefix { get; set; }
        public string AllowMentionPrefix { get; set; }
        public string Administrator { get; set; }
        public ulong guildId { get; set; }

        public string TntDroid { get; set; }

        public string SendJoinMessage { get; set; }
        public string SendMessage { get; set; }
        public string DeleteProfileMessageCache { get; set; }

        public string DeleteProfileMessageCacheTime { get; set; }

        public AllRoles AllRoles { get; set; }

        public RolesBanned RolesBanned { get; set; }

        public RolesModeration RolesModeration { get; set; }

        public RoleModSquadPermission RoleModSquadPermission { get; set; }

        public ServerId ServerId { get; set; }

        public GeneralChannel GeneralChannel { get; set; }

        public BotDevelopmentChannel BotDevelopmentChannel { get; set; }

        public BotFunctionalityChannel BotFunctionalityChannel { get; set; }

        public BotAnnouncementChannel BotAnnouncementChannel { get; set; }

        public BotXpMessageChannel BotXpMessageChannel { get; set; }

        public AnnouncementsChannel AnnouncementsChannel { get; set; }

        public BotCommandsChannel BotCommandsChannel { get; set; }

        public BotPublisherPrivateChannel BotPublisherPrivateChannel { get; set; }

        public UnityNewsChannel UnityNewsChannel { get; set; }

        public BotPrivateChannel BotPrivateChannel { get; set; }

        public DevStreamChannel DevStreamChannel { get; set; }

        public WorkForHireChannel WorkForHireChannel { get; set; }

        public CollaborationChannel CollaborationChannel { get; set; }

        public AnimeChannel AnimeChannel { get; set; }

        public CasinoChannel CasinoChannel { get; set; }

        public MusicCommandsChannel MusicCommandsChannel { get; set; }

        public ulong MutedRoleId { get; set; }
        public ulong StreamingRoleId { get; set; }
        public ulong StreamerRoleId { get; set; }
        public ulong SubsReleasesRoleId { get; set; }
        public ulong SubsNewsRoleId { get; set; }
        public ulong PublisherRoleId { get; set; }
        public ulong StaffRoleId { get; set; }

        public string AssetStoreFrontPage { get; set; }

        public string WikipediaSearchPage { get; set; }

        public string ComplaintCategoryName { get; set; }
        public string ComplaintChannelPrefix { get; set; }
    }

    public class AllRoles
    {
        public string Desc { get; set; }
        public List<string> Roles { get; set; }
    }

    public class RolesBanned
    {
        public string Desc { get; set; }
        public List<string> Roles { get; set; }
    }

    public class RolesModeration
    {
        public string Desc { get; set; }
        public List<string> Roles { get; set; }
    }

    public class RoleModSquadPermission
    {
        public string Desc { get; set; }
        public List<string> Roles { get; set; }
    }

    public class ServerId
    {
        public string Desc { get; set; }
        public ulong Id { get; set; }
    }

    public class GeneralChannel
    {
        public string Desc { get; set; }
        public ulong Id { get; set; }
    }

    public class BotDevelopmentChannel
    {
        public string Desc { get; set; }
        public ulong Id { get; set; }
    }

    public class BotFunctionalityChannel
    {
        public string Desc { get; set; }
        public ulong Id { get; set; }
    }

    public class BotAnnouncementChannel
    {
        public string Desc { get; set; }
        public ulong Id { get; set; }
    }

    public class BotXpMessageChannel
    {
        public string Desc { get; set; }
        public ulong Id { get; set; }
    }

    public class AnnouncementsChannel
    {
        public string Desc { get; set; }
        public ulong Id { get; set; }
    }

    public class BotCommandsChannel
    {
        public string Desc { get; set; }
        public ulong Id { get; set; }
    }

    public class BotPublisherPrivateChannel
    {
        public string Desc { get; set; }
        public ulong Id { get; set; }
    }

    public class UnityNewsChannel
    {
        public string Desc { get; set; }
        public ulong Id { get; set; }
    }

    public class BotPrivateChannel
    {
        public string Desc { get; set; }
        public ulong Id { get; set; }
    }

    public class DevStreamChannel
    {
        public string Desc { get; set; }
        public ulong Id { get; set; }
    }

    public class WorkForHireChannel
    {
        public string Desc { get; set; }
        public ulong Id { get; set; }
    }

    public class CollaborationChannel
    {
        public string Desc { get; set; }
        public ulong Id { get; set; }
    }

    public class AnimeChannel
    {
        public string Desc { get; set; }
        public ulong Id { get; set; }
    }

    public class CasinoChannel
    {
        public string Desc { get; set; }
        public ulong Id { get; set; }
    }

    public class MusicCommandsChannel
    {
        public string Desc { get; set; }
        public ulong Id { get; set; }
    }
}