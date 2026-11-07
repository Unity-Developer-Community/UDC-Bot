using DiscordBot.Domain.UnityNews;

namespace DiscordBot.Tests.Domain.UnityNews;

public class ReleaseNotesMarkdownParserTests
{
    private static NoteSection Section(ReleaseNotes notes, string name) => Assert.Single(notes.Sections, s => s.Name == name);

    [Fact]
    public void Beta_ReadsTheDeltaBlockAndIgnoresThePreviewOfFinalNotes()
    {
        var notes = ReleaseNotesMarkdownParser.Parse(UnityNewsFixtures.Beta8);

        Assert.Equal("6000.7.0b7", notes.Since);
        Assert.Equal(["Features", "Improvements", "API Changes", "Changes", "Fixes"], notes.Sections.Select(s => s.Name));
        Assert.Single(Section(notes, "Features").Entries);
        Assert.Equal(4, Section(notes, "Improvements").Entries.Count);
        Assert.Equal(4, Section(notes, "Fixes").Entries.Count);
        Assert.DoesNotContain(notes.Sections.SelectMany(s => s.Entries), e => e.Text.Contains("cumulative"));
        Assert.DoesNotContain(notes.Packages, p => p.Name == "com.unity.ignored");
    }

    [Fact]
    public void Lts_ReadsTheReleaseNotesBlock()
    {
        var notes = ReleaseNotesMarkdownParser.Parse(UnityNewsFixtures.Lts);

        Assert.Null(notes.Since);
        Assert.Equal(["Improvements", "API Changes", "Fixes"], notes.Sections.Select(s => s.Name));
        Assert.Equal(3, Section(notes, "Fixes").Entries.Count);
        Assert.Equal(["com.unity.collab-proxy", "com.unity.probuilder"], notes.Packages.Select(p => p.Name));
    }

    [Fact]
    public void Entries_SplitAreaAndStripIssueRefsAndSeenNotes()
    {
        var notes = ReleaseNotesMarkdownParser.Parse(UnityNewsFixtures.Beta8);
        var improvements = Section(notes, "Improvements").Entries;

        Assert.Equal(new NoteEntry("Asset Pipeline", "Improved Memory Profiler attribution of Asset Database native memory."), improvements[0]);
        // Both the linked "([UUM-1](url))" and the bare "(UUM-1)" forms, "<br>" and "*First seen in*" are gone.
        Assert.Equal("Fixed an issue where import workers would add overhead.", improvements[1].Text);
        Assert.All(notes.Sections.SelectMany(s => s.Entries), e =>
        {
            Assert.DoesNotContain("UUM", e.Text);
            Assert.DoesNotContain("First seen", e.Text);
            Assert.DoesNotContain("<br>", e.Text);
        });
    }

    [Fact]
    public void Entries_NestedAreaKeepsTheFirst_EmptyAreaIsOther()
    {
        var notes = ReleaseNotesMarkdownParser.Parse(UnityNewsFixtures.Beta8);

        var auditor = Section(notes, "Improvements").Entries[2];
        Assert.Equal("Editor", auditor.Area);
        Assert.StartsWith("Project Auditor: ", auditor.Text);
        Assert.Contains(new NoteEntry("Other", "Fixed a crash with an empty area."), Section(notes, "Fixes").Entries);
    }

    [Fact]
    public void Entries_DecodeHtmlUnescapeMarkdownAndFlattenLinks()
    {
        var notes = ReleaseNotesMarkdownParser.Parse(UnityNewsFixtures.Beta8);

        Assert.Equal("Calling BlobArray<T>.ToArray is now reported as compile error (UAC0044).", Section(notes, "Improvements").Entries[3].Text);
        Assert.Equal("Raised the minimum version of the Hub to 3.0.", Section(notes, "Changes").Entries[0].Text);
    }

    [Fact]
    public void ApiChanges_VerbPrefixIsKeptUnlessTheTextRepeatsIt()
    {
        var api = Section(ReleaseNotesMarkdownParser.Parse(UnityNewsFixtures.Beta8), "API Changes").Entries;

        Assert.Equal("Changed · Moved EntityIdTileUtility to the UnityEditor.Tilemaps namespace.", api[0].Text);
        Assert.Equal("Added BuildHistory.TryGetParentBuild to find the parent build.", api[1].Text);
        Assert.Equal("Added a new attribute.", api[2].Text);
    }

    [Fact]
    public void KnownIssues_CountsBulletsAndThoseFixedLater()
    {
        var beta = ReleaseNotesMarkdownParser.Parse(UnityNewsFixtures.Beta8).KnownIssues;
        var lts = ReleaseNotesMarkdownParser.Parse(UnityNewsFixtures.Lts).KnownIssues;

        Assert.Equal(new KnownIssues(3, 1), beta);
        Assert.Equal(new KnownIssues(2, 0), lts);
    }

    [Fact]
    public void Packages_UpdatedAddedAndDeprecated()
    {
        var packages = ReleaseNotesMarkdownParser.Parse(UnityNewsFixtures.Beta8).Packages;

        Assert.Equal(4, packages.Count);
        Assert.Equal(new PackageChange("com.unity.addressables", PackageChangeKind.Updated, "4.1.1", "4.1.2"), packages[0]);
        Assert.Equal(new PackageChange("com.unity.services.cloudcode", PackageChangeKind.Updated, "2.10.4", "3.0.0"), packages[1]);
        Assert.Equal(new PackageChange("com.unity.external-dependency-manager", PackageChangeKind.Added, null, "2.1.1"), packages[2]);
        Assert.Equal(new PackageChange("com.unity.2d.pixel-perfect", PackageChangeKind.Deprecated, null, null, "The 2D Pixel Perfect package is deprecated."), packages[3]);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Unity 6000.7.0 Beta")]
    [InlineData("<div>not markdown</div>")]
    public void NoRecognisableSections_GivesEmptyNotes(string markdown)
    {
        var notes = ReleaseNotesMarkdownParser.Parse(markdown);

        Assert.Empty(notes.Sections);
        Assert.Empty(notes.Packages);
        Assert.Equal(KnownIssues.None, notes.KnownIssues);
    }

    [Fact]
    public void WindowsLineEndings_ParseTheSame()
    {
        var unix = ReleaseNotesMarkdownParser.Parse(UnityNewsFixtures.Lts);
        var windows = ReleaseNotesMarkdownParser.Parse(UnityNewsFixtures.Lts.Replace("\n", "\r\n"));

        Assert.Equal(unix.Sections.Select(s => (s.Name, s.Entries.Count)), windows.Sections.Select(s => (s.Name, s.Entries.Count)));
        Assert.Equal(unix.Sections[2].Entries[0], windows.Sections[2].Entries[0]);
    }
}
