using DiscordBot.Services.Recruitment.Infrastructure.Discord;
namespace DiscordBot.Services.Recruitment;

public sealed partial class RecruitmentEngine
{
    private bool _feedReady;
    private long _feedGeneration = -1;
    private bool _feedEmbeds;
    private string? _overviewRender;
    private DateTimeOffset? _overviewDirtyAt;

    private void GuardFeedWrite()
    {
        if (!_feedReady || !_gatewayReady() || _feedGeneration != _evidenceGeneration())
            throw new InvalidOperationException("Feed access changed; recruitment recovery is required before publication.");
    }

    public DateTimeOffset? NextDueAt => new[] { NextWorkAt, OverviewDueAt }.Where(t => t.HasValue).Min();
    private DateTimeOffset? OverviewDueAt
    {
        get
        {
            if (!_feedReady || !_gatewayReady() || _feedGeneration != _evidenceGeneration() || State is null) return null;
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

    public async Task RefreshOverviewAsync(CancellationToken ct)
    {
        if (!_feedReady || !_gatewayReady() || _feedGeneration != _evidenceGeneration() || State is null) return;
        var receipt = State.Overview;
        var health = Health();
        var render = health.Text();
        if (render != _overviewRender) _overviewDirtyAt ??= Now;
        if (!(OverviewDueAt <= Now)) return;
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
            var message = new RecruitmentMessage(_feedEmbeds ? $"-# {marker}" : "**Recruitment Status**\n" + render[..Math.Min(1800, render.Length)] + $"\n-# {marker}",
                marker, false, Embed: _feedEmbeds ? health.Embed(includeFreshness: false) : null);
            var delivery = await _discord.UpsertAsync(_settings.FeedChannelId.ToString(), replace ? null : receipt.MessageId,
                receipt.SendPending, receipt.SendStartedAt ?? Now, message, () => { GuardFeedWrite(); receipt.SendPending = true; Save(); }, ct);
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
