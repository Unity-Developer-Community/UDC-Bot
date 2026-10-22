using DiscordBot.Settings;
using DiscordBot.Services.Recruitment.Infrastructure.Discord;
using DiscordBot.Services.Recruitment.Presentation;

namespace DiscordBot.Services.Recruitment.Application;

internal sealed class RecruitmentOverviewPublisher(
    RecruitmentState state,
    RecruitmentSettings settings,
    IRecruitmentDiscord discord,
    TimeProvider time,
    Action save,
    Func<bool> feedAvailable,
    Func<bool> feedEmbeds,
    Action guardFeedWrite,
    Func<RecruitmentHealth> healthSnapshot)
{
    private readonly RecruitmentState State = state;
    private readonly RecruitmentSettings _settings = settings;
    private readonly IRecruitmentDiscord _discord = discord;
    private DateTimeOffset Now => time.GetUtcNow();
    private void Save() => save();
    public void InvalidateRender() => _overviewRender = null;
    private string? _overviewRender;
    private DateTimeOffset? _overviewDirtyAt;

    public DateTimeOffset? NextDueAt
    {
        get
        {
            if (!feedAvailable()) return null;
            var receipt = State.Overview;
            if (receipt.Fault is not null) return receipt.Fault.RetryAt;
            if (receipt.SendPending || receipt.RetiredMessageId is not null || receipt.MessageId is null) return Now;
            var times = new List<DateTimeOffset>();
            if (_overviewDirtyAt is { } dirty)
                times.Add(Max(dirty.AddSeconds(5), (receipt.LastEditedAt ?? DateTimeOffset.MinValue).AddSeconds(30)));
            return times.Count == 0 ? null : times.Min();
        }
    }
    private static DateTimeOffset Max(DateTimeOffset a, DateTimeOffset b) => a > b ? a : b;

    public async Task RefreshAsync(CancellationToken ct)
    {
        if (!feedAvailable()) return;
        var receipt = State.Overview;
        var health = healthSnapshot();
        var render = health.Text();
        if (render != _overviewRender) _overviewDirtyAt ??= Now;
        if (!(NextDueAt <= Now)) return;
        if (receipt.Fault is not null && !receipt.Fault.IsDue(Now)) return;
        try
        {
            if (receipt.RetiredMessageId is not null)
            {
                await _discord.DeleteAdvisoryAsync(_settings.FeedChannelId.ToString(), receipt.RetiredMessageId, ct, receipt.RetiredMarker);
                receipt.RetiredMessageId = receipt.RetiredMarker = null;
                Save();
            }
            var replace = receipt.SendPending ? receipt.PendingReplacement : receipt.MessageId is null;
            if (!receipt.SendPending)
            {
                receipt.PendingReplacement = replace;
                receipt.PendingMarker = replace ? $"recruitment:overview:{State.InstanceId}:{++receipt.Sequence}" : receipt.Marker;
                receipt.SendStartedAt = Now;
            }
            var marker = receipt.PendingMarker ?? throw new InvalidOperationException("Overview delivery marker is missing; reconcile required.");
            var message = new RecruitmentMessage(feedEmbeds() ? $"-# {marker}" : "**Recruitment Status**\n" + render[..Math.Min(1800, render.Length)] + $"\n-# {marker}",
                marker, false, Embed: feedEmbeds() ? health.Embed(includeFreshness: false) : null);
            var delivery = await _discord.UpsertAsync(_settings.FeedChannelId.ToString(), replace ? null : receipt.MessageId,
                receipt.SendPending, receipt.SendStartedAt ?? Now, message, () => { guardFeedWrite(); receipt.SendPending = true; Save(); }, ct);
            if (replace && receipt.MessageId is not null && receipt.MessageId != delivery.Id)
            { receipt.RetiredMessageId = receipt.MessageId; receipt.RetiredMarker = receipt.Marker; }
            receipt.MessageId = delivery.Id; receipt.Marker = marker; receipt.SendPending = false;
            receipt.PendingMarker = null; receipt.PendingReplacement = false; receipt.Fault = null;
            receipt.LastEditedAt = Now;
            if (replace || delivery.NewlySent)
            { receipt.LastPostedAt = Now; }
            _overviewRender = render; _overviewDirtyAt = null;
            Save();
            if (receipt.RetiredMessageId is not null)
            {
                await _discord.DeleteAdvisoryAsync(_settings.FeedChannelId.ToString(), receipt.RetiredMessageId, ct, receipt.RetiredMarker);
                receipt.RetiredMessageId = receipt.RetiredMarker = null; Save();
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException && ex is not System.IO.IOException)
        {
            receipt.Fault = RecruitmentFaultClassifier.From(ex, Now, receipt.Fault);
            Save(); // Cosmetic failures never change listing eligibility or evidence.
        }
    }
}
