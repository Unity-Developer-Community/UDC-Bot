namespace DiscordBot.Services.Recruitment.State;

public sealed class ForumPublication
{
    public ulong? ClosedTagId { get; set; }
    public GuidelineReceipt? Confirmed { get; set; }
    public GuidelineReceipt? Candidate { get; set; }
    public string? ExpectedTopicHash { get; set; }
    public DateTimeOffset? CheckedAtUtc { get; set; }
    public string? Error { get; set; }
}

public sealed class GuidelineReceipt
{
    public string Code { get; set; } = "";
    public DateTimeOffset WeekStartUtc { get; set; }
    public string TopicHash { get; set; } = "";
    public string TemplateHash { get; set; } = "";
    public DateTimeOffset? PublishedAtUtc { get; set; }
}

public sealed class PostAdvisory
{
    public string Generation { get; set; } = "";
    public long Version { get; set; }
    public DateTimeOffset? SendRequestedAtUtc { get; set; }
    public ulong? SearchBeforeId { get; set; }
    public DateTimeOffset? NextCheckAtUtc { get; set; }
    public bool NeedsFreshWindow { get; set; }
    public int IncorrectAttempts { get; set; }
    public DateTimeOffset? RetryCodeAtUtc { get; set; }
    public string? Error { get; set; }
    public string? RenderError { get; set; }
    public ActionConfirmation? Confirmation { get; set; }
}

public sealed class ActionConfirmation
{
    public string Token { get; set; } = "";
    public ulong ActorId { get; set; }
    public ActionOrigin Origin { get; set; }
    public string Note { get; set; } = "";
    public ActionKind Kind { get; set; }
    public long Version { get; set; }
    public DateTimeOffset? AcceptedAtUtc { get; set; }
    public DateTimeOffset ExpiresAtUtc { get; set; }
}

