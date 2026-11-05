namespace CasinoWeb.Auth;

public enum MembershipStatus
{
    Member,
    /// <summary>Not in the guild, or the access token is no longer valid: either way the player must log in again.</summary>
    NotMember,
    /// <summary>Discord could not answer (outage, rate limit). The caller decides whether to keep the session.</summary>
    Unavailable,
}

public interface IGuildMembershipChecker
{
    /// <summary>Asks Discord, with the player's own access token, whether they are in the casino's guild.</summary>
    Task<MembershipStatus> CheckAsync(string accessToken, CancellationToken cancellationToken);
}
