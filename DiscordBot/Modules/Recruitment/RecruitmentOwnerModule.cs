using Discord.Interactions;
using DiscordBot.Modules.Base;
using DiscordBot.Services;
using DiscordBot.Services.Recruitment;

namespace DiscordBot.Modules.Recruitment;

public sealed class RecruitmentCodeModal : IModal
{
    public string Title => "Acknowledge forum guidelines";
    [InputLabel("Code from this forum's Guidelines")]
    [ModalTextInput("code", TextInputStyle.Short, minLength: 1, maxLength: 32)]
    public string Code { get; set; } = "";
}

public sealed class RecruitmentOwnerModule(RecruitService service, RecruitmentOwnerActions owners,
    RecruitmentAdvisoryCoordinator advisory) : BotInteractionModuleBase
{
    [ComponentInteraction("udc-recruit:ack:*:*")]
    public async Task OpenCode(ulong threadId, string generation)
    {
        try
        {
            if (!service.IsAdvisoryRunning) throw new InvalidOperationException("Advisory is stopped or unavailable.");
            // A modal must be the initial response. Do only local identity checks here; submission checks Discord again.
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            await service.ExecuteAdvisoryAsync(async token =>
            {
                await owners.ValidateControlAsync(OwnerContext(threadId), generation, token);
                return true;
            }, deadline.Token);
            await RespondWithModalAsync<RecruitmentCodeModal>($"udc-recruit:code:{threadId}:{generation}");
        }
        catch (Exception error)
        {
            if (Context.Interaction.HasResponded) await FollowupAsync(Failure(error), ephemeral: true);
            else await RespondAsync(Failure(error), ephemeral: true);
        }
    }

    [ModalInteraction("udc-recruit:code:*:*")]
    public Task SubmitCode(ulong threadId, string generation, RecruitmentCodeModal modal) => RunAsync(async token =>
    {
        string result = await owners.SubmitCodeAsync(OwnerContext(threadId), generation, modal.Code, token);
        await advisory.TryRefreshPostAsync(threadId, token);
        return (result, (MessageComponent?)null);
    });

    [ComponentInteraction("udc-recruit:renew:*:*")]
    public Task Renew(ulong threadId, string generation) => RunAsync(async token =>
    {
        await owners.RenewAsync(OwnerContext(threadId), generation, token);
        await advisory.TryRefreshPostAsync(threadId, token);
        return ("A fresh practice window was requested. Check the live public message for its deadline.", (MessageComponent?)null);
    });

    [ComponentInteraction("udc-recruit:close:*:*")]
    public Task Close(ulong threadId, string generation) => PrepareAsync(threadId, generation, RecruitmentActionKind.LockArchive);

    [ComponentInteraction("udc-recruit:remove:*:*")]
    public Task Remove(ulong threadId, string generation) => PrepareAsync(threadId, generation, RecruitmentActionKind.Delete);

    private Task PrepareAsync(ulong threadId, string generation, RecruitmentActionKind action) => RunAsync(async token =>
    {
        var confirmation = await owners.PrepareAsync(OwnerContext(threadId), generation, action, token);
        string explanation = action == RecruitmentActionKind.Delete
            ? "Permanently delete this post and all its replies? This cannot be undone. Existing accepted-listing history is retained."
            : "Lock and archive this listing? Members can still find it in older posts and search. This does not remove it.";
        var controls = new ComponentBuilder().WithButton("Confirm", $"udc-recruit:confirm:{threadId}:{confirmation.Token}",
            action == RecruitmentActionKind.Delete ? ButtonStyle.Danger : ButtonStyle.Primary).Build();
        return (explanation + " Confirmation expires in two minutes.", (MessageComponent?)controls);
    });

    [ComponentInteraction("udc-recruit:confirm:*:*")]
    public Task Confirm(ulong threadId, string confirmationToken) => RunAsync(async token =>
    {
        await owners.ConfirmAsync(OwnerContext(threadId), confirmationToken, token);
        await advisory.TryRefreshPostAsync(threadId, token);
        return ("Your owner action is complete.", (MessageComponent?)null);
    });

    private RecruitmentOwnerContext OwnerContext(ulong threadId)
    {
        if (Context.Guild is null || Context.Channel.Id != threadId)
            throw new InvalidOperationException("Use these controls in the original recruitment thread.");
        return new(Context.Guild.Id, threadId, Context.User.Id);
    }

    private async Task RunAsync(Func<CancellationToken, Task<(string Text, MessageComponent? Controls)>> action)
    {
        await DeferAsync(ephemeral: true);
        try
        {
            var result = await service.ExecuteAdvisoryAsync(action);
            await FollowupAsync(result.Text, components: result.Controls, ephemeral: true, allowedMentions: AllowedMentions.None);
        }
        catch (Exception error) { await FollowupAsync(Failure(error), ephemeral: true, allowedMentions: AllowedMentions.None); }
    }

    internal static string Failure(Exception error) => error is InvalidOperationException ? error.Message :
        "Recruitment is temporarily unavailable. Check the public status before trying again; staff can inspect component health.";
}
