namespace DiscordBot.Services.Recruitment;

/// <summary>A durable hold or retry deadline. Null RetryAt requires a relevant change or explicit recovery.</summary>
public sealed record RecruitmentFault(string Message, DateTimeOffset? RetryAt, int Attempts = 1)
{
    public bool IsDue(DateTimeOffset now) => RetryAt.HasValue && RetryAt <= now;
}
