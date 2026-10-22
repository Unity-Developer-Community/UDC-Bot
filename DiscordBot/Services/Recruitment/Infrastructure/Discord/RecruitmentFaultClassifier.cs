using Discord.Net;
using System.Net;
using DiscordBot.Utils;

namespace DiscordBot.Services.Recruitment.Infrastructure.Discord;

public static class RecruitmentFaultClassifier
{
    public static RecruitmentFault From(Exception error, DateTimeOffset now, RecruitmentFault? previous = null, Func<int>? jitterSeconds = null)
    {
        var attempts = Math.Min((previous?.Attempts ?? 0) + 1, 10);
        var permanent = error is InvalidOperationException || error is HttpException http &&
            (int)http.HttpCode is >= 400 and < 500 && http.HttpCode != HttpStatusCode.TooManyRequests;
        var message = error is HttpException discord
            ? $"Discord {(int)discord.HttpCode}/{(int?)discord.DiscordCode}: {discord.Message}"
            : error.Message;
        var retry = error is RecruitmentDeferredException deferred ? deferred.RetryAt
            : permanent ? (DateTimeOffset?)null : now.AddSeconds(RecruitmentBackoff.Seconds(attempts, (jitterSeconds ?? (() => Random.Shared.Next(1, 6)))()));
        return new RecruitmentFault(DiscordDisplayText.Literal(message, 400), retry, attempts);
    }
}

internal static class RecruitmentBackoff
{
    public static double Seconds(int attempts, int jitterSeconds) => Math.Min(900, 15 * Math.Pow(2, attempts - 1)) + jitterSeconds;
}

internal sealed class RecruitmentDeferredException(DateTimeOffset retryAt) : Exception("Discord rate limit; retry scheduled")
{
    public DateTimeOffset RetryAt { get; } = retryAt;
}
