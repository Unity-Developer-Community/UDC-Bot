using DiscordBot.Settings;
using DiscordBot.Utils;
using DiscordBot.Services.Recruitment.Domain.Policies;
using DiscordBot.Services.Recruitment.Presentation;

namespace DiscordBot.Services.Recruitment.Application;

internal sealed class ListingReportCommands(
    RecruitmentState state,
    TimeProvider time,
    Action save,
    ListingQueries queries,
    ListingPublisher publisher,
    ListingWorkflow workflow,
    Func<bool> reportingReady)
{
    private readonly RecruitmentState State = state;
    private DateTimeOffset Now => time.GetUtcNow();
    private readonly ListingPublisher _publisher = publisher;
    private readonly ListingWorkflow _workflow = workflow;
    private void Save() => save();

    public async Task<string> ReportAsync(string guild, string thread, string reporter, string concern, CancellationToken ct)
    {
        var post = await queries.ReaderListingAsync(guild, thread, reporter, ct);
        if (post is null) return "This listing is unavailable or you are not a current server member.";
        if (post.OwnerId == reporter) return "Use your listing controls or contact a moderator about your own listing.";
        if (!reportingReady())
            return "Private reporting is temporarily unavailable. Please contact a moderator directly.";
        concern = concern.Trim();
        if (concern.Length is < 10 or > 1000) return "Describe the concern in 10–1,000 characters, including relevant context.";
        if (post.Reports.Any(r => r.ReporterId == reporter)) return "You have already reported this listing. Contact a moderator with additional evidence.";
        if (post.Reports.Count >= 20) return "This listing already has reports awaiting or recorded by moderators. Contact a moderator with additional evidence.";
        var history = ListingTransitions.Author(State!, reporter, Now);
        history.RecentReports.RemoveAll(d => d <= Now.AddDays(-1));
        if (history.RecentReports.Count >= 3) return "You have reached the limit of three listing reports in 24 hours. Contact a moderator for urgent concerns.";
        var firstOpen = post.Reports.All(r => r.ResolvedAt.HasValue);
        post.Reports.Add(new RecruitmentReport { ReporterId = reporter, At = Now, Concern = DiscordDisplayText.Literal(concern, 1000) });
        history.RecentReports.Add(Now);
        if (firstOpen) _publisher!.QueueAttention(post, "A private reader report needs review");
        post.UpdatedAt = Now; post.PublicationPending = true; Save();
        // The durable report is accepted even if notification delivery needs a retry.
        await _workflow!.ProcessSafelyAsync(post, ct);
        return "Your report has been saved for moderators. It is not shown to the listing author or other readers. Keep relevant messages and links; the bot cannot read other people's DMs.";
    }

}
