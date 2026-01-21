namespace DiscordBot.Settings;

public class BotSettings
{
    #region Important Settings

    public string Token { get; set; }
    public string Invite { get; set; }
    
    public string DbConnectionString { get; set; }
    public string ServerRootPath { get; set; }
    public char Prefix { get; set; }
    public ulong GuildId { get; set; }
    public bool LogCommandExecutions { get; set; } = true;

    #endregion // Important 
    
    #region Configuration

    public int WelcomeMessageDelaySeconds { get; set; } = 300;
    public bool ModeratorCommandsEnabled { get; set; }
    public bool ModeratorNoInviteLinks { get; set; }
    // How long between when the bot will scold a user for trying to ping everyone. Default 6 hours
    public ulong EveryoneScoldPeriodSeconds { get; set; } = 21600;

    #region Fun Commands

    public string UserModuleSlapObjectsTable { get; set; } = null;
        // = "udc-slap.txt"
    //NOTE: Deserializer will not override a List<string> from the json if a default one is made here.
    public List<string> UserModuleSlapChoices { get; set; }
        // = { "trout", "duck", "truck", "paddle", "magikarp", "sausage", "student loan",
        //     "life choice", "bug report", "unhandled exception", "null pointer", "keyboard",
        //     "cheese wheel", "banana peel", "unresolved bug", "low poly donut" };
    public List<string> UserModuleSlapFails { get; set; }
        // = { "hurting themselves" };
    
    #endregion // Fun Commands

    #region Service Enabling
    // Used for enabling/disabling services in the bot
    
    public bool RecruitmentServiceEnabled { get; set; } = false;
    public bool UnityHelpBabySitterEnabled { get; set; } = false;
    public bool ReactRoleServiceEnabled { get; set; } = false;
    public bool IntroductionWatcherServiceEnabled { get; set; } = false;

    #endregion // Service Enabling

    #region Birthday Announcements
    
    public bool BirthdayAnnouncementEnabled { get; set; } = true;
    public int BirthdayCheckIntervalMinutes { get; set; } = 240; // Check every 4 hours by default
    public ChannelInfo BirthdayAnnouncementChannel { get; set; }
    
    #endregion // Birthday Announcements

    #endregion // Configuration

    #region Asset Publisher

    // Used for Asset Publisher

    public string Email { get; set; }
    public string EmailUsername { get; set; }
    public string EmailPassword { get; set; }
    public string EmailSMTPServer { get; set; }
    public int EmailSMTPPort { get; set; }

    #endregion // Asset Publisher
    
    #region Channels
    
    public ChannelInfo IntroductionChannel { get; set; }
    public ChannelInfo GeneralChannel { get; set; }
    public ChannelInfo GenericHelpChannel { get; set; }
    
    public ChannelInfo BotAnnouncementChannel { get; set; }
    public ChannelInfo AnnouncementsChannel { get; set; }
    public ChannelInfo BotCommandsChannel { get; set; }
    public ChannelInfo UnityNewsChannel { get; set; }
    public ChannelInfo UnityReleasesChannel { get; set; }
    public ChannelInfo RulesChannel { get; set; }

    // Recruitment Channels
    
    public ChannelInfo RecruitmentChannel { get; set; }

    public ChannelInfo ReportedMessageChannel { get; set; }
    
    public ChannelInfo MemeChannel { get; set; }
    
    #region Complaint Channel

    public ulong ComplaintCategoryId { get; set; }
    public string ComplaintChannelPrefix { get; set; }
    public ulong ClosedComplaintCategoryId { get; set; }
    public string ClosedComplaintChannelPrefix { get; set; }

    #endregion // Complaint Channel

    #region Auto-Threads

    public List<AutoThreadChannel> AutoThreadChannels { get; set; } = new List<AutoThreadChannel>();
    public List<string> AutoThreadExclusionPrefixes { get; set; } = new List<string>();

    #endregion // Auto-Threads
    
    #endregion // Channels

    #region User Roles

    public RoleGroup UserAssignableRoles { get; set; }
    public ulong MutedRoleId { get; set; }
    public ulong SubsReleasesRoleId { get; set; }
    public ulong SubsNewsRoleId { get; set; }
    public ulong PublisherRoleId { get; set; }
    public ulong ModeratorRoleId { get; set; }
    public ulong TipsUserRoleId { get; set; } // e.g., Helpers
    public ulong TipsAuthorRoleId { get; set; } // e.g., Moderators

    #endregion // User Roles

    #region Recruitment Thread
    
    public string TagLookingToHire { get; set; }
    public string TagLookingForWork { get; set; }
    public string TagUnpaidCollab { get; set; }
    public string TagPositionFilled { get; set; }
    
    public int EditPermissionAccessTimeMin { get; set; } = 3;

    #endregion // Recruitment Thread Tags

    #region Unity Help Threads
    
    #region Tips
    
    public string TipImageDirectory { get; set; }

    public int TipMaxImageFileSize { get; set; } = 1024 * 1024 * 10; // 10MB
    // Unlikely, but we prevent exploitation by limiting the max directory size to avoid VPS disk space issues
    public int TipMaxDirectoryFileSize { get; set; } = 1024 * 1024 * 1024; // 1GB
    
    #endregion // Tips

    public string TagUnitHelpResolvedTag { get; set; }

    #endregion // Unity Help Threads

    #region API Keys

    public string WeatherAPIKey { get; set; }

    public string FlightAPIKey { get; set; }
    public string FlightAPISecret { get; set; }
    public string FlightAPIId { get; set; }
    public string AirLabAPIKey { get; set; }

    #endregion // API Keys

    #region Casino Settings

    public bool CasinoEnabled { get; set; } = true;
    public ulong CasinoStartingTokens { get; set; } = 1000;
    public List<ulong> CasinoAllowedChannels { get; set; } = new List<ulong>();
    public int CasinoGameTimeoutMinutes { get; set; } = 5;

    // Daily Reward Settings
    public ulong CasinoDailyRewardTokens { get; set; } = 100;
    public int CasinoDailyRewardIntervalDays { get; set; } = 1; // Number of days between rewards

    #endregion // Casino Settings

    #region Other

    public string AssetStoreFrontPage { get; set; }
    public string WikipediaSearchPage { get; set; }

    #endregion // Other
    
}

#region Role Group Collections

// Classes used to hold information regarding a collection of role ids with a description.
public class RoleGroup
{
    public string Desc { get; set; }
    public List<string> Roles { get; set; }
}

#endregion

#region Channel Information

// Channel Information. Description and Channel ID
public class ChannelInfo
{
    public string Desc { get; set; }
    public ulong Id { get; set; }
}

public class AutoThreadChannel
{
    public string Title { get; set; }
    public ulong Id { get; set; }
    public bool CanArchive { get; set; } = false;
    public bool CanDelete { get; set; } = false;
    public string TitleArchived { get; set; }
    public string FirstMessage { get; set; }
    public string Duration { get; set; }

    private static string AuthorName(IUser author)
    {
        return ((IGuildUser)author).Nickname ?? author.Username;
    }

    public string GenerateTitle(IUser author)
    {
        return String.Format(this.Title, AuthorName(author));
    }

    public string GenerateTitleArchived(IUser author)
    {
        return String.Format(this.TitleArchived, AuthorName(author));
    }

    public string GenerateFirstMessage(IUser author)
    {
        return String.Format(this.FirstMessage, author.Mention);
    }
}

#endregion
