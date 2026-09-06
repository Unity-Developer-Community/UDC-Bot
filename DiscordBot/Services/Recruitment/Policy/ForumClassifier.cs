using DiscordBot.Settings.Options;
using Microsoft.Extensions.Options;

namespace DiscordBot.Services.Recruitment.Policy;

public enum ForumKind { PaidRecruiting, PaidForHire, HobbyRecruiting, HobbyForHire }
public enum ListingGroup { Recruiting, ForHire }

public sealed record Forum(ForumKind Kind, ulong ChannelId);

/// <summary>Forum identity is independent of whether recruitment moderation is running.</summary>
public sealed class ForumClassifier
{
    private readonly IReadOnlyList<Forum> _forums;

    public ForumClassifier(IOptions<RecruitmentOptions> options)
    {
        try
        {
            var forums = GetForums(options.Value.Forums);
            // An ambiguous mapping must not classify an unrelated channel.
            _forums = forums.All(f => f.ChannelId != 0) && forums.Select(f => f.ChannelId).Distinct().Count() == 4
                ? forums : [];
        }
        catch (Exception exception) when (exception is InvalidOperationException or OptionsValidationException)
        {
            // Bad optional recruitment configuration must not stop the XP service.
            _forums = [];
        }
    }

    public ForumKind? Classify(ulong channelId, ulong? parentChannelId = null) =>
        _forums.FirstOrDefault(f => f.ChannelId == channelId || f.ChannelId == parentChannelId)?.Kind;

    public static ListingGroup GroupOf(ForumKind kind) => kind switch
    {
        ForumKind.PaidRecruiting or ForumKind.HobbyRecruiting => ListingGroup.Recruiting,
        ForumKind.PaidForHire or ForumKind.HobbyForHire => ListingGroup.ForHire,
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };

    public static bool IsPaid(ForumKind kind) => kind is
        ForumKind.PaidRecruiting or ForumKind.PaidForHire;

    internal static IReadOnlyList<Forum> GetForums(RecruitmentForumsOptions? forums) =>
    [
        new(ForumKind.PaidRecruiting, forums?.PaidRecruiting?.ChannelId ?? 0),
        new(ForumKind.PaidForHire, forums?.PaidForHire?.ChannelId ?? 0),
        new(ForumKind.HobbyRecruiting, forums?.HobbyRecruiting?.ChannelId ?? 0),
        new(ForumKind.HobbyForHire, forums?.HobbyForHire?.ChannelId ?? 0)
    ];
}
