using System.IO;
using System.Text.Json;
using DiscordBot.Settings.Options;
using Microsoft.Extensions.Options;

namespace DiscordBot.Components;

public sealed record ComponentOverride(
    bool Enabled,
    string Actor,
    DateTimeOffset ChangedAtUtc,
    string? Reason);

public sealed class ComponentOverrideDocument
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;
    public Dictionary<string, ComponentOverride> Components { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);
}

public interface IComponentOverrideStore
{
    Task<IReadOnlyDictionary<string, ComponentOverride>> LoadAsync(CancellationToken cancellationToken);
    Task SaveAsync(IReadOnlyDictionary<string, ComponentOverride> overrides, CancellationToken cancellationToken);
}

public sealed class ComponentOverrideStore(IOptions<StorageOptions> storageOptions) : IComponentOverrideStore
{
    public const string FileName = "component-overrides.v1.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private readonly string _path = Path.Combine(storageOptions.Value.ServerRootPath, FileName);

    public async Task<IReadOnlyDictionary<string, ComponentOverride>> LoadAsync(
        CancellationToken cancellationToken)
    {
        if (!File.Exists(_path))
            return new Dictionary<string, ComponentOverride>(StringComparer.OrdinalIgnoreCase);

        try
        {
            await using var stream = File.OpenRead(_path);
            var document = await JsonSerializer.DeserializeAsync<ComponentOverrideDocument>(
                stream,
                JsonOptions,
                cancellationToken);
            if (document is null || document.SchemaVersion != ComponentOverrideDocument.CurrentSchemaVersion)
                throw new InvalidDataException("Unsupported component override schema version.");

            return new Dictionary<string, ComponentOverride>(
                document.Components,
                StringComparer.OrdinalIgnoreCase);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            var backupPath = $"{_path}.corrupt-{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}.bak";
            try
            {
                File.Move(_path, backupPath, overwrite: false);
            }
            catch (Exception)
            {
                // Leave the unreadable source untouched if the backup cannot be created.
            }
            return new Dictionary<string, ComponentOverride>(StringComparer.OrdinalIgnoreCase);
        }
    }

    public async Task SaveAsync(
        IReadOnlyDictionary<string, ComponentOverride> overrides,
        CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(_path)
            ?? throw new InvalidOperationException("The component override path has no parent directory.");
        Directory.CreateDirectory(directory);

        var document = new ComponentOverrideDocument
        {
            Components = new Dictionary<string, ComponentOverride>(overrides, StringComparer.OrdinalIgnoreCase)
        };
        var temporaryPath = $"{_path}.{Guid.NewGuid():N}.tmp";
        try
        {
            await using (var stream = new FileStream(
                             temporaryPath,
                             FileMode.CreateNew,
                             FileAccess.Write,
                             FileShare.None,
                             bufferSize: 4096,
                             useAsync: true))
            {
                await JsonSerializer.SerializeAsync(stream, document, JsonOptions, cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }

            File.Move(temporaryPath, _path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
        }
    }
}
