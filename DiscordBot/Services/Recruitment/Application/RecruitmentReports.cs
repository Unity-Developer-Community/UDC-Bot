using DiscordBot.Services.Recruitment.Infrastructure.Discord;
using DiscordBot.Utils;
using DiscordBot.Services.Recruitment.Domain.Policies;
namespace DiscordBot.Services.Recruitment;

public sealed partial class RecruitmentEngine
{
    private async Task<Listing?> ReaderListingAsync(string guild, string thread, string reader, CancellationToken ct) =>
        State is not null && guild == State.GuildId && State.Listings.TryGetValue(thread, out var post) &&
        post.Status is not ListingStatus.Reference && await _discord.IsMemberAsync(guild, reader, ct) ? post : null;

    public async Task<string> DetailsAsync(string guild, string thread, string reader, CancellationToken ct)
    {
        var post = await ReaderListingAsync(guild, thread, reader, ct);
        return post is null ? "Listing details are unavailable in this server." : RecruitmentPresentation.Details(post);
    }

    public async Task<(string Message, bool Open, bool Removable)> OwnerDetailsAsync(string guild, string thread, string owner, CancellationToken ct)
    {
        var post = Owner(guild, thread, owner);
        if (post is null) return ("This listing does not belong to you or recruitment is unavailable.", false, false);
        return ($"**Your listing · {post.Status}**\n" + await EligibilityAsync(guild, owner, ct) +
            "\nClosing preserves the discussion. Removal permanently deletes it.", post.Status == ListingStatus.Open,
            post.Status is ListingStatus.Open or ListingStatus.Closed);
    }

    public async Task<string> ReportAsync(string guild, string thread, string reporter, string concern, CancellationToken ct)
    {
        var post = await ReaderListingAsync(guild, thread, reporter, ct);
        if (post is null) return "This listing is unavailable or you are not a current server member.";
        if (post.OwnerId == reporter) return "Use your listing controls or contact a moderator about your own listing.";
        if (!_ready || !_feedReady || !_gatewayReady() || _feedGeneration != _evidenceGeneration())
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
        if (firstOpen) QueueAttention(post, "A private reader report needs review");
        post.UpdatedAt = Now; post.PublicationPending = true; Save();
        // The durable report is accepted even if notification delivery needs a retry.
        await ProcessSafelyAsync(post, ct);
        return "Your report has been saved for moderators. It is not shown to the listing author or other readers. Keep relevant messages and links; the bot cannot read other people's DMs.";
    }

    private void QueueAttention(Listing post, string reason)
    {
        // Bound retained receipts. During prolonged feed failure, coalesce rather than grow without limit.
        if (post.Attention.Count >= 8)
        {
            var completed = post.Attention.FirstOrDefault(n => n.MessageId is not null && !n.SendPending);
            if (completed is null) return; // Existing pending notices and the overview still link this listing.
            post.Attention.Remove(completed);
        }
        post.Attention.Add(new AttentionNotice { At = Now, Reason = reason });
    }

    private bool AttentionDue(Listing post) => post.Attention.Any(n => (n.MessageId is null || n.SendPending) && (n.Fault is null || n.Fault.IsDue(Now)));

    private async Task PublishAttentionAsync(Listing post, CancellationToken ct)
    {
        foreach (var notice in post.Attention.Where(n => (n.MessageId is null || n.SendPending) && (n.Fault is null || n.Fault.IsDue(Now))).Take(3))
        {
            try
            {
                var marker = $"recruitment:{post.Id}:attention:{notice.Id}";
                var message = new RecruitmentMessage($"**Recruitment review** · {notice.Reason}\n<#{post.Id}> · author `{post.OwnerId}`\nUse `/recruitment lookup {post.Id}` for evidence and review actions.\n||{marker}||", marker, false);
                var delivery = await _discord.UpsertAsync(_settings.FeedChannelId.ToString(), notice.MessageId, notice.SendPending,
                    notice.At, message, () => { GuardFeedWrite(); notice.SendPending = true; Save(); }, ct);
                notice.MessageId = delivery.Id; notice.SendPending = false; notice.Fault = null; Save();
            }
            catch (Exception ex) when (ex is not OperationCanceledException && ex is not System.IO.IOException)
            {
                notice.Fault = RecruitmentFaultClassifier.From(ex, Now, notice.Fault); Save();
            }
        }
    }
}
