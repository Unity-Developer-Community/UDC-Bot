using Discord.Interactions;
using DiscordBot.Services.Recruitment;

namespace DiscordBot.Modules.Recruitment;

/// <summary>Owner controls and moderator entry points; status reads a detached snapshot without joining the work queue.</summary>
[Group("recruitment", "Recruitment moderation and recovery")]
public sealed class RecruitmentModule : InteractionModuleBase<SocketInteractionContext>
{
    public RecruitmentService Recruitment { get; set; } = null!;
    private string Guild => Context.Guild?.Id.ToString() ?? "";
    private string Actor => Context.User.Id.ToString();
    private string Thread => Context.Channel.Id.ToString();

    [SlashCommand("eligibility", "Check your posting slot, cooldown and attempt limit privately")]
    public async Task Eligibility()
    {
        await DeferAsync(ephemeral: true);
        await Reply(() => Recruitment.RunAsync((engine, ct) => engine.EligibilityAsync(Guild, Actor, ct)));
    }

    [SlashCommand("status", "Show recruitment availability")]
    public async Task Status()
    {
        await DeferAsync(ephemeral: true);
        if (!Recruitment.CanReadStatus(Guild, Actor))
        { await FollowupAsync("Moderator access is required in the configured server.", ephemeral: true); return; }
        await FollowupAsync(embed: Recruitment.Health.Embed(), ephemeral: true, allowedMentions: AllowedMentions.None);
    }
    [SlashCommand("lookup", "Look up a managed thread or author ID")]
    public Task Lookup(string id) => Staff("lookup", id);
    [SlashCommand("preview", "Preview managed forum setup")]
    public Task Preview([Choice("Paid recruiting", "paid-recruiting"), Choice("Paid for hire", "paid-for-hire"),
        Choice("Hobby recruiting", "hobby-recruiting"), Choice("Hobby for hire", "hobby-for-hire")] string forum = "") => Staff("preview", forum);
    [SlashCommand("repair", "Publish and verify managed Guidelines and tags")]
    public Task Repair(string reason, [Choice("All forums", ""), Choice("Paid recruiting", "paid-recruiting"), Choice("Paid for hire", "paid-for-hire"),
        Choice("Hobby recruiting", "hobby-recruiting"), Choice("Hobby for hire", "hobby-for-hire")] string forum = "") => Staff("repair", forum, reason);
    [SlashCommand("reconcile", "Reconcile Discord state and pending actions")]
    public Task Reconcile(string reason) => Staff("reconcile", reason: reason);
    [SlashCommand("manage", "Change a listing or review uncertain evidence")]
    public Task Manage(
        [Choice("Exempt", "exempt"), Choice("Remove exemption", "unexempt"), Choice("Reset acknowledgement/timeouts", "reset"), Choice("Reset timeout streak", "reset-timeouts"), Choice("Reset posting attempts", "reset-attempts"),
         Choice("Resolve reader reports", "resolve-reports"), Choice("Mark material changes reviewed", "review-changes"),
         Choice("Close", "close"), Choice("Reopen", "reopen"), Choice("Designate reference post", "reference"),
         Choice("Confirm unavailable thread is absent", "review-absent")] string operation,
        string threadId, string reason) => Staff(operation, threadId, reason);
    [SlashCommand("waive-cooldown", "Waive an author's creation cooldown without granting a second slot")]
    public Task Waive(IUser author, string reason) => Staff("waive-cooldown", author.Id.ToString(), reason);

    [SlashCommand("portfolio-sites", "List or change recognized portfolio sites (administrator only)")]
    [RequireUserPermission(GuildPermission.Administrator)]
    public async Task PortfolioSites([Choice("List", "list"), Choice("Add", "add"), Choice("Remove", "remove")] string operation = "list",
        string domain = "", string reason = "")
    {
        await DeferAsync(ephemeral: true);
        await Reply(() => Recruitment.RunAsync((engine, ct) => engine.PortfolioSitesAsync(Guild, Actor, operation, domain, reason, ct)));
    }

    private async Task Staff(string operation, string target = "", string reason = "")
    {
        await DeferAsync(ephemeral: true);
        if (operation is "repair" or "reconcile")
        { await FollowupAsync(Recruitment.QueueRecovery(Guild, Actor, operation, target, reason), ephemeral: true, allowedMentions: AllowedMentions.None); return; }
        await Reply(() => Recruitment.RunAsync((engine, ct) => engine.StaffAsync(Guild, Actor, operation, target, reason, ct)));
    }

    [ComponentInteraction("recruit:details:*", true)]
    public async Task Details(string thread)
    {
        await DeferAsync(ephemeral: true);
        if (thread != Thread) { await FollowupAsync("This control belongs to another thread.", ephemeral: true); return; }
        await Reply(() => Recruitment.RunAsync((engine, ct) => engine.DetailsAsync(Guild, thread, Actor, ct)));
    }

    [ComponentInteraction("recruit:owner:*", true)]
    public async Task OwnerControls(string thread)
    {
        await DeferAsync(ephemeral: true);
        if (thread != Thread) { await FollowupAsync("This control belongs to another thread.", ephemeral: true); return; }
        try
        {
            var result = await Recruitment.RunAsync((engine, ct) => engine.OwnerDetailsAsync(Guild, thread, Actor, ct));
            var controls = new ComponentBuilder();
            if (result.Open) controls.WithButton("Close listing", $"recruit:close:{thread}", ButtonStyle.Secondary);
            if (result.Removable) controls.WithButton("Remove post", $"recruit:remove:{thread}", ButtonStyle.Danger);
            await FollowupAsync(result.Message, ephemeral: true, components: controls.Build(), allowedMentions: AllowedMentions.None);
        }
        catch { await FollowupAsync("Listing controls are unavailable. Please try again or contact a moderator.", ephemeral: true); }
    }

    [ComponentInteraction("recruit:report:*", true)]
    public async Task Report(string thread)
    {
        if (thread != Thread || Context.Guild is null) { await RespondAsync("This control belongs to another thread.", ephemeral: true); return; }
        await RespondWithModalAsync<ConcernModal>($"recruit:concern:{thread}");
    }
    public sealed class ConcernModal : IModal
    {
        public string Title => "Private report to moderators";
        [InputLabel("Concern and relevant message links or context")]
        [ModalTextInput("concern", TextInputStyle.Paragraph, minLength: 10, maxLength: 1000)]
        public string Concern { get; set; } = "";
    }
    [ModalInteraction("recruit:concern:*", true)]
    public async Task SubmitConcern(string thread, ConcernModal modal)
    {
        await DeferAsync(ephemeral: true);
        if (thread != Thread) { await FollowupAsync("This control belongs to another thread.", ephemeral: true); return; }
        await Reply(() => Recruitment.RunAsync((engine, ct) => engine.ReportAsync(Guild, thread, Actor, modal.Concern, ct)));
    }

    [ComponentInteraction("recruit:ack:*", true)]
    public async Task Acknowledge(string thread)
    {
        if (thread != Thread || Context.Guild is null) { await RespondAsync("This control belongs to another thread.", ephemeral: true); return; }
        // The modal itself grants no authority; submission checks persisted ownership and eligibility.
        await RespondWithModalAsync<GuidelinesModal>($"recruit:code:{thread}");
    }
    public sealed class GuidelinesModal : IModal
    {
        public string Title => "Acknowledge the Guidelines";
        [InputLabel("Code from this forum's native Guidelines")]
        [ModalTextInput("code", TextInputStyle.Short, minLength: 5, maxLength: 5)]
        public string Code { get; set; } = "";
    }
    [ModalInteraction("recruit:code:*", true)]
    public async Task SubmitCode(string thread, GuidelinesModal modal)
    {
        await DeferAsync(ephemeral: true);
        if (thread != Thread) { await FollowupAsync("This control belongs to another thread.", ephemeral: true); return; }
        await Reply(() => Recruitment.RunAsync((engine, ct) => engine.AcknowledgeAsync(Guild, thread, Actor, modal.Code, ct, Context.Interaction.CreatedAt), thread));
    }
    [ComponentInteraction("recruit:close:*", true)]
    public async Task Close(string thread)
    {
        await DeferAsync(ephemeral: true);
        if (thread != Thread) { await FollowupAsync("This control belongs to another thread.", ephemeral: true); return; }
        await Reply(() => Recruitment.RunAsync((engine, ct) => engine.CloseOwnerAsync(Guild, thread, Actor, ct), thread));
    }
    [ComponentInteraction("recruit:remove:*", true)]
    public async Task Remove(string thread)
    {
        await DeferAsync(ephemeral: true);
        if (thread != Thread) { await FollowupAsync("This control belongs to another thread.", ephemeral: true); return; }
        try
        {
            var result = await Recruitment.RunAsync((engine, _) => Task.FromResult(engine.PrepareRemoval(Guild, thread, Actor)), thread);
            var controls = result.Token is null ? null : new ComponentBuilder().WithButton("Confirm removal",
                $"recruit:confirm:{thread}:{result.Token}", ButtonStyle.Danger).Build();
            await FollowupAsync(result.Message, ephemeral: true, components: controls, allowedMentions: AllowedMentions.None);
        }
        catch { await FollowupAsync("Recruitment could not complete this request. Try again or ask a moderator to check status.", ephemeral: true); }
    }
    [ComponentInteraction("recruit:confirm:*:*", true)]
    public async Task Confirm(string thread, string token)
    {
        await DeferAsync(ephemeral: true);
        if (thread != Thread) { await FollowupAsync("This control belongs to another thread.", ephemeral: true); return; }
        await Reply(() => Recruitment.RunAsync((engine, ct) => engine.ConfirmRemovalAsync(Guild, thread, Actor, token, ct), thread));
    }
    private async Task Reply(Func<Task<string>> action)
    {
        try
        {
            var text = await action();
            for (var offset = 0; offset < text.Length; offset += 1900)
                await FollowupAsync(text.Substring(offset, Math.Min(1900, text.Length - offset)), ephemeral: true, allowedMentions: AllowedMentions.None);
        }
        catch { await FollowupAsync("Recruitment could not complete this request. Try again or ask a moderator to check status.", ephemeral: true); }
    }
}
