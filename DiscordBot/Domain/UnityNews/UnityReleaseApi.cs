namespace DiscordBot.Domain.UnityNews;

/// <summary>
/// One page of <c>services.api.unity.com/unity/editor/release/v1/releases</c>. Only the fields the bot uses
/// are mapped; Newtonsoft matches property names case-insensitively.
/// </summary>
public sealed class UnityReleaseApiPage
{
    public int Offset { get; set; }
    public int Limit { get; set; }
    public int Total { get; set; }
    public List<UnityReleaseApiItem> Results { get; set; } = [];

    /// <summary>The releases of the page, newest first as returned; entries with an unknown stream or no version are skipped.</summary>
    public IReadOnlyList<UnityRelease> ToReleases() =>
        Results.Select(r => r.ToRelease()).OfType<UnityRelease>().ToList();
}

public sealed class UnityReleaseApiItem
{
    public string? Version { get; set; }
    public DateTimeOffset ReleaseDate { get; set; }
    public UnityReleaseApiNotes? ReleaseNotes { get; set; }
    public string? Stream { get; set; }
    public bool Recommended { get; set; }

    public UnityRelease? ToRelease()
    {
        if (string.IsNullOrWhiteSpace(Version) || !Enum.TryParse<UnityStream>(Stream, ignoreCase: true, out var stream))
            return null;
        return new UnityRelease(Version, stream, ReleaseDate, ReleaseNotes?.Url, Recommended);
    }
}

public sealed class UnityReleaseApiNotes
{
    public string? Url { get; set; }
}
