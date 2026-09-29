using Discord.Interactions;
using DiscordBot.Services.Recruitment;

namespace DiscordBot.Modules.Recruitment;

[Group("recruitment", "Recruitment moderation and recovery")]
public sealed class RecruitmentModule : InteractionModuleBase<SocketInteractionContext>
{
    public RecruitmentService Recruitment { get; set; } = null!;
    private string Guild => Context.Guild?.Id.ToString() ?? "";
    private string Actor => Context.User.Id.ToString();
    private string Thread => Context.Channel.Id.ToString();

    [SlashCommand("status", "Show recruitment availability")]
    public Task Status() => Staff("status");
    [SlashCommand("lookup", "Look up a managed thread or author ID")]
    public Task Lookup(string id) => Staff("lookup", id);
    [SlashCommand("preview", "Preview managed forum setup")]
    public Task Preview([Choice("Paid recruiting", "paid-recruiting"), Choice("Paid for hire", "paid-for-hire"),
        Choice("Hobby recruiting", "hobby-recruiting"), Choice("Hobby for hire", "hobby-for-hire")] string forum = "") => Staff("preview", forum);
    [SlashCommand("repair", "Publish and verify managed Guidelines and tags")]
    public Task Repair(string reason) => Staff("repair", reason: reason);
    [SlashCommand("reconcile", "Reconcile Discord state and pending actions")]
    public Task Reconcile(string reason) => Staff("reconcile", reason: reason);
    [SlashCommand("manage", "Change a listing or review uncertain evidence")]
    public Task Manage(
        [Choice("Exempt", "exempt"), Choice("Remove exemption", "unexempt"), Choice("Reset acknowledgement/timeouts", "reset"), Choice("Reset timeout streak", "reset-timeouts"),
         Choice("Close", "close"), Choice("Reopen", "reopen"), Choice("Designate reference post", "reference"),
         Choice("Confirm no qualifying reply", "review-no-reply"), Choice("Confirm qualifying reply", "review-reply"),
         Choice("Confirm unavailable thread is absent", "review-absent")] string operation,
        string threadId, string reason) => Staff(operation, threadId, reason);
    [SlashCommand("waive-cooldown", "Waive an author's creation cooldown without granting a second slot")]
    public Task Waive(IUser author, string reason) => Staff("waive-cooldown", author.Id.ToString(), reason);

    private async Task Staff(string operation, string target = "", string reason = "")
    {
        await DeferAsync(ephemeral: true);
        await Reply(() => Recruitment.RunAsync((engine, ct) => engine.StaffAsync(Guild, Actor, operation, target, reason, ct)));
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
