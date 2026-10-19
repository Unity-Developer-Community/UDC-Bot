using DiscordBot.Settings;
using DiscordBot.Utils;
using DiscordBot.Services.Recruitment.Domain.Policies;
using DiscordBot.Services.Recruitment.Presentation;

namespace DiscordBot.Services.Recruitment.Application;

internal sealed class ListingOwnerCommands(RecruitmentState state, RecruitmentSettings settings, IRecruitmentDiscord discord, TimeProvider time, Action save, ListingWorkflow workflow, ListingActionExecutor actions, Func<bool> ready, Func<bool> acknowledgementReady, Func<bool> evidenceCurrent)
{
    private readonly RecruitmentState State = state;
    private readonly RecruitmentSettings _settings = settings;
    private readonly IRecruitmentDiscord _discord = discord;
    private DateTimeOffset Now => time.GetUtcNow();
    private readonly ListingWorkflow _workflow = workflow;
    private readonly ListingActionExecutor _actions = actions;
    private void Save() => save();
    private readonly Dictionary<string, RemovalConfirmation> _confirmations = new();
    private sealed record RemovalConfirmation(string Guild, string Thread, string Owner, long Version, DateTimeOffset Expires);

    public async Task<string> AcknowledgeAsync(string guild, string thread, string owner, string code, CancellationToken ct, DateTimeOffset? receivedAt = null)
    {
        var post = Owner(guild, thread, owner);
        if (post is null) return "This control is unavailable or does not belong to you.";
        // Startup/recovery establishes cross-forum identity coverage; ordinary acknowledgements only
        // refresh the candidate and its blocking author records.
        if (!acknowledgementReady()) return "Recruitment is waiting for recovery. Please try again shortly.";
        if (post.Status != ListingStatus.Open || post.AcceptedAt.HasValue) return "This listing no longer needs acknowledgement.";
        var submittedAt = receivedAt ?? Now;
        if (!post.Deadline.HasValue || post.Deadline < submittedAt || post.PromptDeliveredAt > submittedAt)
            return "The acknowledgement window is unavailable or has expired.";
        if (post.LastCodeAttemptAt?.AddSeconds(5) > Now) return "Please wait a few seconds before trying again.";
        post.LastCodeAttemptAt = Now; Save();
        if (!post.ValidCodes.Contains(code.Trim().ToUpperInvariant())) return "That code does not match. Read the native Guidelines for this forum and try again.";
        // Persist a timely valid submission before any processing can execute an overdue timeout.
        // Invalid/late submissions never extend the window or fence the deadline worker.
        post.AcknowledgedAt = submittedAt; post.Version++; post.UpdatedAt = Now;
        var author = ListingTransitions.Author(State!, owner, Now);
        author.ConsecutiveTimeouts = 0; author.TimeoutAlerted = false;
        Save();
        _discord.InvalidateThread(post.Id);
        post.Fault = null;
        await _workflow!.ProcessAsync(post, ct);
        if (post.PendingAction is null && post.Status == ListingStatus.Open && !post.AcceptedAt.HasValue)
            await _workflow!.ProcessAsync(post, ct); // A recovered timeout intent may have just been cancelled.
        if (post.Status != ListingStatus.Open) return "This listing is no longer open. Use your listing controls to check its status.";
        return post.AcceptedAt.HasValue ? "Guidelines acknowledged. Your listing is accepted." : !evidenceCurrent()
            ? "Guidelines acknowledged. Acceptance is waiting for inventory recovery."
            : "Guidelines acknowledged. The existing listing or creation cooldown still blocks acceptance; see the advisory.";
    }

    public async Task<string> CloseOwnerAsync(string guild, string thread, string owner, CancellationToken ct)
    {
        var post = Owner(guild, thread, owner);
        if (post is null || post.Status != ListingStatus.Open) return "This open listing does not belong to you or recruitment is unavailable.";
        await _actions!.BeginActionAsync(post, RecruitmentActionKind.Close, owner, "Owner closed listing", "", false, ct);
        return "Listing closed. Its accepted creation cooldown is unchanged.";
    }

    public (string Message, string? Token) PrepareRemoval(string guild, string thread, string owner)
    {
        var post = Owner(guild, thread, owner);
        if (post is null || post.Status is ListingStatus.Deleted or ListingStatus.Reference) return ("This listing is unavailable or does not belong to you.", null);
        foreach (var key in _confirmations.Where(x => x.Value.Expires <= Now || x.Value.Owner == owner).Select(x => x.Key).ToArray()) _confirmations.Remove(key);
        var token = Guid.NewGuid().ToString("N");
        _confirmations[token] = new RemovalConfirmation(guild, thread, owner, post.Version, Now.AddMinutes(2));
        var next = State!.Authors.GetValueOrDefault(owner)?.LastAcceptedCreation?.AddDays(_settings.CooldownDays);
        var timing = !post.AcceptedAt.HasValue ? "Removing this unaccepted attempt adds no accepted-listing cooldown; its creation still counts toward the attempt limit." : next.HasValue
            ? "Its creation cooldown remains " + DiscordDisplayText.Timestamp(next.Value) + "." : "The accepted creation cooldown has been waived.";
        return ($"Permanently remove this forum post? {timing} This confirmation expires in two minutes.", token);
    }

    public async Task<string> ConfirmRemovalAsync(string guild, string thread, string owner, string token, CancellationToken ct)
    {
        if (!_confirmations.TryGetValue(token, out var confirm) || confirm.Guild != guild || confirm.Thread != thread || confirm.Owner != owner)
            return "This confirmation does not belong to this listing and owner.";
        _confirmations.Remove(token);
        var post = Owner(guild, thread, owner);
        if (post is null || confirm.Expires <= Now || confirm.Version != post.Version || post.Status is ListingStatus.Deleted or ListingStatus.Reference)
            return "This confirmation expired or the listing changed. Request removal again.";
        await _actions!.BeginActionAsync(post, RecruitmentActionKind.Delete, owner, "Owner confirmed removal", "", false, ct);
        return "Post removed. Any accepted creation cooldown is unchanged.";
    }

    public Listing? Owner(string guild, string thread, string owner) =>
        ready() && State?.GuildId == guild && State.Listings.TryGetValue(thread, out var post) && post.OwnerId == owner ? post : null;

}
