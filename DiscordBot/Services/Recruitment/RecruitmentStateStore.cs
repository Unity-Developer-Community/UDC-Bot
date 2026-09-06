using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using DiscordBot.Settings.Options;
using Microsoft.Extensions.Options;

namespace DiscordBot.Services.Recruitment;

/// <summary>
/// Single-writer, versioned snapshots. Missing state requires explicit enrollment;
/// damaged state requires explicit recovery. Callbacks must perform no external I/O.
/// </summary>
public sealed class RecruitmentStateStore : IAsyncDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters = { new SnowflakeConverter(), new JsonStringEnumConverter(allowIntegerValues: false) }
    };

    private readonly SemaphoreSlim _mutex = new(1, 1);
    private readonly ulong _guildId;
    private FileStream? _writerLease;
    private RecruitmentStateDocument? _state;
    private bool _loaded;
    private bool _faulted;
    private bool _disposed;
    public string StatePath { get; }
    public string BackupPath => StatePath + ".bak";
    public bool IsHealthy => _loaded && !_faulted && !_disposed && _state is not null;

    public RecruitmentStateStore(IOptions<StorageOptions> storage, IOptions<DiscordGuildOptions> guild)
    {
        _guildId = guild.Value.GuildId;
        if (_guildId == 0) throw new ArgumentException("A guild ID is required.", nameof(guild));
        StatePath = Path.GetFullPath(Path.Combine(storage.Value.ServerRootPath, "recruitment", "recruitment-state.json"));
    }

    public async Task<RecruitmentStateDocument?> LoadAsync(CancellationToken cancellationToken = default)
    {
        await _mutex.WaitAsync(cancellationToken);
        try
        {
            AcquireWriter();
            if (_loaded && !_faulted) return _state is null ? null : Clone(_state);
            try
            {
                _state = await ReadAsync(StatePath, cancellationToken);
            }
            catch (FileNotFoundException)
            {
                _state = null;
            }
            if (_state is { SchemaVersion: < RecruitmentStateDocument.CurrentSchemaVersion })
            {
                var migrated = Clone(_state);
                migrated.SchemaVersion = RecruitmentStateDocument.CurrentSchemaVersion;
                migrated.Revision = checked(migrated.Revision + 1);
                if (_state.SchemaVersion == 1)
                {
                    foreach (var post in migrated.Posts.Values)
                    {
                        post.Observation.Imported = true;
                        post.Observation.HistoryUncertain = true;
                        post.RequiresReview = true;
                    }
                }
                await WriteAsync(migrated, preservePrevious: true, overwrite: true, cancellationToken);
                _state = migrated;
            }
            _loaded = true;
            _faulted = false;
            return _state is null ? null : Clone(_state);
        }
        catch (OperationCanceledException) { throw; }
        catch
        {
            _faulted = true;
            throw;
        }
        finally { _mutex.Release(); }
    }

    public async Task InitializeAsync(DateTimeOffset enrolledAtUtc, CancellationToken cancellationToken = default)
    {
        await _mutex.WaitAsync(cancellationToken);
        try
        {
            AcquireWriter();
            if (!_loaded || _faulted || _state is not null || File.Exists(StatePath) || File.Exists(BackupPath))
                throw new InvalidOperationException("Load missing state before enrollment; existing state or a backup requires recovery.");
            var document = new RecruitmentStateDocument { GuildId = _guildId, EnrolledAtUtc = enrolledAtUtc };
            Validate(document);
            await WriteAsync(document, preservePrevious: false, overwrite: false, cancellationToken);
            _state = Clone(document);
        }
        catch (OperationCanceledException) { throw; }
        catch
        {
            _faulted = true;
            throw;
        }
        finally { _mutex.Release(); }
    }

    public async Task<T> UpdateAsync<T>(Func<RecruitmentStateDocument, T> update,
        CancellationToken cancellationToken = default)
    {
        await _mutex.WaitAsync(cancellationToken);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (!IsHealthy) throw new InvalidOperationException("Recruitment state is unavailable; load/enroll or recover it first.");
            var working = Clone(_state!);
            var result = update(working);
            working.Revision = checked(_state!.Revision + 1);
            Validate(working);
            try
            {
                await WriteAsync(working, preservePrevious: true, overwrite: true, cancellationToken);
            }
            catch (OperationCanceledException) { throw; }
            catch
            {
                _faulted = true;
                throw;
            }
            // Callers may retain a reference from the callback, so never adopt it.
            _state = Clone(working);
            return result;
        }
        finally { _mutex.Release(); }
    }

    /// <summary>Explicit operator recovery only; never called automatically by LoadAsync.</summary>
    public async Task RestoreBackupAsync(CancellationToken cancellationToken = default)
    {
        await _mutex.WaitAsync(cancellationToken);
        try
        {
            AcquireWriter();
            var backup = await ReadAsync(BackupPath, cancellationToken);
            if (backup.SchemaVersion == 1)
                foreach (var post in backup.Posts.Values)
                {
                    post.Observation.Imported = true;
                    post.Observation.HistoryUncertain = true;
                    post.RequiresReview = true;
                }
            backup.SchemaVersion = RecruitmentStateDocument.CurrentSchemaVersion;
            if (File.Exists(StatePath))
                File.Copy(StatePath, StatePath + $".replaced-{Guid.NewGuid():N}", overwrite: false);
            await WriteAsync(backup, preservePrevious: false, overwrite: true, cancellationToken);
            _state = Clone(backup);
            _loaded = true;
            _faulted = false;
        }
        catch (OperationCanceledException) { throw; }
        catch
        {
            _faulted = true;
            throw;
        }
        finally { _mutex.Release(); }
    }

    private void AcquireWriter()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_writerLease is not null) return;
        Directory.CreateDirectory(Path.GetDirectoryName(StatePath)!);
        _writerLease = new FileStream(StatePath + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        // Keep the lock file; unlinking it allows two writers to lock different inodes.
    }

    private async Task<RecruitmentStateDocument> ReadAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        var document = await JsonSerializer.DeserializeAsync<RecruitmentStateDocument>(stream, JsonOptions, cancellationToken)
            ?? throw new InvalidDataException("Recruitment state must be an object.");
        Validate(document);
        return document;
    }

    private async Task WriteAsync(RecruitmentStateDocument document, bool preservePrevious, bool overwrite,
        CancellationToken cancellationToken)
    {
        var temporaryPath = StatePath + $".{Guid.NewGuid():N}.tmp";
        var backupTemporaryPath = BackupPath + $".{Guid.NewGuid():N}.tmp";
        try
        {
            await using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write,
                             FileShare.None, 4096, FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(stream, document, JsonOptions, cancellationToken);
                await stream.FlushAsync(cancellationToken);
                stream.Flush(flushToDisk: true);
            }
            cancellationToken.ThrowIfCancellationRequested();
            if (preservePrevious)
            {
                // Revalidate the source before preserving it as the last good snapshot.
                var previous = await ReadAsync(StatePath, cancellationToken);
                if (!JsonSerializer.SerializeToUtf8Bytes(previous, JsonOptions)
                        .SequenceEqual(JsonSerializer.SerializeToUtf8Bytes(_state, JsonOptions)))
                    throw new InvalidDataException("Recruitment state changed outside the active writer.");
                File.Copy(StatePath, backupTemporaryPath, overwrite: false);
                File.Move(backupTemporaryPath, BackupPath, overwrite: true);
            }
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporaryPath, StatePath, overwrite);
            // No cancellation point after commit: memory must reflect the committed file.
        }
        finally
        {
            TryDeleteTemporary(temporaryPath);
            TryDeleteTemporary(backupTemporaryPath);
        }
    }

    private static void TryDeleteTemporary(string path)
    {
        try { File.Delete(path); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private static RecruitmentStateDocument Clone(RecruitmentStateDocument document) =>
        JsonSerializer.Deserialize<RecruitmentStateDocument>(JsonSerializer.SerializeToUtf8Bytes(document, JsonOptions), JsonOptions)!;

    private void Validate(RecruitmentStateDocument document)
    {
        if (document.SchemaVersion is not (1 or 2 or RecruitmentStateDocument.CurrentSchemaVersion))
            throw new InvalidDataException("Unsupported recruitment state schema; explicit migration is required.");
        if (document.GuildId != _guildId || document.Revision < 0 || document.DroppedObservationEvents < 0 ||
            document.Posts is null || document.Authors is null || document.Forums is null)
            throw new InvalidDataException("Recruitment state has an invalid guild, revision or collection.");
        RequireUtc(document.EnrolledAtUtc, document.LastGatewayGapAtUtc);
        RecruitmentPublicationValidation.Validate(document);
        foreach (var (id, forum) in document.Forums)
        {
            if (id == 0 || forum is null || forum.Error?.Length > 200)
                throw new InvalidDataException("Recruitment inventory metadata is invalid.");
            RequireUtc(forum.ActiveCheckedAtUtc, forum.ArchiveBeforeUtc, forum.ArchiveCompletedAtUtc);
        }
        foreach (var (id, post) in document.Posts)
        {
            if (post is null || id == 0 || id != post.ThreadId || post.ParentChannelId == 0 || post.AuthorId == 0 ||
                !Enum.IsDefined(post.Forum) || !Enum.IsDefined(post.Acknowledgement) || !Enum.IsDefined(post.Lifecycle) ||
                !Enum.IsDefined(post.Activity) || !Enum.IsDefined(post.Payment) ||
                post.CloseReason is { } reason && !Enum.IsDefined(reason) ||
                post.Title is null || post.Title.Length > 100 || post.AppliedTagIds is null ||
                post.AppliedTagIds.Length > 5 || post.AppliedTagIds.Any(tag => tag == 0) ||
                post.AcceptedCodes is null || post.AcceptedCodes.Length > 8 ||
                post.AcceptedCodes.Any(code => code is null || code.Length != 5 || !code.All(char.IsAsciiLetterOrDigit)))
                throw new InvalidDataException("Recruitment post metadata is invalid.");
            RequireUtc(post.CreatedAtUtc, post.FirstSeenAtUtc, post.AcceptedAtUtc, post.PromptedAtUtc,
                post.ChallengeDeadlineUtc, post.ClosedAtUtc, post.DeletedObservedAtUtc,
                post.FirstQualifyingResponseAtUtc, post.ResponsesCheckedThroughUtc);
            var observation = post.Observation;
            if (observation is null || observation.Error?.Length > 200 || observation.FeedError?.Length > 200 ||
                observation.StarterHash?.Length > 64 || observation.FeedHash?.Length > 64 ||
                observation.FeedSearchBeforeId == 0 ||
                observation.FeedSendRequestedAtUtc is not null && observation.FeedChannelId == 0)
                throw new InvalidDataException("Recruitment observation metadata is invalid.");
            RequireUtc(observation.LastSeenAtUtc, observation.NextCheckAtUtc, observation.JoinedAtUtc,
                observation.FeedSendRequestedAtUtc, observation.FeedRetryAtUtc);
            if (post.FirstSeenAtUtc < post.CreatedAtUtc || post.AcceptedAtUtc < post.CreatedAtUtc ||
                post.PromptedAtUtc < post.CreatedAtUtc || post.ClosedAtUtc < post.CreatedAtUtc ||
                post.DeletedObservedAtUtc < post.CreatedAtUtc || post.FirstQualifyingResponseAtUtc <= post.CreatedAtUtc ||
                post.AcceptedAtUtc is not null && post.Acknowledgement != RecruitmentAcknowledgement.Passed ||
                post.Acknowledgement == RecruitmentAcknowledgement.Pending &&
                (post.PromptedAtUtc is null || post.ChallengeDeadlineUtc is null ||
                 post.ChallengeDeadlineUtc <= post.PromptedAtUtc || post.AcceptedCodes.Length == 0))
                throw new InvalidDataException("Recruitment post transition or deadline is invalid.");
        }
        foreach (var (id, author) in document.Authors)
        {
            if (author is null || id == 0 || author.UserId != id || author.ConsecutiveTimeouts < 0 || author.Groups is null)
                throw new InvalidDataException("Recruitment author metadata is invalid.");
            RequireUtc(author.FirstAttemptAtUtc, author.LastAttemptAtUtc);
            foreach (var (group, history) in author.Groups)
            {
                if (!Enum.IsDefined(group) || history is null)
                    throw new InvalidDataException("Recruitment group history is invalid.");
                RequireUtc(history.LastAcceptedCreatedAtUtc, history.LastAcceptedDeletedAtUtc);
            }
        }
    }

    private static void RequireUtc(params DateTimeOffset?[] dates)
    {
        if (dates.Any(date => date is { } value && (value.Offset != TimeSpan.Zero || value == default)))
            throw new InvalidDataException("Recruitment timestamps must be non-default UTC values.");
    }

    /// <summary>Release the writer between managed lifetimes; the next start must reread disk.</summary>
    public async Task ReleaseAsync()
    {
        await _mutex.WaitAsync();
        try
        {
            _writerLease?.Dispose();
            _writerLease = null;
            _state = null;
            _loaded = false;
        }
        finally { _mutex.Release(); }
    }

    public async ValueTask DisposeAsync()
    {
        await _mutex.WaitAsync();
        try
        {
            if (_disposed) return;
            _disposed = true;
            if (_writerLease is not null) await _writerLease.DisposeAsync();
        }
        finally { _mutex.Release(); }
    }

    private sealed class SnowflakeConverter : JsonConverter<ulong>
    {
        public override ulong Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options) =>
            reader.TokenType == JsonTokenType.String && ulong.TryParse(reader.GetString(), NumberStyles.None,
                CultureInfo.InvariantCulture, out var value) ? value : throw new JsonException("Expected a decimal-string Discord ID.");
        public override void Write(Utf8JsonWriter writer, ulong value, JsonSerializerOptions options) =>
            writer.WriteStringValue(value.ToString(CultureInfo.InvariantCulture));
        public override ulong ReadAsPropertyName(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options) =>
            ulong.TryParse(reader.GetString(), NumberStyles.None, CultureInfo.InvariantCulture, out var value) ?
                value : throw new JsonException("Expected a decimal-string Discord ID key.");
        public override void WriteAsPropertyName(Utf8JsonWriter writer, ulong value, JsonSerializerOptions options) =>
            writer.WritePropertyName(value.ToString(CultureInfo.InvariantCulture));
    }
}
