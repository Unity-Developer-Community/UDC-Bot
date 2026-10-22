using DiscordBot.Services.Recruitment.Domain.Policies;
using DiscordBot.Services.Recruitment.Infrastructure.Discord;
using System.IO;
using System.Security.Cryptography;
using DiscordBot.Settings;
using Newtonsoft.Json;

namespace DiscordBot.Services.Recruitment;

/// <summary>Publishes native Guidelines and managed tags, retaining intent across partial Discord writes.</summary>
public sealed class RecruitmentSetup
{
    public event Action? Progress;
    private readonly RecruitmentStateStore _store;
    private readonly RecruitmentAssets _assets;
    private readonly RecruitmentSettings _settings;
    private readonly IRecruitmentDiscord _discord;
    public RecruitmentSetup(RecruitmentStateStore store, RecruitmentAssets assets, RecruitmentSettings settings, IRecruitmentDiscord discord)
        => (_store, _assets, _settings, _discord) = (store, assets, settings, discord);

    public async Task ReconcileAsync(DateTimeOffset now, SetupRequest request, CancellationToken ct)
    {
        var (repair, retryHeld, target) = (request.Intent == SetupIntent.Repair, request.RetryHeld, request.Target);
        var state = _store.State;
        if (state.RotationCode.Length == 0 || (now >= state.RotateAt && state.Forums.Values.All(f => f.PendingTopic is null && f.VisibleCode == state.RotationCode)))
        {
            state.RotationCode = RecruitmentAssets.NewCode();
            // Monday 00:00 UTC, persisted once for all four publications.
            var date = now.UtcDateTime.Date;
            state.RotateAt = new DateTimeOffset(date.AddDays(7 - ((int)date.DayOfWeek + 6) % 7), TimeSpan.Zero);
            _store.Save();
        }
        foreach (var (purpose, receipt) in state.Forums)
        {
            if (target is not null && target != purpose) continue;
            if (!repair && !retryHeld && receipt.Fault is { } fault && !fault.IsDue(now)) continue;
            try
            {
                await ReconcileForumAsync(purpose, receipt, repair, ct);
                receipt.Fault = null;
            }
            catch (Exception ex) when (ex is not OperationCanceledException && ex is not IOException)
            {
                receipt.Fault = RecruitmentFaultClassifier.From(ex, now, receipt.Fault);
            }
            _store.Save();
            Progress?.Invoke();
        }
    }

    private async Task ReconcileForumAsync(string purpose, ForumReceipt receipt, bool repair, CancellationToken ct)
    {
        var state = _store.State;
        var live = await _discord.GetForumAsync(receipt.ForumId, ct);
        var plan = ForumSetupPlan.Prepare(purpose, receipt, live, repair,
            receipt.PendingTopic ?? _assets.Render(purpose, state.RotationCode, _settings), state.RotationCode, _assets.Tags[purpose]);
        var (topic, code, definitions, desired) = plan;
        if (live.Topic != topic || !live.RequireTag || !SameTags(live.Tags, desired))
        {
            PersistIntent(receipt, plan, repair);
            live = await PublishAsync(purpose, receipt, live, plan, ct);
        }
        ConfirmPublication(purpose, receipt, live, plan);
    }

    private void PersistIntent(ForumReceipt receipt, ForumSetupPlan plan, bool repair)
    {
        var (topic, code, definitions, _) = plan;
        receipt.PendingRepair |= repair;
        receipt.PendingTopic = topic;
        receipt.PendingCode = code;
        receipt.PendingTags = definitions;
        _store.Save();
    }

    private async Task<ForumSnapshot> PublishAsync(string purpose, ForumReceipt receipt, ForumSnapshot live, ForumSetupPlan plan, CancellationToken ct)
    {
        var (topic, _, _, desired) = plan;
        // Re-read immediately before writing to detect edits made during setup preparation.
        var beforeWrite = await _discord.GetForumAsync(receipt.ForumId, ct);
        if (beforeWrite.Topic != live.Topic || !SameTags(beforeWrite.Tags, live.Tags) || beforeWrite.Flags != live.Flags)
            throw new InvalidOperationException($"Concurrent forum edit in {purpose}; retry setup preview.");
        // Discord validates Require Tag against selectable tags. Establish them first, including
        // their assigned IDs, before enabling that flag on an initially empty forum.
        live = beforeWrite;
        if (live.Topic != topic || !SameTags(live.Tags, desired))
            live = await _discord.PublishForumAsync(live, live.Topic == topic ? null : topic,
                SameTags(live.Tags, desired) ? null : desired, live.RequireTag, ct);
        if (!MatchesPublication(live, topic, desired) || !live.Tags.Any(t => !t.Moderated && t.Id.Length > 0))
            throw new InvalidOperationException($"Tag/Guidelines verification failed in {purpose}; Require Tag was not enabled.");
        if (!live.RequireTag)
        {
            beforeWrite = await _discord.GetForumAsync(receipt.ForumId, ct);
            if (beforeWrite.Topic != live.Topic || !SameTags(beforeWrite.Tags, live.Tags) || beforeWrite.Flags != live.Flags)
                throw new InvalidOperationException($"Concurrent forum edit in {purpose}; retry setup preview.");
            live = await _discord.PublishForumAsync(beforeWrite, null, null, true, ct);
        }
        return live;
    }

    private void ConfirmPublication(string purpose, ForumReceipt receipt, ForumSnapshot live, ForumSetupPlan plan)
    {
        var state = _store.State;
        var (topic, code, definitions, desired) = plan;
        if (!live.RequireTag || !MatchesPublication(live, topic, desired))
            throw new InvalidOperationException($"Setup verification failed in {purpose}.");
        receipt.Tags = definitions.ToDictionary(d => d.Key, d =>
        {
            var tag = live.Tags.Single(t => t.Name == d.Name);
            return new ManagedTag { Id = tag.Id, Name = tag.Name, Moderated = tag.Moderated };
        });
        receipt.ConfirmedTopic = topic;
        receipt.VisibleCode = code;
        receipt.PendingTopic = receipt.PendingCode = null;
        receipt.PendingTags = null;
        receipt.PendingRepair = false;
        // Any code actually visible during a delivered challenge remains valid for that challenge.
        foreach (var post in state.Listings.Values.Where(p => p.ForumId == receipt.ForumId && p.Status == ListingStatus.Open && !p.AcceptedAt.HasValue))
            if (!post.ValidCodes.Contains(code)) post.ValidCodes.Add(code);
        _store.Save();
    }

    private static bool MatchesPublication(ForumSnapshot live, string topic, IReadOnlyList<LiveTag> desired) =>
        live.Topic == topic && desired.Count == live.Tags.Count &&
        desired.All(t => live.Tags.Any(v => v.Name == t.Name && v.Moderated == t.Moderated &&
            v.Emoji?.ToString() == t.Emoji?.ToString() && (t.Id == "" || t.Id == v.Id)));

    private static bool SameTags(IReadOnlyList<LiveTag> a, IReadOnlyList<LiveTag> b) =>
        a.OrderBy(t => t.Id).Select(t => (t.Id, t.Name, t.Moderated, Emoji: t.Emoji?.ToString()))
            .SequenceEqual(b.OrderBy(t => t.Id).Select(t => (t.Id, t.Name, t.Moderated, Emoji: t.Emoji?.ToString())));
}
