namespace DiscordBot.Settings.Options;

public enum RecruitmentMode { Observe, Advisory, Enforce }

public sealed class RecruitmentOptions
{
    public const string SectionName = "Recruitment";
    public bool Enabled { get; set; }
    public RecruitmentMode Mode { get; set; } = RecruitmentMode.Observe;
    public RecruitmentForumsOptions Forums { get; set; } = new();
    public ulong FeedChannelId { get; set; }
    public string GuidelinesDirectory { get; set; } = "recruitment/guidelines";
    public int AcknowledgementMinutes { get; set; } = 30;
    public int CooldownDays { get; set; } = 30;
    public int UnansweredDays { get; set; } = 30;
    public bool EnforceGuidelineTimeouts { get; set; }
    public bool EnforceLifecycleClosures { get; set; }
    public bool EnforceListingLimits { get; set; }
    public int PostRetentionMonths { get; set; } = 12;
    public int AuthorRetentionMonths { get; set; } = 24;
}

public sealed class RecruitmentForumsOptions
{
    public RecruitmentForumOptions PaidRecruiting { get; set; } = new();
    public RecruitmentForumOptions PaidForHire { get; set; } = new();
    public RecruitmentForumOptions HobbyRecruiting { get; set; } = new();
    public RecruitmentForumOptions HobbyForHire { get; set; } = new();
}

public sealed class RecruitmentForumOptions
{
    public ulong ChannelId { get; set; }
}
