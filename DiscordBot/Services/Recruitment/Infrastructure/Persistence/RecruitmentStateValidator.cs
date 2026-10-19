using System.IO;

namespace DiscordBot.Services.Recruitment.Infrastructure.Persistence;

/// <summary>Synchronous validation at every existing load and save boundary.</summary>
public static class RecruitmentStateValidator
{
    private static bool Id(string? id) => ulong.TryParse(id, out var value) && value != 0;

    public static void Validate(RecruitmentState state, string guildId, IReadOnlyDictionary<string, string> forums)
    {
        ValidateIdentity(state, guildId, forums);
        ValidateRegistry(state.PortfolioSites);
        ValidateOverview(state);
        foreach (var (purpose, receipt) in state.Forums) ValidateForum(purpose, receipt);
        foreach (var (id, post) in state.Listings) ValidateListing(id, post, state, forums);
        foreach (var (id, author) in state.Authors) ValidateAuthor(id, author);
    }

    private static void ValidateIdentity(RecruitmentState state, string guildId, IReadOnlyDictionary<string, string> forums)
    {
        if (state.Schema != RecruitmentState.Format || !Guid.TryParseExact(state.InstanceId, "N", out _) ||
            state.GuildId != guildId || !Id(guildId) || state.InitializedAt == default || state.DiscoveryFloor == default ||
            state.Forums is null || state.Listings is null || state.Authors is null || state.Overview is null ||
            state.Forums.Count != 4 || forums.Count != 4 || forums.Values.Distinct().Count() != 4 ||
            forums.Any(f => !Id(f.Value) || !state.Forums.TryGetValue(f.Key, out var saved) || saved is null || saved.ForumId != f.Value))
            throw new InvalidDataException("Recruitment state schema, identity or forum mapping is invalid.");
    }

    private static void ValidateRegistry(PortfolioRegistry registry)
    {
        if (registry is null || registry.Version < 1 || registry.Hosts is null || registry.Hosts.Count > 100 ||
            registry.Hosts.Distinct(StringComparer.Ordinal).Count() != registry.Hosts.Count ||
            registry.Hosts.Any(h => !RecruitmentPortfolioDomains.TryNormalize(h, out var normalized) || h != normalized) ||
            registry.Audit is null || registry.Audit.Count > 100 || registry.Audit.Any(a => a is null || a.At == default ||
                !Id(a.Actor) || string.IsNullOrWhiteSpace(a.Reason) || a.Reason.Length > 570))
            throw new InvalidDataException("Recruitment portfolio-site registry is invalid.");
    }

    private static void ValidateOverview(RecruitmentState state)
    {
        var overview = state.Overview;
        if (overview.Sequence < 0 ||
            overview.MessageId is not null && (!Id(overview.MessageId) || overview.Marker is null || !overview.Marker.StartsWith($"recruitment:overview:{state.InstanceId}:")) ||
            overview.RetiredMessageId is not null && (!Id(overview.RetiredMessageId) || overview.RetiredMessageId == overview.MessageId || overview.RetiredMarker is null || !overview.RetiredMarker.StartsWith($"recruitment:overview:{state.InstanceId}:")) ||
            overview.SendPending && (overview.SendStartedAt is null || overview.PendingMarker is null || !overview.PendingMarker.StartsWith($"recruitment:overview:{state.InstanceId}:")))
            throw new InvalidDataException("Recruitment overview receipt is invalid.");
    }

    private static void ValidateForum(string purpose, ForumReceipt f)
    {
        if (f is null || f.Tags is null || f.ConfirmedTopic is null || f.VisibleCode is null || f.Reference is null ||
            f.Reference.ThreadId is not null && !Id(f.Reference.ThreadId) ||
            f.Reference.CreatePending && (f.Reference.CreateStartedAt is null || f.Reference.ThreadId is not null) ||
            f.Tags.Values.Any(t => t is null || !Id(t.Id) || string.IsNullOrWhiteSpace(t.Name)))
            throw new InvalidDataException($"Recruitment Forums[{purpose}] receipt is invalid.");
    }

    private static void ValidateListing(string id, Listing p, RecruitmentState state, IReadOnlyDictionary<string, string> forums)
    {
        if (p is null || id != p.Id || !Id(id) || !Id(p.OwnerId) || !forums.Values.Contains(p.ForumId))
            throw new InvalidDataException($"Recruitment Listings[{id}] identity is invalid.");
        if (p.PortfolioRegistryVersion < 0 || p.PortfolioRegistryVersion > state.PortfolioSites.Version ||
                p.CreatedAt == default || p.CreatedAt.Offset != TimeSpan.Zero || p.UpdatedAt == default ||
                !Enum.IsDefined(p.Status) || p.ValidCodes is null || p.Facts is null || p.Audit is null ||
                !state.Authors.ContainsKey(p.OwnerId) || p.Facts.Payment is null || p.Facts.Portfolio is null)
            throw new InvalidDataException($"Recruitment Listings[{id}] lifecycle is invalid.");
        if ((p.AcceptedAt.HasValue && !p.AcknowledgedAt.HasValue) ||
                (p.Deadline.HasValue && !p.PromptDeliveredAt.HasValue) ||
                (p.PendingAction is not null && !Enum.IsDefined(p.PendingAction.Kind)))
            throw new InvalidDataException($"Recruitment Listings[{id}] action is invalid.");
        ValidateDelivery(id, p);
        ValidateEvidence(id, p);
    }

    private static void ValidateDelivery(string id, Listing p)
    {
        if (p.PublicMediaAttachmentId is not null && !Id(p.PublicMediaAttachmentId) ||
                (p.PublicMediaAttachmentId is null) != (p.PublicMediaFileName is null) ||
                p.PublicMediaFileName is { } fileName && (fileName.Length > 120 || !fileName.StartsWith("recruitment-observations-", StringComparison.Ordinal) || !fileName.EndsWith(".png", StringComparison.Ordinal)) ||
                p.PublicRestoreLocked && !p.PublicRestoreArchive ||
                p.AcknowledgementNotice is null || p.AcknowledgementNotice.MessageId is not null && !Id(p.AcknowledgementNotice.MessageId) ||
                p.AcknowledgementNotice.SendPending && p.AcknowledgementNotice.StartedAt is null ||
                p.PublicPin is null || p.PublicPin.MessageId is not null && !Id(p.PublicPin.MessageId) ||
                p.PublicPin.Confirmed && p.PublicPin.MessageId is null ||
                p.PublicPin.RestoreLocked && !p.PublicPin.RestoreArchive)
            throw new InvalidDataException($"Recruitment Listings[{id}].Delivery is invalid.");
    }

    private static void ValidateEvidence(string id, Listing p)
    {
        if (!FactsValid(p.Facts) || p.Facts.Observations is null || p.Facts.Observations.Count > 4 ||
                p.Facts.Observations.Any(o => o is null || o.Code is null || o.Description is null || o.Evidence is null || o.Evidence.Length > 180) ||
                p.Facts.PaymentEvidence is null || p.Facts.PaymentEvidence.Length > 370 ||
                p.EarlierMaterialVersions is null || p.EarlierMaterialVersions.Count > 3 ||
                p.MaterialChanges < 0 || p.ReviewedMaterialChanges < 0 || p.ReviewedMaterialChanges > p.MaterialChanges ||
                !VersionValid(p.OriginalStarter) || !VersionValid(p.LatestStarter) || p.EarlierMaterialVersions.Any(v => v is null || !VersionValid(v)) ||
                p.Reports is null || p.Reports.Count > 20 || p.Reports.Any(r => r is null || !Id(r.ReporterId) || r.At == default || r.Concern is null || r.Concern.Length > 1000) ||
                p.Attention is null || p.Attention.Count > 8 || p.Attention.Any(n => n is null || !Guid.TryParseExact(n.Id, "N", out _) || n.At == default || n.Reason is null || n.Reason.Length > 300 || n.MessageId is not null && !Id(n.MessageId)))
            throw new InvalidDataException($"Recruitment Listings[{id}].Evidence is invalid.");
    }

    private static void ValidateAuthor(string id, AuthorHistory a)
    {
        if (!Id(id) || a is null || a.ConsecutiveTimeouts < 0 || a.UpdatedAt == default ||
            a.RecentCreations is null || a.RecentCreations.Count > 21 || a.RecentCreations.Any(d => d == default) || a.RecentReports is null || a.RecentReports.Count > 3 || a.RecentReports.Any(d => d == default))
            throw new InvalidDataException($"Recruitment Authors[{id}] history is invalid.");
    }

    private static bool FactsValid(InitialFacts f) =>
        f.ServerNickname is null or { Length: <= 100 } && f.GlobalName is null or { Length: <= 100 } &&
        f.Username is null or { Length: <= 100 } && f.AvatarIdentity is null or { Length: <= 200 } &&
        (f.AvatarUrl is null || RecruitmentSignals.SafeAvatarUrl(f.AvatarUrl) == f.AvatarUrl) &&
        f.Checks is { Count: <= 9 } && f.Checks.Select(c => c?.Id).Distinct().Count() == f.Checks.Count &&
        f.Checks.All(c => c is not null && c.Id is { Length: <= 30 } && c.Label is { Length: <= 30 } && c.Value is { Length: <= 100 } &&
            c.Evidence is null or { Length: <= 370 } && Enum.IsDefined(c.Severity) && Enum.IsDefined(c.Applicability)) &&
        f.PortfolioCandidates is { Candidates.Count: <= 8 } && f.PortfolioCandidates.Candidates.All(c =>
            c is not null && c.Url is { Length: <= 300 } && c.Host is { Length: <= 253 } && c.ReuseUrl is null or { Length: <= 300 });

    private static bool VersionValid(StarterVersion? version) => version is null ||
        version.ObservedAt != default && version.Content is { Length: <= 2000 } && version.Fingerprint is { Length: 64 } &&
        (version.TermsFingerprint is null || version.TermsFingerprint.Length == 64) &&
        version.Payment is not null && version.PaymentEvidence is { Length: <= 370 } && version.Links is { Count: <= 8 } &&
        version.Links.All(l => l is { Length: <= 300 }) && version.Attachments is { Count: <= 10 } &&
        version.Attachments.All(a => a is not null && a.Name is { Length: <= 120 } && (a.ContentType is null || a.ContentType.Length <= 80));

}
