using System.IO;
using System.Text;
using Newtonsoft.Json;

namespace DiscordBot.Services.Recruitment;

/// <summary>Used only by the recruitment worker. A failed write permanently fences this instance.</summary>
public sealed class RecruitmentStateStore : IDisposable
{
    private readonly string _path;
    private readonly FileStream _lease;
    private bool _faulted;
    private string? _lastSnapshot;
    private static readonly JsonSerializerSettings Json = new()
    {
        DateParseHandling = DateParseHandling.DateTimeOffset,
        MissingMemberHandling = MissingMemberHandling.Error,
        Formatting = Formatting.Indented
    };
    public RecruitmentState State { get; }

    public RecruitmentStateStore(string root, string guildId, IReadOnlyDictionary<string, string> forums, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(root)) throw new InvalidDataException("Recruitment needs a persistent ServerRootPath.");
        var directory = Path.Combine(root, "recruitment");
        Directory.CreateDirectory(directory);
        _path = Path.Combine(directory, "recruitment-state.json");
        _lease = new FileStream(Path.Combine(directory, "writer.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        try
        {
            var marker = Path.Combine(directory, "initialized");
            if (File.Exists(_path))
            {
                if (!File.Exists(marker)) throw new InvalidDataException("State exists without its initialization receipt; operator recovery required.");
                State = Read(_path);
                Validate(State, guildId, forums);
                _lastSnapshot = JsonConvert.SerializeObject(State, Json);
                if (File.ReadAllText(marker) != State.InstanceId) throw new InvalidDataException("Recruitment state identity differs from its receipt.");
            }
            else
            {
                if (Directory.EnumerateFiles(directory).Any(file => Path.GetFileName(file) != "writer.lock"))
                    throw new InvalidDataException("Recruitment history is missing; refusing to reset cooldowns.");
                State = new RecruitmentState { GuildId = guildId, InitializedAt = now, DiscoveryFloor = now.AddMonths(-120),
                    Forums = forums.ToDictionary(x => x.Key, x => new ForumReceipt { ForumId = x.Value }) };
                Save();
                using var receipt = new FileStream(marker, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                receipt.Write(Encoding.UTF8.GetBytes(State.InstanceId));
                receipt.Flush(true);
            }
        }
        catch { _lease.Dispose(); throw; }
    }

    public void Save()
    {
        if (_faulted) throw new IOException("Recruitment storage is fenced after an earlier write failure.");
        try
        {
            Validate(State, State.GuildId, State.Forums.ToDictionary(x => x.Key, x => x.Value.ForumId));
            var snapshot = JsonConvert.SerializeObject(State, Json);
            if (snapshot == _lastSnapshot) return;
            if (Encoding.UTF8.GetByteCount(snapshot) > 32 * 1024 * 1024) throw new InvalidDataException("Recruitment state exceeds 32 MiB; operator review required.");
            WriteDurably(_path + ".tmp", snapshot);
            if (File.Exists(_path))
            {
                // Never replace a good backup with an externally damaged primary.
                var previous = Read(_path);
                Validate(previous, State.GuildId, State.Forums.ToDictionary(x => x.Key, x => x.Value.ForumId));
                if (previous.InstanceId != State.InstanceId) throw new InvalidDataException("State identity changed on disk.");
                WriteDurably(_path + ".bak.tmp", File.ReadAllText(_path));
                File.Move(_path + ".bak.tmp", _path + ".bak", true);
            }
            File.Move(_path + ".tmp", _path, true);
            _lastSnapshot = snapshot;
        }
        catch { _faulted = true; throw; }
    }

    private static void WriteDurably(string path, string text)
    {
        using var file = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
        file.Write(Encoding.UTF8.GetBytes(text));
        file.Flush(true);
    }

    private static RecruitmentState Read(string path)
    {
        if (new FileInfo(path).Length > 32 * 1024 * 1024) throw new InvalidDataException("Recruitment state exceeds 32 MiB; operator review required.");
        try
        {
            return JsonConvert.DeserializeObject<RecruitmentState>(File.ReadAllText(path), Json)
                ?? throw new InvalidDataException("Recruitment state is empty.");
        }
        catch (JsonException ex) { throw new InvalidDataException("Recruitment state is malformed or incomplete; operator recovery required.", ex); }
    }

    public static void Validate(RecruitmentState state, string guildId, IReadOnlyDictionary<string, string> forums)
    {
        static bool Id(string? id) => ulong.TryParse(id, out var value) && value != 0;
        if (state.Schema != RecruitmentState.Format || !Guid.TryParseExact(state.InstanceId, "N", out _) ||
            state.GuildId != guildId || !Id(guildId) || state.InitializedAt == default || state.DiscoveryFloor == default ||
            state.Forums is null || state.Listings is null || state.Authors is null || state.Overview is null ||
            state.Forums.Count != 4 || forums.Count != 4 || forums.Values.Distinct().Count() != 4 ||
            forums.Any(f => !Id(f.Value) || !state.Forums.TryGetValue(f.Key, out var saved) || saved.ForumId != f.Value))
            throw new InvalidDataException("Recruitment state schema, identity or forum mapping is invalid.");
        var registry = state.PortfolioSites;
        if (registry is null || registry.Version < 1 || registry.Hosts is null || registry.Hosts.Count > 100 ||
            registry.Hosts.Distinct(StringComparer.Ordinal).Count() != registry.Hosts.Count ||
            registry.Hosts.Any(h => !RecruitmentPortfolioDomains.TryNormalize(h, out var normalized) || h != normalized) ||
            registry.Audit is null || registry.Audit.Count > 100 || registry.Audit.Any(a => a is null || a.At == default ||
                !Id(a.Actor) || string.IsNullOrWhiteSpace(a.Reason) || a.Reason.Length > 570))
            throw new InvalidDataException("Recruitment portfolio-site registry is invalid.");
        var overview = state.Overview;
        if (overview.Sequence < 0 ||
            overview.MessageId is not null && (!Id(overview.MessageId) || overview.Marker is null || !overview.Marker.StartsWith($"recruitment:overview:{state.InstanceId}:")) ||
            overview.RetiredMessageId is not null && (!Id(overview.RetiredMessageId) || overview.RetiredMessageId == overview.MessageId || overview.RetiredMarker is null || !overview.RetiredMarker.StartsWith($"recruitment:overview:{state.InstanceId}:")) ||
            overview.SendPending && (overview.SendStartedAt is null || overview.PendingMarker is null || !overview.PendingMarker.StartsWith($"recruitment:overview:{state.InstanceId}:")))
            throw new InvalidDataException("Recruitment overview receipt is invalid.");
        foreach (var f in state.Forums.Values)
            if (f is null || f.Tags is null || f.ConfirmedTopic is null || f.VisibleCode is null || f.Reference is null ||
                f.Reference.ThreadId is not null && !Id(f.Reference.ThreadId) ||
                f.Reference.CreatePending && (f.Reference.CreateStartedAt is null || f.Reference.ThreadId is not null) ||
                f.Tags.Values.Any(t => t is null || !Id(t.Id) || string.IsNullOrWhiteSpace(t.Name)))
                throw new InvalidDataException("Recruitment forum receipt is invalid.");
        foreach (var (id, p) in state.Listings)
            if (p is null || id != p.Id || !Id(id) || !Id(p.OwnerId) || !forums.Values.Contains(p.ForumId) ||
                p.PublicMediaAttachmentId is not null && !Id(p.PublicMediaAttachmentId) ||
                (p.PublicMediaAttachmentId is null) != (p.PublicMediaFileName is null) ||
                p.PublicMediaFileName is { } fileName && (fileName.Length > 120 || !fileName.StartsWith("recruitment-observations-", StringComparison.Ordinal) || !fileName.EndsWith(".png", StringComparison.Ordinal)) ||
                p.PublicRestoreLocked && !p.PublicRestoreArchive ||
                p.PublicPin is null || p.PublicPin.MessageId is not null && !Id(p.PublicPin.MessageId) ||
                p.PublicPin.Confirmed && p.PublicPin.MessageId is null ||
                p.PublicPin.RestoreLocked && !p.PublicPin.RestoreArchive ||
                p.PortfolioRegistryVersion < 0 || p.PortfolioRegistryVersion > registry.Version ||
                p.CreatedAt == default || p.CreatedAt.Offset != TimeSpan.Zero || p.UpdatedAt == default ||
                !Enum.IsDefined(p.Status) || p.ValidCodes is null || p.Facts is null || p.Audit is null ||
                !state.Authors.ContainsKey(p.OwnerId) || p.Facts.Payment is null || p.Facts.Portfolio is null ||
                !FactsValid(p.Facts) || p.Facts.Observations is null || p.Facts.Observations.Count > 4 ||
                p.Facts.Observations.Any(o => o is null || o.Code is null || o.Description is null || o.Evidence is null || o.Evidence.Length > 180) ||
                p.Facts.PaymentEvidence is null || p.Facts.PaymentEvidence.Length > 370 ||
                p.EarlierMaterialVersions is null || p.EarlierMaterialVersions.Count > 3 ||
                p.MaterialChanges < 0 || p.ReviewedMaterialChanges < 0 || p.ReviewedMaterialChanges > p.MaterialChanges ||
                !VersionValid(p.OriginalStarter) || !VersionValid(p.LatestStarter) || p.EarlierMaterialVersions.Any(v => v is null || !VersionValid(v)) ||
                p.Reports is null || p.Reports.Count > 20 || p.Reports.Any(r => r is null || !Id(r.ReporterId) || r.At == default || r.Concern is null || r.Concern.Length > 1000) ||
                p.Attention is null || p.Attention.Count > 8 || p.Attention.Any(n => n is null || !Guid.TryParseExact(n.Id, "N", out _) || n.At == default || n.Reason is null || n.Reason.Length > 300 || n.MessageId is not null && !Id(n.MessageId)) ||
                (p.AcceptedAt.HasValue && !p.AcknowledgedAt.HasValue) ||
                (p.Deadline.HasValue && !p.PromptDeliveredAt.HasValue) ||
                (p.PendingAction is not null && !Enum.IsDefined(p.PendingAction.Kind)))
                throw new InvalidDataException("Recruitment listing state is invalid.");
        foreach (var (id, a) in state.Authors)
            if (!Id(id) || a is null || a.ConsecutiveTimeouts < 0 || a.UpdatedAt == default ||
                a.RecentCreations is null || a.RecentCreations.Count > 21 || a.RecentCreations.Any(d => d == default) || a.RecentReports is null || a.RecentReports.Count > 3 || a.RecentReports.Any(d => d == default))
                throw new InvalidDataException("Recruitment author history is invalid.");
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

    public void Dispose() => _lease.Dispose();
}
