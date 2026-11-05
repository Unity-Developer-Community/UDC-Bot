using System.Net;
using System.Net.Http.Headers;
using DiscordBot.Settings;

namespace CasinoWeb.Auth;

/// <summary>Uses <c>GET /users/@me/guilds/{guild}/member</c> (scope <c>guilds.members.read</c>); the bot token is never involved.</summary>
public sealed class DiscordGuildMembershipChecker(HttpClient http, BotSettings settings, ILogger<DiscordGuildMembershipChecker> logger) : IGuildMembershipChecker
{
    public static readonly Uri ApiBaseAddress = new("https://discord.com/api/v10/");

    public async Task<MembershipStatus> CheckAsync(string accessToken, CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"users/@me/guilds/{settings.GuildId}/member");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

            switch (response.StatusCode)
            {
                case HttpStatusCode.OK:
                    return MembershipStatus.Member;
                case HttpStatusCode.NotFound:
                case HttpStatusCode.Unauthorized:
                case HttpStatusCode.Forbidden:
                    return MembershipStatus.NotMember;
                default:
                    logger.LogWarning("Discord membership check answered {Status}", (int)response.StatusCode);
                    return MembershipStatus.Unavailable;
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Discord membership check failed");
            return MembershipStatus.Unavailable;
        }
    }
}
