using DiscordBot.Utils;
using Discord.Net;
using System.Net;

namespace DiscordBot.Services.Recruitment;

/// <summary>A durable hold or retry deadline. Null RetryAt requires a relevant change or explicit recovery.</summary>
public sealed record RecruitmentFault(string Message, DateTimeOffset? RetryAt, int Attempts = 1)
{
    public bool IsDue(DateTimeOffset now) => RetryAt.HasValue && RetryAt <= now;

    public static RecruitmentFault From(Exception error, DateTimeOffset now, RecruitmentFault? previous = null)
    {
        var attempts = Math.Min((previous?.Attempts ?? 0) + 1, 10);
        var permanent = error is InvalidOperationException || error is HttpException http &&
            (int)http.HttpCode is >= 400 and < 500 && http.HttpCode != HttpStatusCode.TooManyRequests;
        var message = error is HttpException discord
            ? $"Discord {(int)discord.HttpCode}/{(int?)discord.DiscordCode}: {discord.Message}"
            : error.Message;
        var retry = error is RecruitmentDeferredException deferred ? deferred.RetryAt
            : permanent ? (DateTimeOffset?)null : now.AddSeconds(Math.Min(900, 15 * Math.Pow(2, attempts - 1)) + Random.Shared.Next(1, 6));
        return new RecruitmentFault(DiscordDisplayText.Literal(message, 400), retry, attempts);
    }
}

internal sealed class RecruitmentDeferredException(DateTimeOffset retryAt) : Exception("Discord rate limit; retry scheduled")
{
    public DateTimeOffset RetryAt { get; } = retryAt;
}
