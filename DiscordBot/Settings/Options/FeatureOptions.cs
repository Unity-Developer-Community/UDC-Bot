namespace DiscordBot.Settings.Options;

public sealed class UserActivityOptions
{
    public const string SectionName = "UserActivity";

    public int WelcomeMessageDelaySeconds { get; set; } = 300;
    public ulong EveryoneScoldPeriodSeconds { get; set; } = 21600;
    public List<string> Thanks { get; set; } = ["thanks", "ty", "thx", "thnx", "thanx", "thankyou", "thank you", "cheers"];
    public int ThanksCooldown { get; set; } = 60;
    public int ThanksMinJoinTime { get; set; } = 600;
    public int XpMinPerMessage { get; set; } = 10;
    public int XpMaxPerMessage { get; set; } = 30;
    public int XpMinCooldown { get; set; } = 60;
    public int XpMaxCooldown { get; set; } = 180;
    public int CodeReminderCooldown { get; set; } = 86400;
}

public sealed class UserFunOptions
{
    public const string SectionName = "UserFun";

    public string SlapObjectsTable { get; set; } = string.Empty;
    public List<string> SlapChoices { get; set; } = [];
    public List<string> SlapFailures { get; set; } = [];
}

public sealed class RoleAssignmentOptions
{
    public const string SectionName = "RoleAssignment";

    public List<string> AssignableRoles { get; set; } = [];
}

public sealed class ModerationOptions
{
    public const string SectionName = "Moderation";

    public bool CommandsEnabled { get; set; }
    public bool BlockInviteLinks { get; set; }
    public bool IntroductionWatcherEnabled { get; set; }
    public ulong MutedRoleId { get; set; }
    public ulong IntroductionChannelId { get; set; }
    public ulong GeneralChannelId { get; set; }
    public ulong ReportedMessageChannelId { get; set; }
    public ulong MemeChannelId { get; set; }
    public ulong RulesChannelId { get; set; }
}

public sealed class TicketOptions
{
    public const string SectionName = "Tickets";

    public ulong OpenCategoryId { get; set; }
    public string OpenChannelPrefix { get; set; } = "Complaint";
    public ulong ClosedCategoryId { get; set; }
    public string ClosedChannelPrefix { get; set; } = "Closed-";
}

public sealed class FeedOptions
{
    public const string SectionName = "Feeds";

    public ulong NewsChannelId { get; set; }
    public ulong ReleasesChannelId { get; set; }
    public ulong NewsSubscriberRoleId { get; set; }
    public ulong ReleasesSubscriberRoleId { get; set; }
}

public sealed class RecruitmentOptions
{
    public const string SectionName = "Recruitment";

    public bool Enabled { get; set; }
    public ulong ForumChannelId { get; set; }
    public ulong LookingToHireTagId { get; set; }
    public ulong LookingForWorkTagId { get; set; }
    public ulong UnpaidCollaborationTagId { get; set; }
    public ulong PositionFilledTagId { get; set; }
    public int EditPermissionMinutes { get; set; } = 3;
}

public sealed class UnityHelpOptions
{
    public const string SectionName = "UnityHelp";

    public bool Enabled { get; set; }
    public ulong ForumChannelId { get; set; }
    public ulong ResolvedTagId { get; set; }
}

public sealed class BirthdayOptions
{
    public const string SectionName = "BirthdayAnnouncements";

    public bool Enabled { get; set; } = true;
    public int CheckIntervalMinutes { get; set; } = 240;
    public ulong AnnouncementChannelId { get; set; }
}

public sealed class ReminderOptions
{
    public const string SectionName = "Reminders";

    public ulong FallbackChannelId { get; set; }
}

public sealed class TipsOptions
{
    public const string SectionName = "Tips";

    public string ImageDirectory { get; set; } = string.Empty;
    public int MaxImageBytes { get; set; } = 10 * 1024 * 1024;
    public long MaxDirectoryBytes { get; set; } = 1024L * 1024 * 1024;
    public ulong HelperRoleId { get; set; }
}

public sealed class CasinoOptions
{
    public const string SectionName = "Casino";

    public bool Enabled { get; set; } = true;
    public long StartingTokens { get; set; } = 1000;
    public List<ulong> AllowedChannelIds { get; set; } = [];
    public int GameTimeoutMinutes { get; set; } = 5;
    public long DailyRewardTokens { get; set; } = 100;
    public int DailyRewardIntervalSeconds { get; set; } = 86400;
}

public sealed class WeatherOptions
{
    public const string SectionName = "Weather";

    [Secret]
    public string ApiKey { get; set; } = string.Empty;
}

public sealed class AirportOptions
{
    public const string SectionName = "Airport";

    [Secret]
    public string FlightApiKey { get; set; } = string.Empty;

    [Secret]
    public string FlightApiSecret { get; set; } = string.Empty;

    [Secret]
    public string AirLabsApiKey { get; set; } = string.Empty;
}

public sealed class KnowledgeSearchOptions
{
    public const string SectionName = "KnowledgeSearch";

    public string WikipediaSearchPage { get; set; } = string.Empty;
}
