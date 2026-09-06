using System.IO;
using DiscordBot.Services.Recruitment.Publishing;

namespace DiscordBot.Services.Recruitment.State;

internal static class PublicationValidation
{
    public static void Validate(StateDocument state)
    {
        foreach (var forum in state.Forums.Values)
        {
            var publication = forum?.Publication;
            if (publication is null || publication.ClosedTagId == 0 || publication.Error?.Length > 200)
            {
                throw new InvalidDataException("Invalid recruitment publication metadata.");
            }
            CheckDate(publication.CheckedAtUtc);
            if (publication.Confirmed is { PublishedAtUtc: null })
                throw new InvalidDataException("A confirmed publication requires a read-back receipt time.");
            foreach (var receipt in new[] { publication.Confirmed, publication.Candidate }.OfType<GuidelineReceipt>())
            {
                if (!GuidelineTemplates.IsCode(receipt.Code) || !IsHash(receipt.TopicHash) || !IsHash(receipt.TemplateHash) ||
                    receipt.WeekStartUtc != GuidelineTemplates.WeekStart(receipt.WeekStartUtc))
                {
                    throw new InvalidDataException("Invalid recruitment guideline receipt.");
                }
                CheckDate(receipt.WeekStartUtc);
                CheckDate(receipt.PublishedAtUtc);
            }
            if (publication.Candidate is not null && !IsHash(publication.ExpectedTopicHash))
            {
                throw new InvalidDataException("A candidate publication requires its previous topic hash.");
            }
        }
        foreach (var post in state.Posts.Values)
        {
            var advisory = post?.Advisory;
            if (advisory is null || advisory.Version < 0 || advisory.IncorrectAttempts < 0 ||
                advisory.Generation is null || advisory.Generation.Length is not (0 or 16) ||
                advisory.Error?.Length > 200 || advisory.RenderError?.Length > 200 || advisory.SearchBeforeId == 0)
            {
                throw new InvalidDataException("Invalid recruitment advisory metadata.");
            }
            if (advisory.Generation.Length != 0) CheckToken(advisory.Generation);
            CheckDate(advisory.SendRequestedAtUtc, advisory.NextCheckAtUtc, advisory.RetryCodeAtUtc);
            if (advisory.Confirmation is { } confirmation)
            {
                CheckToken(confirmation.Token);
                CheckAction(confirmation.Kind);
                CheckDate(confirmation.ExpiresAtUtc, confirmation.AcceptedAtUtc);
                if (confirmation.Version < 0 || confirmation.ActorId == 0 || !Enum.IsDefined(confirmation.Origin) || confirmation.Note is null || confirmation.Note.Length > 200) throw new InvalidDataException("Invalid confirmation version.");
            }

        }
    }

    private static bool IsHash(string? value) => value is { Length: 64 } && value.All(Uri.IsHexDigit);
    private static void CheckToken(string value)
    {
        if (value is not { Length: 16 } || !value.All(Uri.IsHexDigit)) throw new InvalidDataException("Invalid action token.");
    }
    private static void CheckAction(ActionKind kind)
    {
        if (kind is not (ActionKind.Delete or ActionKind.LockArchive)) throw new InvalidDataException("Invalid owner action.");
    }
    private static void CheckDate(params DateTimeOffset?[] values)
    {
        if (values.Any(value => value is { } date && (date == default || date.Offset != TimeSpan.Zero)))
            throw new InvalidDataException("Publication dates must be non-default UTC values.");
    }
}
