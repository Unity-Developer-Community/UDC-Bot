namespace DiscordBot.Services.Recruitment.Domain.Policies;

public static class RecruitmentConditions
{
    public const string None = "";
    public const string Timeout = "timeout";
    public const string Ineligible = "ineligible";
    public const string ClosedTag = "closed-tag";
    public const string LegacyUnanswered = "unanswered";
}
