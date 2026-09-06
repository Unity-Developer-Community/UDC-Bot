using System.IO;

namespace DiscordBot.Services.Recruitment;

internal static class RecruitmentLifecycleValidation
{
    public static void Validate(RecruitmentStateDocument state)
    {
        Dates(state.EnforcementStartedAtUtc, state.LastRetentionAtUtc);
        if (state.RetiredThreadIds is null || state.RetiredThreadIds.Contains(0) ||
            state.RetiredThreadIds.Any(state.Posts.ContainsKey)) Invalid();
        if (state.LastMode is { } mode && !Enum.IsDefined(mode)) Invalid();
        foreach (var author in state.Authors.Values)
        {
            Dates(author.LastActivityAtUtc);
            if (author.TimeoutAlertActionId is { } alert) Token(alert);
            foreach (var group in author.Groups.Values)
            {
                if (group.Waiver is not { } waiver) continue;
                Dates(waiver.GrantedAtUtc, waiver.ThroughCreatedAtUtc, waiver.ThroughDeletedAtUtc);
                if (waiver.ActorId == 0 || !Note(waiver.Reason)) Invalid();
            }
        }
        foreach (var post in state.Posts.Values)
        {
            Dates(post.EnforcementNextCheckAtUtc, post.HistoryReviewedThroughUtc);
            if (post.Audit is null) Invalid();
            foreach (var record in post.Audit!)
            {
                if (record is null || !Note(record.Operation) || !Note(record.Reason)) Invalid();
                Token(record!.Id);
                Dates(record.AtUtc);
            }
            if (post.PendingAction is not { } action) continue;
            Token(action.Id);
            Dates(action.RequestedAtUtc, action.AttemptedAtUtc, action.CompletedAtUtc, action.CancelledAtUtc, action.AcceptedAtUtc);
            if (action.Kind is not (RecruitmentActionKind.Delete or RecruitmentActionKind.LockArchive or RecruitmentActionKind.Reopen) ||
                !Enum.IsDefined(action.Origin) || !Enum.IsDefined(action.Reason) || action.ExpectedVersion < 0 ||
                action.Origin != RecruitmentActionOrigin.Automatic && action.ActorId == 0 || !Note(action.Note) ||
                action.CompletedAtUtc is not null && action.CancelledAtUtc is not null || action.TimeoutCountAfter is < 1 ||
                action.AttemptedAtUtc < action.RequestedAtUtc || action.CompletedAtUtc < action.RequestedAtUtc ||
                action.CancelledAtUtc < action.RequestedAtUtc) Invalid();
        }
    }

    private static bool Note(string? value) => !string.IsNullOrWhiteSpace(value) && value.Length <= 200;
    private static void Token(string? value)
    {
        if (value is not { Length: 16 } || !value.All(Uri.IsHexDigit)) Invalid();
    }
    private static void Dates(params DateTimeOffset?[] values)
    {
        if (values.Any(value => value is { } date && (date == default || date.Offset != TimeSpan.Zero))) Invalid();
    }
    private static void Invalid() => throw new InvalidDataException("Invalid recruitment lifecycle metadata.");
}
