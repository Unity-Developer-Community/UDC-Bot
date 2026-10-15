namespace DiscordBot.Settings;

public class BotSettings
{
    public string Token { get; set; } = string.Empty;
    public string Invite { get; set; } = string.Empty;
    public string DbConnectionString { get; set; } = string.Empty;
    public string ServerRootPath { get; set; } = string.Empty;
    public string AssetsRootPath { get; set; } = "./Assets";
    public char Prefix { get; set; }
    public ulong GuildId { get; set; }
    public bool LogCommandExecutions { get; set; } = true;
    public int WelcomeMessageDelaySeconds { get; set; } = 300;
    public ulong EveryoneScoldPeriodSeconds { get; set; } = 21600;
    public string WikipediaSearchPage { get; set; } = string.Empty;

    public ChannelSettings Channels { get; set; } = new();
    public RoleSettings Roles { get; set; } = new();
    public RecruitmentSettings Recruitment { get; set; } = new();
    public UnityHelpSettings UnityHelp { get; set; } = new();
    public CasinoSettings Casino { get; set; } = new();
    public BirthdaySettings Birthday { get; set; } = new();
    public ApiKeySettings ApiKeys { get; set; } = new();
    public FunCommandSettings FunCommands { get; set; } = new();

    public (List<string> Errors, List<string> Warnings) Validate()
    {
        var errors = new List<string>();
        var warnings = new List<string>();

        if (string.IsNullOrWhiteSpace(Token))
            errors.Add("Token is not configured — bot cannot authenticate");
        if (GuildId == 0)
            errors.Add("GuildId is not configured");
        if (Prefix == '\0')
            errors.Add("Prefix is not configured");
        if (string.IsNullOrWhiteSpace(DbConnectionString))
            errors.Add("DbConnectionString is not configured — database features will fail");

        if (string.IsNullOrWhiteSpace(ServerRootPath))
            warnings.Add("ServerRootPath is empty — runtime data storage may fail");

        ValidateChannel(warnings, Channels.General, nameof(Channels.General));
        ValidateChannel(warnings, Channels.Introduction, nameof(Channels.Introduction));
        ValidateChannel(warnings, Channels.BotAnnouncement, nameof(Channels.BotAnnouncement));
        ValidateChannel(warnings, Channels.BotCommands, nameof(Channels.BotCommands));
        ValidateChannel(warnings, Channels.UnityNews, nameof(Channels.UnityNews));
        ValidateChannel(warnings, Channels.UnityReleases, nameof(Channels.UnityReleases));
        ValidateChannel(warnings, Channels.Rules, nameof(Channels.Rules));

        if (Birthday.Enabled)
            ValidateChannel(warnings, Channels.BirthdayAnnouncement, nameof(Channels.BirthdayAnnouncement));

        if (Recruitment.Enabled)
            warnings.AddRange(Recruitment.Validate().Select(error => $"Recruitment stopped: {error}"));

        if (UnityHelp.BabySitterEnabled)
        {
            ValidateChannel(warnings, Channels.GenericHelp, nameof(Channels.GenericHelp));
            if (string.IsNullOrWhiteSpace(UnityHelp.TagResolved))
                warnings.Add("UnityHelp BabySitter enabled but TagResolved is empty");
        }

        if (Casino.Enabled && Casino.StartingTokens < 0)
            errors.Add("Casino.StartingTokens is negative");

        return (errors, warnings);
    }

    private static void ValidateChannel(List<string> warnings, ChannelInfo? channel, string name)
    {
        if (channel is null || channel.Id == 0)
            warnings.Add($"{name} is not configured (null or Id=0)");
    }
}

public class ChannelSettings
{
    public ChannelInfo Introduction { get; set; } = null!;
    public ChannelInfo General { get; set; } = null!;
    public ChannelInfo GenericHelp { get; set; } = null!;
    public ChannelInfo BotAnnouncement { get; set; } = null!;
    public ChannelInfo BotCommands { get; set; } = null!;
    public ChannelInfo UnityNews { get; set; } = null!;
    public ChannelInfo UnityReleases { get; set; } = null!;
    public ChannelInfo Rules { get; set; } = null!;
    public ChannelInfo Meme { get; set; } = null!;
    public ChannelInfo BirthdayAnnouncement { get; set; } = null!;

    public ulong ComplaintCategoryId { get; set; }
    public string ComplaintPrefix { get; set; } = string.Empty;
    public ulong ClosedComplaintCategoryId { get; set; }
    public string ClosedComplaintPrefix { get; set; } = string.Empty;
}

public class RoleSettings
{
    public ulong SubsReleases { get; set; }
    public ulong SubsNews { get; set; }
    public ulong Moderator { get; set; }
    public ulong TipsUser { get; set; }
}

public class RecruitmentSettings
{
    public bool Enabled { get; set; }
    public ulong FeedChannelId { get; set; }
    public RecruitmentForums Forums { get; set; } = new();
    public int AcknowledgementMinutes { get; set; } = 30;
    public int CooldownDays { get; set; } = 30;
    public int AttemptLimit { get; set; } = 2;
    public int AttemptWindowMinutes { get; set; } = 60;
    public int AttemptPauseMinutes { get; set; } = 60;
    public int UnansweredDays { get; set; } = 30; // Legacy configuration; listings no longer expire.
    public int PostRetentionMonths { get; set; } = 12;
    public int AuthorRetentionMonths { get; set; } = 24;

    public List<string> Validate()
    {
        var errors = new List<string>();
        var forums = Forums?.Mapping();
        if (forums is null || forums.Values.Any(id => id == 0) || forums.Values.Distinct().Count() != 4)
            errors.Add("all four named forum IDs must be nonzero and distinct");
        if (FeedChannelId == 0 || (forums?.Values.Contains(FeedChannelId) ?? false))
            errors.Add("FeedChannelId must identify a separate staff text channel");
        if (AcknowledgementMinutes is < 1 or > 1440 || CooldownDays is < 1 or > 365)
            errors.Add("durations must be positive (acknowledgement at most 1440 minutes; cooldown at most 365 days)");
        if (AttemptLimit is < 1 or > 20 || AttemptWindowMinutes is < 1 or > 1440 || AttemptPauseMinutes is < 1 or > 1440)
            errors.Add("attempt limit must be 1–20; attempt window and pause must be 1–1440 minutes");
        if (PostRetentionMonths is < 1 or > 120 || AuthorRetentionMonths < PostRetentionMonths || AuthorRetentionMonths > 120)
            errors.Add("retention must be 1–120 months, with author retention at least post retention");
        return errors;
    }
}

public class RecruitmentForums
{
    public ulong PaidRecruitingId { get; set; }
    public ulong PaidForHireId { get; set; }
    public ulong HobbyRecruitingId { get; set; }
    public ulong HobbyForHireId { get; set; }

    public Dictionary<string, ulong> Mapping() => new()
    {
        ["paid-recruiting"] = PaidRecruitingId, ["paid-for-hire"] = PaidForHireId,
        ["hobby-recruiting"] = HobbyRecruitingId, ["hobby-for-hire"] = HobbyForHireId
    };

    public bool Contains(ulong channelId, ulong? parentId = null) =>
        Mapping().Values.Any(id => id != 0 && (id == channelId || id == parentId));
}

public class UnityHelpSettings
{
    public bool BabySitterEnabled { get; set; } = false;
    public string TagResolved { get; set; } = string.Empty;
    public string TipImageDirectory { get; set; } = string.Empty;
    public int TipMaxImageFileSize { get; set; } = 1024 * 1024 * 10;
    public int TipMaxDirectoryFileSize { get; set; } = 1024 * 1024 * 1024;
}

public class CasinoSettings
{
    public bool Enabled { get; set; } = true;
    public long StartingTokens { get; set; } = 1000;
    public List<ulong> AllowedChannels { get; set; } = new();
    public int GameTimeoutMinutes { get; set; } = 5;
    public long DailyRewardTokens { get; set; } = 100;
    public int DailyRewardIntervalSeconds { get; set; } = 86400;
}

public class BirthdaySettings
{
    public bool Enabled { get; set; } = true;
    public int CheckIntervalMinutes { get; set; } = 240;
}

public class ApiKeySettings
{
    public string Weather { get; set; } = string.Empty;
    public string Flight { get; set; } = string.Empty;
    public string FlightSecret { get; set; } = string.Empty;
    public string AirLab { get; set; } = string.Empty;
}

public class FunCommandSettings
{
    public string? SlapObjectsTable { get; set; } = null;
    public List<string> SlapChoices { get; set; } = [];
    public List<string> SlapFails { get; set; } = [];
}

public class ChannelInfo
{
    public string Desc { get; set; } = string.Empty;
    public ulong Id { get; set; }
}
