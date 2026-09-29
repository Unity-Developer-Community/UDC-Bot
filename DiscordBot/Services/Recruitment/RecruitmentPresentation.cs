using System.IO;
using DiscordBot.Settings;
using ImageMagick;
using ImageMagick.Drawing;

namespace DiscordBot.Services.Recruitment;

public static class RecruitmentPresentation
{
    public static string Facts(Listing post) =>
        $"Initial snapshot · account created {Date(post.Facts.AccountCreatedAt)} · joined {Date(post.Facts.JoinedAt)}\n" +
        $"Community activity: {post.Facts.Activity}. Payment: {post.Facts.Payment}.\n" +
        $"Earlier accepted listing: {(post.Facts.PriorAcceptedListing ? "recorded" : "none recorded")}. {post.Facts.TagAdvice}.";

    private static string Date(DateTimeOffset? date) => date?.ToString("yyyy-MM-dd") ?? "unknown";
    public static string Timestamp(DateTimeOffset date) => $"<t:{date.ToUnixTimeSeconds()}:F>";
    public static string Safe(string value, int max = 180) => new string(value.Take(max)
        .Select(c => char.IsControl(c) || "`*_~|<>@".Contains(c) ? ' ' : c).ToArray());

    public static RecruitmentMessage Public(RecruitmentState state, Listing post, RecruitmentSettings settings, DateTimeOffset now, byte[]? image = null)
    {
        var eligibility = RecruitmentPolicy.Evaluate(state, post, now, settings.CooldownDays);
        var status = post.Status != ListingStatus.Open ? post.Status.ToString() : post.AcceptedAt.HasValue ? "Accepted" :
            post.AcknowledgedAt.HasValue ? "Guidelines acknowledged; waiting for eligibility" : "Read this forum's Guidelines and acknowledge the code";
        var text = $"**{status}**\n";
        if (post.Status == ListingStatus.Open && !post.AcceptedAt.HasValue)
        {
            text += post.Deadline.HasValue ? $"Acknowledgement window ends {Timestamp(post.Deadline.Value)}.\n" : "Your full acknowledgement window starts when these controls are confirmed available.\n";
            if (eligibility.BlockingListingId is not null)
                text += $"Your other listing <#{eligibility.BlockingListingId}> occupies your one listing slot.\n";
            if (eligibility.NextAllowedAt.HasValue) text += $"Creation cooldown ends {Timestamp(eligibility.NextAllowedAt.Value)}.\n";
        }
        if (post.AcceptedAt.HasValue)
        {
            var next = state.Authors.GetValueOrDefault(post.OwnerId)?.LastAcceptedCreation?.AddDays(settings.CooldownDays);
            text += next.HasValue ? $"Replacement cooldown: {Timestamp(next.Value)}. Close this listing before replacing it.\n"
                : "Creation cooldown waived. Close this listing before replacing it.\n";
        }
        if (post.Exempt || post.Pinned) text += "Protected from automatic closure/removal.\n";
        if (post.Hold is not null || post.AvailabilityUnknown) text += "Processing is waiting for moderator review or Discord recovery.\n";
        text += "\n" + Facts(post) + "\nAgree on scope and payment in writing; verify claims independently. These facts are not an endorsement.";
        var marker = $"recruitment:{post.Id}:public";
        return new RecruitmentMessage(text + $"\n||{marker}||", marker, post.Status == ListingStatus.Open, image, Facts(post));
    }

    public static RecruitmentMessage Staff(RecruitmentState state, Listing post, RecruitmentSettings settings)
    {
        var author = state.Authors.GetValueOrDefault(post.OwnerId);
        var next = author?.LastAcceptedCreation?.AddDays(settings.CooldownDays);
        var text = $"**Recruitment · {post.Status}** · {Safe(post.Title)}\n" +
            $"Author `{post.OwnerId}` · thread `{post.Id}` · forum `{post.ForumId}`\n" +
            $"https://discord.com/channels/{state.GuildId}/{post.Id}\n" +
            $"Created {Timestamp(post.CreatedAt)} · acknowledged {(post.AcknowledgedAt.HasValue ? "yes" : "no")} · accepted {(post.AcceptedAt.HasValue ? "yes" : "no")}\n" +
            $"Next creation: {(next.HasValue ? Timestamp(next.Value) : "no accepted cooldown")}\n" +
            $"Response: {(post.HasQualifyingReply ? "qualifying reply recorded" : post.ResponseCoverageComplete ? "none observed" : "history incomplete; unanswered closure held")}\n" +
            $"Outcome: {Safe(post.LastOutcome)}\n";
        if (post.PendingAction is { } action)
            text += $"Pending {action.Kind}: {Safe(action.Reason)} · actor `{action.ActorId}` · action `{action.Id}`\n";
        if (post.Hold is not null) text += $"Hold: {Safe(post.Hold)}\n";
        if (post.TimeoutAlertCount >= 3) text += $"**Moderator attention: {post.TimeoutAlertCount} consecutive confirmed Guidelines timeouts.**\n";
        foreach (var audit in post.Audit.TakeLast(3)) text += $"{audit.At:yyyy-MM-dd HH:mm} · `{audit.Actor}` · {Safe(audit.Reason, 180)}\n";
        var marker = $"recruitment:{post.Id}:staff";
        if (text.Length > 1900) text = text[..1880] + "\n… more in stored audit\n";
        return new RecruitmentMessage(text + $"||{marker}||", marker, false);
    }

    public static byte[]? RenderBanner(Listing post, string assetsRoot)
    {
        try
        {
            var font = Path.Combine(assetsRoot, "fonts", "OpenSans-Regular.ttf");
            if (!File.Exists(font)) return null;
            // Fixed size, local font and bounded, bot-generated text only. No remote images or member markup.
            using var image = new MagickImage(new MagickColor("#17212D"), 900, 270);
            var draw = new Drawables().Font(font).FillColor(new MagickColor("#F0F4F8")).FontPointSize(25)
                .Text(28, 42, "RECRUITMENT · INITIAL SNAPSHOT").FontPointSize(19)
                .Text(28, 84, $"Account: {Date(post.Facts.AccountCreatedAt)}     Joined: {Date(post.Facts.JoinedAt)}")
                .Text(28, 121, $"Community activity: {post.Facts.Activity}")
                .Text(28, 158, $"Payment: {post.Facts.Payment}")
                .FontPointSize(17).FillColor(new MagickColor("#B8C8D9"))
                .Text(28, 208, "Agree on scope and terms in writing. Verify claims independently.")
                .Text(28, 239, "Context only; these facts are not an endorsement.");
            draw.Draw(image);
            var bytes = image.ToByteArray(MagickFormat.Png);
            return bytes.Length <= 512 * 1024 ? bytes : null;
        }
        catch { return null; } // Rendering is cosmetic; essential information is also text.
    }
}
