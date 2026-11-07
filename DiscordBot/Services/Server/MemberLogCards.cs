namespace DiscordBot.Services.Server;

/// <summary>
/// Card layouts for the member log: joins, leaves and name changes. Inputs are plain values so
/// the layouts can be tested. A line only appears when it carries information.
/// </summary>
public static class MemberLogCards
{
    public static readonly Color JoinedColor = new(120, 200, 120);
    public static readonly Color LeftColor = new(200, 160, 100);
    public static readonly Color RemovedColor = new(220, 90, 90);
    public static readonly Color NameChangedColor = new(130, 170, 240);

    /// <summary>Accounts younger than this when they join are flagged as new.</summary>
    public static readonly TimeSpan NewAccountAge = TimeSpan.FromDays(7);

    private const int MaxReasonLength = 600;
    private const int MaxRolesLength = 1000;
    private const int MaxNameLength = 200;

    public sealed record JoinedMember(
        CardUser User,
        DateTimeOffset CreatedAt,
        DateTimeOffset JoinedAt,
        bool IsBot = false,
        bool DefaultAvatar = false,
        bool Rejoined = false,
        bool PendingScreening = false,
        bool QuarantinedUsername = false);

    /// <summary>Join card. <paramref name="joinSourceLine"/> comes from <see cref="JoinSourceLine"/> once Discord tells us.</summary>
    public static MessageComponent JoinedCard(JoinedMember member, string? joinSourceLine = null)
    {
        var accountAge = member.JoinedAt - member.CreatedAt;
        var created = accountAge < NewAccountAge
            ? $"⚠️ Account created {AuditLogCards.Timestamp(member.CreatedAt, 'R')} (new account)"
            : $"Account created {AuditLogCards.Timestamp(member.CreatedAt, 'R')}";

        var flags = new List<string>();
        if (member.IsBot) flags.Add("Bot");
        if (member.DefaultAvatar) flags.Add("Default avatar");
        if (member.Rejoined) flags.Add("Rejoined");
        if (member.PendingScreening) flags.Add("Pending screening");
        if (member.QuarantinedUsername) flags.Add("Quarantined username");

        return new AuditCard(JoinedColor)
            .WithHeader("📥 Member joined", member.User, null,
                created,
                flags.Count > 0 ? "Flags: " + string.Join(" · ", flags) : "",
                joinSourceLine ?? "")
            .WithFooter($"Joined {AuditLogCards.Timestamp(member.JoinedAt)}")
            .Build();
    }

    /// <summary>How the member joined, or null when Discord doesn't say.</summary>
    public static string? JoinSourceLine(JoinSourceType type, string? inviteCode, ulong? inviterId)
    {
        var code = string.IsNullOrWhiteSpace(inviteCode) ? null : $"`{inviteCode.Replace("`", "'")}`";
        var from = inviterId is { } id and not 0 ? $" from <@{id}>" : "";

        return type switch
        {
            JoinSourceType.VanityUrl => code != null ? $"Joined via vanity URL {code}" : "Joined via vanity URL",
            JoinSourceType.ServerDiscovery => "Joined via Server Discovery",
            JoinSourceType.StudentHub => "Joined via Student Hub",
            JoinSourceType.BotInvite => $"Added by bot invite{from}",
            JoinSourceType.Integration => "Added by an integration",
            JoinSourceType.ManualVerification => "Joined via manual verification",
            // InviteCode, and Unknown or a newer value that still came with an invite.
            _ when code != null => $"Joined via invite {code}{from}",
            _ when from.Length > 0 => $"Joined via an invite{from}",
            JoinSourceType.InviteCode => "Joined via an invite",
            _ => null,
        };
    }

    public enum LeaveKind { Left, Kicked, Banned }

    public sealed record LeftMember(
        CardUser User,
        DateTimeOffset? JoinedAt,
        IReadOnlyList<string> RoleNames,
        LeaveKind Kind = LeaveKind.Left,
        ulong? ModeratorId = null,
        string? ModeratorName = null,
        string? Reason = null);

    public static MessageComponent LeftCard(LeftMember member, DateTimeOffset leftAt)
    {
        var moderator = member.ModeratorId is { } modId
            ? $"<@{modId}>{(member.ModeratorName != null ? $" {Format.Sanitize(member.ModeratorName)}" : "")}"
            : "an unknown moderator";
        var removal = member.Kind switch
        {
            LeaveKind.Kicked => $"👢 Kicked by {moderator}",
            LeaveKind.Banned => $"🔨 Banned by {moderator}",
            _ => "",
        };
        var stay = member.JoinedAt is { } joinedAt
            ? $"Joined {AuditLogCards.Timestamp(joinedAt, 'R')} · stayed {StayDuration(leftAt - joinedAt)}"
            : "";
        var reason = member.Kind != LeaveKind.Left && !string.IsNullOrWhiteSpace(member.Reason)
            ? AuditCard.Fit($"Reason: {Format.Sanitize(member.Reason.Trim())}", MaxReasonLength, "…")
            : "";

        var card = new AuditCard(member.Kind == LeaveKind.Left ? LeftColor : RemovedColor)
            .WithHeader("📤 Member left", member.User, null, removal, reason, stay);

        var roles = member.RoleNames.Where(r => !string.IsNullOrWhiteSpace(r)).ToList();
        if (roles.Count > 0)
            card.AddText("Roles: " + string.Join(", ", roles.Select(Format.Sanitize)), MaxRolesLength);

        return card.WithFooter($"Left {AuditLogCards.Timestamp(leftAt)}").Build();
    }

    public enum NameKind { Nickname, DisplayName, Username }

    /// <summary>One name field's values in order, oldest first. <paramref name="Omitted"/> values were dropped after the first one.</summary>
    public sealed record NameChange(NameKind Kind, IReadOnlyList<string?> Values, int Omitted = 0)
    {
        public int Changes => Math.Max(0, Values.Count - 1) + Omitted;
    }

    public static MessageComponent NameChangedCard(CardUser user, NameKind kind, string? before, string? after, DateTimeOffset changedAt) =>
        NameChangedCard(user, [new NameChange(kind, [before, after])], changedAt, changedAt);

    /// <summary>
    /// A user's name changes within one window. A single change shows Before/After; several show each
    /// field's history, <c>a → b → c</c>.
    /// </summary>
    public static MessageComponent NameChangedCard(CardUser user, IReadOnlyList<NameChange> fields, DateTimeOffset firstAt, DateTimeOffset lastAt)
    {
        var changes = fields.Sum(f => f.Changes);
        var title = fields.Count == 1 ? NameTitle(fields[0].Kind) : "Name changed";

        string[] lines = fields is [{ Values.Count: 2, Omitted: 0 } only]
            ? [$"**Before:** {NameText(only.Values[0])}", $"**After:** {NameText(only.Values[1])}"]
            : fields.Select(f => $"**{NameLabel(f.Kind)}:** {NameHistory(f)}").ToArray();

        var footer = changes <= 1
            ? $"Changed {AuditLogCards.Timestamp(lastAt)}"
            : $"Changed {AuditLogCards.Timestamp(firstAt)} – {AuditLogCards.Timestamp(lastAt, 'T')} · {changes} changes";

        return new AuditCard(NameChangedColor)
            .WithHeader($"🏷️ {title}", user, null, lines)
            .WithFooter(footer)
            .Build();
    }

    private static string NameTitle(NameKind kind) => kind switch
    {
        NameKind.Nickname => "Nickname changed",
        NameKind.DisplayName => "Display name changed",
        _ => "Username changed",
    };

    private static string NameLabel(NameKind kind) => kind switch
    {
        NameKind.Nickname => "Nickname",
        NameKind.DisplayName => "Display name",
        _ => "Username",
    };

    private static string NameHistory(NameChange field)
    {
        var values = field.Values.Select(NameText).ToList();
        if (field.Omitted > 0 && values.Count > 1)
            values.Insert(1, "…");
        return string.Join(" → ", values);
    }

    private static string NameText(string? name) =>
        string.IsNullOrWhiteSpace(name) ? "*(none)*" : AuditCard.Fit(Format.Sanitize(name), MaxNameLength, "…");

    /// <summary>Compact duration: <c>2y 14d</c>, <c>3d 4h</c>, <c>5h 12m</c>, <c>12m</c>, <c>40s</c>.</summary>
    public static string StayDuration(TimeSpan duration)
    {
        if (duration < TimeSpan.Zero)
            duration = TimeSpan.Zero;

        static string Pair(int big, string bigUnit, int small, string smallUnit) =>
            small > 0 ? $"{big}{bigUnit} {small}{smallUnit}" : $"{big}{bigUnit}";

        var days = (int)duration.TotalDays;
        if (days >= 365)
            return Pair(days / 365, "y", days % 365, "d");
        if (days >= 1)
            return Pair(days, "d", duration.Hours, "h");
        if (duration.Hours >= 1)
            return Pair(duration.Hours, "h", duration.Minutes, "m");
        if (duration.Minutes >= 1)
            return $"{duration.Minutes}m";
        return $"{duration.Seconds}s";
    }
}
