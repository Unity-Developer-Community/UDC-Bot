namespace DiscordBot.Settings;

public class BotSettings
{
    #region Important Settings

    public string Token { get; set; } = string.Empty;
    public string Invite { get; set; } = string.Empty;

    public string DbConnectionString { get; set; } = string.Empty;
    public string ServerRootPath { get; set; } = string.Empty;
    public string AssetsRootPath { get; set; } = "./Assets";
    public char Prefix { get; set; }
    public ulong GuildId { get; set; }
    public bool LogCommandExecutions { get; set; } = true;

    #endregion // Important 

    #region Configuration

    public int WelcomeMessageDelaySeconds { get; set; } = 300;
    // How long between when the bot will scold a user for trying to ping everyone. Default 6 hours
    public ulong EveryoneScoldPeriodSeconds { get; set; } = 21600;

    #region Fun Commands

    public string? UserModuleSlapObjectsTable { get; set; } = null;
    //NOTE: Deserializer will not override a List<string> from the json if a default one is made here.
    public List<string> UserModuleSlapChoices { get; set; } = [];
    // = { "trout", "duck", "truck", "paddle", "magikarp", "sausage", "student loan",
    //     "life choice", "bug report", "unhandled exception", "null pointer", "keyboard",
    //     "cheese wheel", "banana peel", "unresolved bug", "low poly donut" };
    public List<string> UserModuleSlapFails { get; set; } = [];
    // = { "hurting themselves" };

    #endregion // Fun Commands

    #region Service Enabling
    // Used for enabling/disabling services in the bot

    public bool RecruitmentServiceEnabled { get; set; } = false;
    public bool UnityHelpBabySitterEnabled { get; set; } = false;

    #endregion // Service Enabling

    #region Birthday Announcements

    public bool BirthdayAnnouncementEnabled { get; set; } = true;
    public int BirthdayCheckIntervalMinutes { get; set; } = 240; // Check every 4 hours by default
    public ChannelInfo BirthdayAnnouncementChannel { get; set; } = null!;

    #endregion // Birthday Announcements

    #endregion // Configuration

    #region Channels

    public ChannelInfo IntroductionChannel { get; set; } = null!;
    public ChannelInfo GeneralChannel { get; set; } = null!;
    public ChannelInfo GenericHelpChannel { get; set; } = null!;

    public ChannelInfo BotAnnouncementChannel { get; set; } = null!;
    public ChannelInfo BotCommandsChannel { get; set; } = null!;
    public ChannelInfo UnityNewsChannel { get; set; } = null!;
    public ChannelInfo UnityReleasesChannel { get; set; } = null!;
    public ChannelInfo RulesChannel { get; set; } = null!;

    // Recruitment Channels

    public ChannelInfo RecruitmentChannel { get; set; } = null!;

    public ChannelInfo MemeChannel { get; set; } = null!;

    #region Complaint Channel

    public ulong ComplaintCategoryId { get; set; }
    public string ComplaintChannelPrefix { get; set; } = string.Empty;
    public ulong ClosedComplaintCategoryId { get; set; }
    public string ClosedComplaintChannelPrefix { get; set; } = string.Empty;

    #endregion // Complaint Channel

    #endregion // Channels

    #region User Roles

    public ulong SubsReleasesRoleId { get; set; }
    public ulong SubsNewsRoleId { get; set; }
    public ulong ModeratorRoleId { get; set; }
    public ulong TipsUserRoleId { get; set; } // e.g., Helpers

    #endregion // User Roles

    #region Recruitment Thread

    public string TagLookingToHire { get; set; } = string.Empty;
    public string TagLookingForWork { get; set; } = string.Empty;
    public string TagUnpaidCollab { get; set; } = string.Empty;
    public string TagPositionFilled { get; set; } = string.Empty;

    public int EditPermissionAccessTimeMin { get; set; } = 3;

    #endregion // Recruitment Thread Tags

    #region Unity Help Threads

    #region Tips

    public string TipImageDirectory { get; set; } = string.Empty;

    public int TipMaxImageFileSize { get; set; } = 1024 * 1024 * 10; // 10MB
    // Unlikely, but we prevent exploitation by limiting the max directory size to avoid VPS disk space issues
    public int TipMaxDirectoryFileSize { get; set; } = 1024 * 1024 * 1024; // 1GB

    #endregion // Tips

    public string TagUnitHelpResolvedTag { get; set; } = string.Empty;

    #endregion // Unity Help Threads

    #region API Keys

    public string WeatherAPIKey { get; set; } = string.Empty;

    public string FlightAPIKey { get; set; } = string.Empty;
    public string FlightAPISecret { get; set; } = string.Empty;

    public string AirLabAPIKey { get; set; } = string.Empty;

    #endregion // API Keys

    #region Casino Settings

    public bool CasinoEnabled { get; set; } = true;
    public long CasinoStartingTokens { get; set; } = 1000;
    public List<ulong> CasinoAllowedChannels { get; set; } = new List<ulong>();
    public int CasinoGameTimeoutMinutes { get; set; } = 5;

    // Daily Reward Settings
    public long CasinoDailyRewardTokens { get; set; } = 100;
    public int CasinoDailyRewardIntervalSeconds { get; set; } = 86400; // 24 hours = 86400 seconds

    #endregion // Casino Settings

    #region Other

    public string WikipediaSearchPage { get; set; } = string.Empty;

    #endregion // Other

}

#region Channel Information

// Channel Information. Description and Channel ID
public class ChannelInfo
{
    public string Desc { get; set; } = string.Empty;
    public ulong Id { get; set; }
}

#endregion
