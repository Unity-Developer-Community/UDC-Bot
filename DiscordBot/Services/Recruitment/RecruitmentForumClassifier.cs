using DiscordBot.Settings.Options;
using Microsoft.Extensions.Options;

namespace DiscordBot.Services.Recruitment;

public enum RecruitmentForumKind { PaidRecruiting, PaidForHire, HobbyRecruiting, HobbyForHire }
public enum RecruitmentListingGroup { Recruiting, ForHire }

public sealed record RecruitmentForum(RecruitmentForumKind Kind, ulong ChannelId);

/// <summary>Forum identity is independent of whether recruitment moderation is running.</summary>
public sealed class RecruitmentForumClassifier
{
    private readonly IReadOnlyList<RecruitmentForum> _forums;

    public RecruitmentForumClassifier(IOptions<RecruitmentOptions> options)
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

    public RecruitmentForumKind? Classify(ulong channelId, ulong? parentChannelId = null) =>
        _forums.FirstOrDefault(f => f.ChannelId == channelId || f.ChannelId == parentChannelId)?.Kind;

    public static RecruitmentListingGroup GroupOf(RecruitmentForumKind kind) => kind switch
    {
        RecruitmentForumKind.PaidRecruiting or RecruitmentForumKind.HobbyRecruiting => RecruitmentListingGroup.Recruiting,
        RecruitmentForumKind.PaidForHire or RecruitmentForumKind.HobbyForHire => RecruitmentListingGroup.ForHire,
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };

    public static bool IsPaid(RecruitmentForumKind kind) => kind is
        RecruitmentForumKind.PaidRecruiting or RecruitmentForumKind.PaidForHire;

    internal static IReadOnlyList<RecruitmentForum> GetForums(RecruitmentForumsOptions? forums) =>
    [
        new(RecruitmentForumKind.PaidRecruiting, forums?.PaidRecruiting?.ChannelId ?? 0),
        new(RecruitmentForumKind.PaidForHire, forums?.PaidForHire?.ChannelId ?? 0),
        new(RecruitmentForumKind.HobbyRecruiting, forums?.HobbyRecruiting?.ChannelId ?? 0),
        new(RecruitmentForumKind.HobbyForHire, forums?.HobbyForHire?.ChannelId ?? 0)
    ];
}
