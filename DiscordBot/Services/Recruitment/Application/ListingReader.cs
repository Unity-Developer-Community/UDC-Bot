using System.IO;
using DiscordBot.Settings;
using DiscordBot.Utils;
using DiscordBot.Services.Recruitment.Domain.Analysis;
using DiscordBot.Services.Recruitment.Domain.Policies;
using DiscordBot.Services.Recruitment.Infrastructure.Discord;

namespace DiscordBot.Services.Recruitment.Application;

internal sealed class ListingReader(RecruitmentState state, RecruitmentSettings settings, IRecruitmentDiscord discord, TimeProvider time, Action save, AcknowledgementRecovery windows)
{
    private readonly RecruitmentState State = state;
    private readonly RecruitmentSettings _settings = settings;
    private readonly IRecruitmentDiscord _discord = discord;
    private DateTimeOffset Now => time.GetUtcNow();
    private void Save() => save();
    private readonly AcknowledgementRecovery _recoverWindows = windows;

    public async Task<ThreadSnapshot?> ReadAsync(Listing post, CancellationToken ct, bool refresh = false)
    {
        try
        {
            var live = await _discord.GetThreadAsync(post.Id, ct, refresh);
            if (live is not null && (live.OwnerId != post.OwnerId || live.ForumId != post.ForumId))
                throw new InvalidOperationException("Thread identity changed; staff review required.");
            post.AvailabilityUnknown = false;
            if (live is not null) post.Pinned = live.Pinned;
            return live;
        }
        catch { post.AvailabilityUnknown = true; Save(); throw; }
    }

    public void RecordAbsence(Listing post)
    {
        post.Status = ListingStatus.Deleted; post.PendingAction = null; post.UpdatedAt = Now;
        post.PublicationPending = true; post.PublicSendPending = false; post.Hold = null; post.Fault = null;
        post.AvailabilityUnknown = false; post.Deadline = null; _recoverWindows.Remove(post.Id);
        post.LastOutcome = "Thread absent; deletion actor unknown"; post.Version++;
        ListingTransitions.Author(State!, post.OwnerId, Now);
        Save();
    }

    public async Task RefreshBlockersAsync(Listing candidate, CancellationToken ct)
    {
        // Eligibility cannot depend on which half of a bounded sweep happened to run first.
        for (var count = 0; count < 10; count++)
        {
            var id = ListingEligibilityPolicy.Evaluate(State!, candidate, Now, _settings.CooldownDays).BlockingListingId;
            if (id is null) return;
            var blocker = State!.Listings[id];
            try
            {
                if (await ReadAsync(blocker, ct, refresh: true) is not null) return;
                RecordAbsence(blocker);
            }
            catch (Exception ex) when (ex is not IOException && ex is not OperationCanceledException)
            { blocker.Hold = "Author capacity could not be verified; existing slot retained"; Save(); return; }
        }
    }

}
