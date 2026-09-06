using Discord.Interactions;
using Discord.WebSocket;
using DiscordBot.Modules.Base;
using DiscordBot.Policies;
using DiscordBot.Services;
using DiscordBot.Services.Recruitment.Actions;
using DiscordBot.Services.Recruitment.Policy;
using DiscordBot.Services.Recruitment.Publishing;
using DiscordBot.Services.Recruitment.State;
using DiscordBot.Services.Recruitment;
using DiscordBot.Settings.Options;
using Microsoft.Extensions.Options;

namespace DiscordBot.Modules.Recruitment;

// No component-enabled precondition: staff must be able to preview while setup is degraded or stopped.
[Group("recruitment", "Review and publish recruitment forum guidelines")]
public sealed class RecruitmentSetupModule(RecruitmentService service, GuidelinePublisher publisher,
    IBotAuthorizationPolicy authorization, IOptions<DiscordGuildOptions> guild,
    IOptions<RecruitmentOptions> options, StaffActions staff, PublicCoordinator publicCoordinator) : BotInteractionModuleBase
{
    [SlashCommand("preview", "Privately preview guidelines and the current topic/tag fingerprints")]
    public async Task Preview(ForumKind forum)
    {
        await DeferAsync(ephemeral: true);
        try
        {
            RequireStaff();
            var preview = await publisher.PreviewAsync(Forum(forum), CancellationToken.None);
            var embed = new EmbedBuilder().WithTitle("Guidelines preview · ABCDE is a placeholder")
                .WithDescription(preview.Topic)
                .AddField("Current topic fingerprint", preview.CurrentTopicHash)
                .AddField("Current tag fingerprint", preview.CurrentTagHash)
                .WithFooter("Review the existing Discord Guidelines too. Publish explicitly adopts that exact topic; tag repair is optional.").Build();
            await FollowupAsync(embed: embed, ephemeral: true, allowedMentions: AllowedMentions.None);
        }
        catch (Exception error) { await FollowupAsync(RecruitmentOwnerModule.Failure(error), ephemeral: true); }
    }

    [SlashCommand("publish", "Adopt the previewed topic and publish guidelines; optionally repair a renamed Closed binding")]
    public async Task Publish(ForumKind forum,
        [Summary("expected-topic-hash", "Current topic fingerprint from the preview")] string expectedTopicHash,
        [Summary("repair-tag-hash", "Optional current tag fingerprint to release a renamed Closed binding")] string? repairTagHash = null)
    {
        await DeferAsync(ephemeral: true);
        try
        {
            RequireStaff();
            await service.ExecutePublicAsync(async token =>
            {
                RequireStaff();
                await publisher.EnsureAsync(Forum(forum), token, expectedTopicHash, repairTagHash);
                return true;
            });
            await FollowupAsync("Guidelines and Closed tag confirmed for this forum. Advisory recovery will refresh affected posts.", ephemeral: true);
        }
        catch (Exception error) { await FollowupAsync(RecruitmentOwnerModule.Failure(error), ephemeral: true); }
    }

    [SlashCommand("status", "Inspect a recruitment record, including while the component is stopped")]
    public async Task Status(string thread)
    {
        await DeferAsync(ephemeral: true);
        try
        {
            RequireStaff();
            await FollowupAsync(await staff.StatusAsync(ThreadId(thread), CancellationToken.None), ephemeral: true, allowedMentions: AllowedMentions.None);
        }
        catch (Exception error) { await FollowupAsync(RecruitmentOwnerModule.Failure(error), ephemeral: true); }
    }

    [SlashCommand("reconcile", "Refresh one post and its staff-feed evidence without clearing uncertainty")]
    public Task Reconcile(string thread) => RunStaffAsync(thread, async (id, token) =>
    {
        await staff.ReconcileAsync(id, token);
        return "Reconciliation completed. Uncertain history remains held for explicit review.";
    }, publicRequired: false);

    [SlashCommand("adopt", "Accept a reviewed historical listing after capacity/cooldown checks in Enforce mode")]
    public Task Adopt(string thread, string reason) => RunStaffAsync(thread, async (id, token) =>
    {
        await staff.AdoptAsync(id, Context.User.Id, reason, token);
        return "Historical listing adopted. Unanswered closure still requires verified response coverage.";
    });

    [SlashCommand("exempt", "Set or remove a listing exemption with a recorded staff reason")]
    public Task Exempt(string thread, bool enabled, string reason) => RunStaffAsync(thread, async (id, token) =>
    {
        await staff.ExemptAsync(id, enabled, Context.User.Id, reason, token);
        return enabled ? "Listing exempted; pending lifecycle work was cancelled." : "Exemption removed; enabled policy gates apply on the next check.";
    }, publicRequired: false);

    [SlashCommand("waive-cooldown", "Waive existing cooldown anchors in this post's group; preserve active capacity")]
    public Task WaiveCooldown(string thread, string reason) => RunStaffAsync(thread, async (id, token) =>
    {
        await staff.WaiveCooldownAsync(id, Context.User.Id, reason, token);
        return "Current cooldown anchors waived. Active listings and future accepted-listing waits remain in effect.";
    }, publicRequired: false);

    [SlashCommand("restart-acknowledgement", "Grant an unaccepted listing a fresh full window with a recorded reason")]
    public Task RestartAcknowledgement(string thread, string reason) => RunStaffAsync(thread, async (id, token) =>
    {
        await staff.RestartAcknowledgementAsync(id, Context.User.Id, reason, token);
        return "A fresh full window was requested. Any separate history review holds remain in effect.";
    });

    [SlashCommand("reset-timeouts", "Reset the author's consecutive timeout count with a recorded reason")]
    public Task ResetTimeouts(string thread, string reason) => RunStaffAsync(thread, async (id, token) =>
    {
        await staff.ResetTimeoutsAsync(id, Context.User.Id, reason, token);
        return "Consecutive timeout count reset; existing post/action history retained.";
    }, publicRequired: false);

    [SlashCommand("review", "Record a review; choose a response finding only after inspecting the earlier history")]
    public Task Review(string thread, ResponseReview responses, string reason, bool dismiss = false) => RunStaffAsync(thread, async (id, token) =>
    {
        await staff.ReviewAsync(id, responses, dismiss, Context.User.Id, reason, token);
        return "Staff review recorded. Later gaps and messages will still be checked.";
    }, publicRequired: false);

    [SlashCommand("resolve-missing", "Resolve a still-missing post using a verified UTC deletion time and staff reason")]
    public Task ResolveMissing(string thread, string deletedAtUtc, string reason) => RunStaffAsync(thread, async (id, token) =>
    {
        if (!DateTimeOffset.TryParse(deletedAtUtc, System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out var deleted) || deleted.Offset != TimeSpan.Zero)
            throw new InvalidOperationException("Use a UTC timestamp, for example 2026-09-06T12:00:00Z.");
        await staff.ResolveMissingAsync(id, deleted, Context.User.Id, reason, token);
        return "Disappearance resolved; any accepted-deletion cooldown uses the verified timestamp.";
    }, publicRequired: false);

    [SlashCommand("close", "Lock/archive a listing and retain its history")]
    public Task Close(string thread, string reason) => RunStaffAsync(thread, async (id, token) =>
    {
        await staff.ChangeLifecycleAsync(id, ActionKind.LockArchive, Context.User.Id, reason, token);
        return "Listing closed. Archived posts remain publicly discoverable.";
    });

    [SlashCommand("reopen", "Reopen a closed listing after a capacity check and grant a recorded exemption")]
    public Task Reopen(string thread, string reason) => RunStaffAsync(thread, async (id, token) =>
    {
        await staff.ChangeLifecycleAsync(id, ActionKind.Reopen, Context.User.Id, reason, token);
        return "Listing reopened and exempted. Deliberately remove the exemption when its next lifecycle policy is settled.";
    });

    [SlashCommand("remove", "Prepare a private, expiring confirmation to permanently delete a listing")]
    public async Task Remove(string thread, string reason)
    {
        await DeferAsync(ephemeral: true);
        try
        {
            RequireStaff();
            ulong id = ThreadId(thread);
            var confirmation = await service.ExecutePublicAsync(async token =>
            {
                RequireStaff();
                return await staff.PrepareRemovalAsync(id, Context.User.Id, reason, token);
            });
            var controls = new ComponentBuilder().WithButton("Permanently delete", $"udc-recruit:staff-confirm:{id}:{confirmation.Token}", ButtonStyle.Danger).Build();
            await FollowupAsync($"Delete <#{id}> and every reply? This cannot be undone. Accepted-listing history is retained. Confirmation expires in two minutes.",
                components: controls, ephemeral: true, allowedMentions: AllowedMentions.None);
        }
        catch (Exception error) { await FollowupAsync(RecruitmentOwnerModule.Failure(error), ephemeral: true); }
    }

    [ComponentInteraction("udc-recruit:staff-confirm:*:*", ignoreGroupNames: true)]
    public Task ConfirmRemoval(ulong threadId, string confirmation) => RunStaffAsync(threadId.ToString(), async (id, token) =>
    {
        await staff.ConfirmRemovalAsync(id, Context.User.Id, confirmation, token);
        return "Staff removal confirmed; history retained.";
    });

    private async Task RunStaffAsync(string thread, Func<ulong, CancellationToken, Task<string>> action, bool publicRequired = true)
    {
        await DeferAsync(ephemeral: true);
        try
        {
            RequireStaff();
            ulong id = ThreadId(thread);
            async Task<string> Execute(CancellationToken token)
            {
                RequireStaff();
                string result = await action(id, token);
                if (service.IsPublicRunning) await publicCoordinator.TryRefreshPostAsync(id, token);
                return result;
            }
            string result = publicRequired ? await service.ExecutePublicAsync(Execute) : await service.ExecuteManagedAsync(Execute);
            await FollowupAsync(result, ephemeral: true, allowedMentions: AllowedMentions.None);
        }
        catch (Exception error) { await FollowupAsync(RecruitmentOwnerModule.Failure(error), ephemeral: true); }
    }

    private static ulong ThreadId(string value) => ulong.TryParse(value.Trim().Trim('<', '#', '>'), out ulong id) && id != 0
        ? id : throw new InvalidOperationException("Provide the recruitment thread's ID or channel mention.");

    private Forum Forum(ForumKind kind) => ForumClassifier.GetForums(options.Value.Forums).Single(forum => forum.Kind == kind);

    private void RequireStaff()
    {
        if (Context.Guild?.Id != guild.Value.GuildId || Context.User is not SocketGuildUser user ||
            !(user.GuildPermissions.Administrator || authorization.IsModerator(user)))
            throw new InvalidOperationException("Only configured moderators or administrators in this guild can manage recruitment setup.");
    }
}
