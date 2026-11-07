using DiscordBot.Domain.UnityNews;

namespace DiscordBot.Tests.Domain.UnityNews;

public class ReleaseSummaryTests
{
    private static readonly DateTimeOffset Day1 = new(2026, 10, 15, 10, 0, 0, TimeSpan.Zero);

    private static (UnityRelease, ReleaseNotes) Beta(string version, int days, ReleaseNotes notes) =>
        (UnityNewsFixtures.Release(version, UnityStream.Beta, Day1.AddDays(days)), notes);

    private static ReleaseNotes Notes(string since, KnownIssues known, IEnumerable<NoteSection> sections, params PackageChange[] packages) =>
        new(since, sections.ToList(), packages, known);

    private static NoteSection Section(string name, params (string Area, string Text)[] entries) =>
        new(name, entries.Select(e => new NoteEntry(e.Area, e.Text)).ToList());

    [Fact]
    public void Merge_SingleRelease_IsThatRelease()
    {
        var notes = ReleaseNotesMarkdownParser.Parse(UnityNewsFixtures.Beta8);
        var summary = ReleaseSummary.Merge([Beta("6000.7.0b8", 0, notes)]);

        Assert.False(summary.IsDigest);
        Assert.Equal("6000.7.0b8", summary.Latest.Version);
        Assert.Equal(notes.Sections, summary.Sections);
    }

    [Fact]
    public void Merge_OrdersOldestToNewest_AndRecordsVersions()
    {
        var empty = ReleaseNotes.Empty;
        var summary = ReleaseSummary.Merge([Beta("6000.7.0b8", 14, empty), Beta("6000.7.0b6", 0, empty), Beta("6000.7.0b7", 7, empty)]);

        Assert.True(summary.IsDigest);
        Assert.Equal(["6000.7.0b6", "6000.7.0b7", "6000.7.0b8"], summary.Releases.Select(r => r.Version));
        Assert.Equal("6000.7.0b8", summary.Latest.Version);
    }

    [Fact]
    public void Merge_UnitesEntriesPerSectionAndDropsDuplicates()
    {
        var older = Notes("6000.7.0b5", new KnownIssues(5, 1),
            [Section("Improvements", ("UI", "Faster layout."), ("Editor", "Better menus.")), Section("Fixes", ("UI", "Fixed a crash."))]);
        var newer = Notes("6000.7.0b6", new KnownIssues(2, 0),
            [Section("Features", ("Editor", "A new window.")), Section("Improvements", ("ui", "faster layout."), ("Audio", "Lower latency."))]);

        var summary = ReleaseSummary.Merge([Beta("6000.7.0b6", 7, newer), Beta("6000.7.0b5", 0, older)]);

        Assert.Equal(["Features", "Improvements", "Fixes"], summary.Sections.Select(s => s.Name));
        Assert.Equal(["Faster layout.", "Better menus.", "Lower latency."], summary.Sections[1].Entries.Select(e => e.Text));
        Assert.Equal("6000.7.0b5", summary.Since);
    }

    [Fact]
    public void Merge_KnownIssuesComeFromTheNewestRelease()
    {
        var older = Notes("a", new KnownIssues(9, 3), []);
        var newer = Notes("b", new KnownIssues(4, 1), []);

        var summary = ReleaseSummary.Merge([Beta("6000.7.0b7", 7, newer), Beta("6000.7.0b6", 0, older)]);

        Assert.Equal(new KnownIssues(4, 1), summary.KnownIssues);
    }

    [Fact]
    public void Merge_PackagesKeepEarliestFromAndLatestTo()
    {
        var older = Notes("a", KnownIssues.None, [],
            new PackageChange("com.unity.a", PackageChangeKind.Updated, "1.0.0", "1.1.0"),
            new PackageChange("com.unity.reverted", PackageChangeKind.Updated, "1.0.0", "2.0.0"),
            new PackageChange("com.unity.new", PackageChangeKind.Added, null, "1.0.0"));
        var newer = Notes("b", KnownIssues.None, [],
            new PackageChange("com.unity.a", PackageChangeKind.Updated, "1.1.0", "1.2.0"),
            new PackageChange("com.unity.reverted", PackageChangeKind.Updated, "2.0.0", "1.0.0"),
            new PackageChange("com.unity.new", PackageChangeKind.Updated, "1.0.0", "1.0.1"),
            new PackageChange("com.unity.b", PackageChangeKind.Updated, "3.0.0", "3.1.0"));

        var packages = ReleaseSummary.Merge([Beta("6000.7.0b7", 7, newer), Beta("6000.7.0b6", 0, older)]).Packages;

        Assert.Equal(
            [
                new PackageChange("com.unity.a", PackageChangeKind.Updated, "1.0.0", "1.2.0"),
                new PackageChange("com.unity.new", PackageChangeKind.Added, null, "1.0.1"),
                new PackageChange("com.unity.b", PackageChangeKind.Updated, "3.0.0", "3.1.0"),
            ],
            packages);
    }

    [Fact]
    public void Merge_NoReleases_Throws()
    {
        Assert.Throws<ArgumentException>(() => ReleaseSummary.Merge([]));
    }
}
