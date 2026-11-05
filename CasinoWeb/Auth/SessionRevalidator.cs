using System.Globalization;
using Microsoft.AspNetCore.Authentication;

namespace CasinoWeb.Auth;

public enum SessionCheck
{
    /// <summary>Checked recently enough, nothing to do.</summary>
    Fresh,
    /// <summary>Still valid; the properties changed and the cookie must be re-issued.</summary>
    Renewed,
    /// <summary>No longer in the guild (or the login expired): sign the player out.</summary>
    Revoked,
}

/// <summary>Keeps a login valid only while the player is still in the guild, asking Discord at most once per <see cref="Interval"/>.</summary>
public sealed class SessionRevalidator(IGuildMembershipChecker checker, TimeProvider clock, ILogger<SessionRevalidator> logger)
{
    public static readonly TimeSpan Interval = TimeSpan.FromMinutes(10);
    /// <summary>When Discord cannot answer, the session is kept and asked again after this delay.</summary>
    public static readonly TimeSpan RetryDelay = TimeSpan.FromMinutes(1);

    public const string AccessTokenName = "access_token";
    public const string CheckedAtKey = "casino.membership_checked_at";

    public async Task<SessionCheck> CheckAsync(AuthenticationProperties properties, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        if (properties.Items.TryGetValue(CheckedAtKey, out var raw)
            && DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var checkedAt)
            && now - checkedAt < Interval)
        {
            return SessionCheck.Fresh;
        }

        var token = properties.GetTokenValue(AccessTokenName);
        if (string.IsNullOrEmpty(token))
            return SessionCheck.Revoked;

        switch (await checker.CheckAsync(token, cancellationToken))
        {
            case MembershipStatus.Member:
                MarkChecked(properties, now);
                return SessionCheck.Renewed;
            case MembershipStatus.Unavailable:
                logger.LogWarning("Membership could not be verified; keeping the session and retrying in {Delay}", RetryDelay);
                MarkChecked(properties, now - Interval + RetryDelay);
                return SessionCheck.Renewed;
            default:
                return SessionCheck.Revoked;
        }
    }

    public static void MarkChecked(AuthenticationProperties properties, DateTimeOffset at) =>
        properties.Items[CheckedAtKey] = at.ToString("O", CultureInfo.InvariantCulture);
}
