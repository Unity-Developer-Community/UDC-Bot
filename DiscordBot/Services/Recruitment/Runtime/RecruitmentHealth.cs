using DiscordBot.Utils;
namespace DiscordBot.Services.Recruitment;

public sealed record ForumHealth(string Purpose, string Id, string Setup, string? Fault, DateTimeOffset? RetryAt, string? ReferenceId = null, string? ReferenceFault = null);

/// <summary>Detached from mutable state so moderators can inspect progress while the worker awaits Discord.</summary>
public sealed record RecruitmentHealth(string Status, string Work, DateTimeOffset ObservedAt,
    int Open, int Pending, int Held, bool InventoryComplete, DateTimeOffset? LastRecovery,
    DateTimeOffset? NextDue, IReadOnlyList<ForumHealth> Forums, string? OverviewFault = null, string? Job = null, bool RoutinePendingIdentity = false, string? Attention = null, string? Metrics = null)
{
    public static RecruitmentHealth Starting(DateTimeOffset now) => new("Starting", "Opening recruitment state", now,
        0, 0, 0, false, null, null, Array.Empty<ForumHealth>());

    public Embed Embed(bool includeFreshness = true)
    {
        var embed = new EmbedBuilder().WithTitle("Recruitment Status").WithColor(InventoryComplete && Status == "Operating" ? Color.Green : Color.Orange)
            .WithDescription(Text());
        if (includeFreshness) embed.WithFooter($"Snapshot captured {ObservedAt:u}");
        return embed.Build();
    }

    public string Text()
    {
        var forums = string.Join("\n", Forums.Select(f => $"• <#{f.Id}> · {f.Purpose}: {f.Setup}" +
            (f.Fault is null ? "" : $" — {DiscordDisplayText.Literal(f.Fault, 250)}" + (f.RetryAt.HasValue ? $"; retry {DiscordDisplayText.Timestamp(f.RetryAt.Value)}" : "; preview and repair required")) +
            (f.ReferenceId is null ? " · reference pending" : $" · reference <#{f.ReferenceId}>") +
            (f.ReferenceFault is null ? "" : " · reference held: " + DiscordDisplayText.Literal(f.ReferenceFault, 180))));
        return DiscordDisplayText.Literal(Status, 400) + $"\nOpen: {Open} · awaiting acknowledgement: {Pending} · held: {Held}\n" +
            $"Inventory: {(InventoryComplete ? "complete" : "recovery needed")} · work: {Work}\n" +
            (Attention is null ? "" : "**Needs review**\n" + Attention + "\n") +
            (Metrics is null ? "" : Metrics + "\n") +
            (LastRecovery.HasValue ? $"Last recovery: {DiscordDisplayText.Timestamp(LastRecovery.Value)}\n" : "No completed recovery yet.\n") +
            (NextDue.HasValue ? $"Next scheduled work: {DiscordDisplayText.Timestamp(NextDue.Value)}\n" : "") + forums +
            (OverviewFault is null ? "" : "\nOverview: " + OverviewFault) +
            (Job is null ? "" : "\nJob: " + Job) +
            "\nUse /recruitment status, lookup, preview, repair or reconcile. Repairs and changes require a reason.";
    }
}
