namespace DiscordBot.Domain.UnityNews;

/// <summary>
/// What a post shows: one release, or a digest merging every release since the last post (oldest to newest).
/// </summary>
public sealed record ReleaseSummary(
    IReadOnlyList<UnityRelease> Releases,
    string? Since,
    IReadOnlyList<NoteSection> Sections,
    IReadOnlyList<PackageChange> Packages,
    KnownIssues KnownIssues)
{
    public UnityRelease Latest => Releases[^1];

    public bool IsDigest => Releases.Count > 1;

    public static ReleaseSummary From(UnityRelease release, ReleaseNotes notes) =>
        new([release], notes.Since, notes.Sections, notes.Packages, notes.KnownIssues);

    /// <summary>
    /// Merges the delta notes of several releases: entries are united per section with duplicates removed,
    /// packages keep the earliest "from" (none when added within the digest) and the latest "to", known issues come from the newest release.
    /// </summary>
    public static ReleaseSummary Merge(IEnumerable<(UnityRelease Release, ReleaseNotes Notes)> items)
    {
        var ordered = items.OrderBy(i => i.Release.ReleaseDate).ThenBy(i => i.Release.Version, StringComparer.Ordinal).ToList();
        if (ordered.Count == 0)
            throw new ArgumentException("At least one release is required.", nameof(items));
        if (ordered.Count == 1)
            return From(ordered[0].Release, ordered[0].Notes);

        var sectionNames = new List<string>();
        var entries = new Dictionary<string, List<NoteEntry>>(StringComparer.OrdinalIgnoreCase);
        var seen = new HashSet<(string, string, string)>();
        var packages = new List<PackageChange>();

        foreach (var (_, notes) in ordered)
        {
            foreach (var section in notes.Sections)
            {
                if (!entries.TryGetValue(section.Name, out var list))
                {
                    list = [];
                    entries[section.Name] = list;
                    sectionNames.Add(section.Name);
                }
                foreach (var entry in section.Entries)
                    if (seen.Add((section.Name.ToLowerInvariant(), entry.Area.ToLowerInvariant(), entry.Text.ToLowerInvariant())))
                        list.Add(entry);
            }

            foreach (var package in notes.Packages)
            {
                var index = packages.FindIndex(p => p.Name.Equals(package.Name, StringComparison.OrdinalIgnoreCase));
                if (index < 0)
                    packages.Add(package);
                else
                {
                    var earlier = packages[index];
                    var added = earlier.Kind == PackageChangeKind.Added;
                    packages[index] = new PackageChange(
                        earlier.Name,
                        added ? PackageChangeKind.Added : package.Kind,
                        added ? null : earlier.From ?? package.From,
                        package.To ?? earlier.To,
                        package.Note ?? earlier.Note);
                }
            }
        }

        // An update that was reverted within the digest is not a change.
        packages.RemoveAll(p => p.Kind == PackageChangeKind.Updated && p.From != null && p.From == p.To);

        var sections = sectionNames
            .OrderBy(ReleaseNotesMarkdownParser.OrderIndex)
            .Select(name => new NoteSection(name, entries[name]))
            .ToList();

        return new ReleaseSummary(
            ordered.Select(i => i.Release).ToList(),
            ordered[0].Notes.Since,
            sections,
            packages,
            ordered[^1].Notes.KnownIssues);
    }
}
