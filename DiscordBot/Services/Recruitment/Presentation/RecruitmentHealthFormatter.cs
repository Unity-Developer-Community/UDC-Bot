using DiscordBot.Utils;

namespace DiscordBot.Services.Recruitment.Presentation;

public static class RecruitmentHealthFormatter
{
    public static Embed Embed(this RecruitmentHealth health,bool includeFreshness = true)
    {
        var embed = new EmbedBuilder().WithTitle("Recruitment Status").WithColor(health.InventoryComplete && health.Kind == RecruitmentHealthKind.Operating ? Color.Green : Color.Orange)
            .WithDescription(health.Text());
        if (includeFreshness) embed.WithFooter($"Snapshot captured {health.ObservedAt:u}");
        return embed.Build();
    }

    public static string Text(this RecruitmentHealth health)
    {
        var Status = health.Status;
        var Open = health.Open;
        var Pending = health.Pending;
        var Held = health.Held;
        var InventoryComplete = health.InventoryComplete;
        var Work = health.Work;
        var Attention = health.Attention;
        var Metrics = health.Metrics;
        var LastRecovery = health.LastRecovery;
        var NextDue = health.NextDue;
        var Forums = health.Forums;
        var OverviewFault = health.OverviewFault;
        var Job = health.Job;
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
