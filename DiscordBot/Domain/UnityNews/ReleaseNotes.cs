namespace DiscordBot.Domain.UnityNews;

/// <summary>One bullet of a release notes section, split into its area (<c>UI Toolkit</c>) and cleaned text.</summary>
public sealed record NoteEntry(string Area, string Text);

/// <summary>A <c>####</c> section of the release notes, such as Features or Fixes.</summary>
public sealed record NoteSection(string Name, IReadOnlyList<NoteEntry> Entries);

public enum PackageChangeKind
{
    Updated,
    Added,
    Deprecated,
    Removed,
}

/// <summary>A package line: <c>From</c> and <c>To</c> for updates, <c>To</c> for additions, <c>Note</c> for deprecations.</summary>
public sealed record PackageChange(string Name, PackageChangeKind Kind, string? From, string? To, string? Note = null);

/// <summary>How many known issues the notes list and how many of them say they are fixed in a later release.</summary>
public sealed record KnownIssues(int Count, int FixedLater)
{
    public static readonly KnownIssues None = new(0, 0);
}

/// <summary>What changed in one release (the delta block of its notes), without the cumulative preview of final notes.</summary>
public sealed record ReleaseNotes(
    string? Since,
    IReadOnlyList<NoteSection> Sections,
    IReadOnlyList<PackageChange> Packages,
    KnownIssues KnownIssues)
{
    public static readonly ReleaseNotes Empty = new(null, [], [], KnownIssues.None);
}
