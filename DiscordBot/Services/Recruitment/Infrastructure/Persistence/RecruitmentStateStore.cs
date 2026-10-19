using DiscordBot.Services.Recruitment.Infrastructure.Persistence;
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

    public static void Validate(RecruitmentState state, string guildId, IReadOnlyDictionary<string, string> forums) =>
        RecruitmentStateValidator.Validate(state, guildId, forums);

    public void Dispose() => _lease.Dispose();
}
