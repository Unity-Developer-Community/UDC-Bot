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

    #endregion // Important 
    
    #region Configuration

    public int WelcomeMessageDelaySeconds { get; set; } = 300;
    public bool ModeratorCommandsEnabled { get; set; }
    // How long between when the bot will scold a user for trying to ping everyone. Default 6 hours
    public ulong EveryoneScoldPeriodSeconds { get; set; } = 21600;

    #region Fun Commands

    public List<string> UserModuleSlapChoices { get; set; } = new List<string>() { "trout", "duck", "truck", "paddle", "magikarp", "sausage", "student loan", "low poly donut" };

    #endregion // Fun Commands

    #region Service Enabling
    // Used for enabling/disabling services in the bot
    
    public bool RecruitmentServiceEnabled { get; set; } = false;

    public bool UnityHelpBabySitterEnabled { get; set; } = false;

    #endregion // Service Enabling

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
    
    public GeneralChannel GeneralChannel { get; set; }
    public GenericHelpChannel GenericHelpChannel { get; set; }
    
    public BotAnnouncementChannel BotAnnouncementChannel { get; set; }
    public AnnouncementsChannel AnnouncementsChannel { get; set; }
    public BotCommandsChannel BotCommandsChannel { get; set; }
    public UnityNewsChannel UnityNewsChannel { get; set; }
    public UnityReleasesChannel UnityReleasesChannel { get; set; }
    public RulesChannel RulesChannel { get; set; }

    // Recruitment Channels
    
    public RecruitmentChannel RecruitmentChannel { get; set; }

    public ChannelInfo ReportedMessageChannel { get; set; }
    
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

    public UserAssignableRoles UserAssignableRoles { get; set; }
    public ulong MutedRoleId { get; set; }
    public ulong SubsReleasesRoleId { get; set; }
    public ulong SubsNewsRoleId { get; set; }
    public ulong PublisherRoleId { get; set; }
    public ulong ModeratorRoleId { get; set; }

    #endregion // User Roles

    #region Recruitment Thread Tags
    
    public string TagLookingToHire { get; set; }
    public string TagLookingForWork { get; set; }
    public string TagUnpaidCollab { get; set; }
    public string TagPositionFilled { get; set; }

    #endregion // Recruitment Thread Tags

    #region API Keys

    public string WeatherAPIKey { get; set; }

    public string FlightAPIKey { get; set; }
    public string FlightAPISecret { get; set; }
    public string FlightAPIId { get; set; }
    public string AirLabAPIKey { get; set; }

    #endregion // API Keys

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

public class UserAssignableRoles : RoleGroup
{
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

public class GeneralChannel : ChannelInfo
{
}

public class GenericHelpChannel : ChannelInfo
{
    
}

public class BotAnnouncementChannel : ChannelInfo
{
}

public class AnnouncementsChannel : ChannelInfo
{
}

public class BotCommandsChannel : ChannelInfo
{
}

public class UnityNewsChannel : ChannelInfo
{
}

public class UnityReleasesChannel : ChannelInfo
{
}

public class RecruitmentChannel : ChannelInfo
{
}

public class RulesChannel : ChannelInfo
{
}

#endregion