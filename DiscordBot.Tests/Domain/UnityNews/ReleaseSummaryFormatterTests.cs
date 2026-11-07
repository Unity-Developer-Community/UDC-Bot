using DiscordBot.Domain.UnityNews;

namespace DiscordBot.Tests.Domain.UnityNews;

public class ReleaseSummaryFormatterTests
{
    private static ReleaseSummary Summary(UnityStream stream, string version, ReleaseNotes notes, bool recommended = false) =>
        ReleaseSummary.From(UnityNewsFixtures.Release(version, stream, recommended: recommended), notes);

    private static ReleaseSummary BigBeta() => Summary(UnityStream.Beta, "6000.7.0b8", UnityNewsFixtures.BigBetaNotes());

    private static string Text(ReleaseMessage message) => string.Join("\n", message.Blocks);

    private static string AllText(IEnumerable<ReleaseMessage> messages) => string.Join("\n", messages.Select(Text));

    private static NoteSection Section(string name, IEnumerable<(string Area, string Text)> entries) =>
        new(name, entries.Select(e => new NoteEntry(e.Area, e.Text)).ToList());

    private static ReleaseNotes Notes(params NoteSection[] sections) => new(null, sections, [], KnownIssues.None);

    private static void AssertWithinBudget(IReadOnlyList<ReleaseMessage> messages)
    {
        Assert.InRange(messages.Count, 1, ReleaseSummaryFormatter.MaxMessages);
        Assert.All(messages, m => Assert.True(m.TextLength <= ReleaseSummaryFormatter.MessageBudget, $"Message was {m.TextLength} chars"));
    }

    [Fact]
    public void BigBeta_StarterHasHeaderAndFeaturesAndFitsTheBudget()
    {
        var messages = ReleaseSummaryFormatter.Format(BigBeta());
        var starter = Text(messages[0]);

        AssertWithinBudget(messages);
        Assert.True(messages[0].TextLength < 4000);
        Assert.StartsWith("## [Beta · 6000.7.0b8](<https://unity.com/releases/editor/beta/6000.7.0b8>)", starter);
        Assert.Contains("Unity 6.7 Beta", starter);
        Assert.Contains("changes since 6000.7.0b7", starter);
        Assert.Contains("Features **1** · Improvements **10** · API Changes **12** · Changes **13** · Fixes **253** · Packages **25**", starter);
        Assert.Contains("⚠️ **7 known issues** (2 fixed in a later release)", starter);
        Assert.Contains("### Features · 1", starter);
        Assert.Contains("- **Area 01**: Features entry 0", starter);
    }

    [Fact]
    public void BigBeta_FixesAreAreaCountsOnly()
    {
        var all = AllText(ReleaseSummaryFormatter.Format(BigBeta()));

        Assert.Contains("### Fixes · 253", all);
        Assert.Contains("Area 01 **7** · Area 02 **7**", all);
        Assert.DoesNotContain("Fixes entry", all);
    }

    [Fact]
    public void BigBeta_PackagesAreCompact()
    {
        var all = AllText(ReleaseSummaryFormatter.Format(BigBeta()));

        Assert.Contains("### Packages · 25", all);
        Assert.Contains("- `com.unity.package00` 1.0.0 → 1.1.0", all);
        Assert.DoesNotContain("http", all.Split("### Packages")[1]);
    }

    [Fact]
    public void Lts_FitsOneMessage()
    {
        var notes = ReleaseNotesMarkdownParser.Parse(UnityNewsFixtures.Lts);
        var messages = ReleaseSummaryFormatter.Format(Summary(UnityStream.Lts, "6000.3.28f1", notes, recommended: true));
        var text = Text(Assert.Single(messages));

        Assert.StartsWith("## [LTS · 6000.3.28f1](<https://unity.com/releases/editor/whats-new/6000.3.28f1>)", text);
        Assert.Contains("Unity 6.3 LTS", text);
        Assert.Contains("✅ recommended", text);
        Assert.DoesNotContain("changes since", text);
        Assert.Contains("⚠️ **2 known issues** — [see release notes]", text);
        Assert.Contains("### Improvements · 1", text);
        Assert.Contains("- **License**: Updated the Licensing Client to v1.19.0, which starts faster.", text);
        Assert.Contains("- **iOS**: Added iPhone 18 Pro device generation enums.", text);
        Assert.Contains("- **2D**: Fixed shadows from a Collider 2D (for example a Tilemap Collider 2D) flickering.", text);
        Assert.Contains("- `com.unity.collab-proxy` 2.13.6 → 2.14.0", text);
    }

    [Fact]
    public void Digest_HeaderStatesHowManyReleasesAreIncluded()
    {
        var items = new[] { "6000.7.0b8", "6000.7.0b6", "6000.7.0b7" }
            .Select((v, i) => (UnityNewsFixtures.Release(v, UnityStream.Beta, new DateTimeOffset(2026, 10, 1 + i, 10, 0, 0, TimeSpan.Zero)), ReleaseNotes.Empty))
            .ToList();
        var summary = ReleaseSummary.Merge(items);

        var starter = Text(ReleaseSummaryFormatter.Format(summary)[0]);

        Assert.Contains("**3 betas since last post:** b8, b6, b7", starter);
    }

    [Fact]
    public void SingleRelease_HasNoDigestLine()
    {
        Assert.DoesNotContain("since last post", Text(ReleaseSummaryFormatter.Format(BigBeta())[0]));
    }

    [Fact]
    public void Digest_ListsAtMostTwelveVersions()
    {
        var items = Enumerable.Range(1, 15)
            .Select(i => (UnityNewsFixtures.Release($"7000.0.0a{i}", UnityStream.Alpha, new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero).AddDays(i)), ReleaseNotes.Empty));

        var starter = Text(ReleaseSummaryFormatter.Format(ReleaseSummary.Merge(items))[0]);

        Assert.Contains("**15 alphas since last post:** a1, a2,", starter);
        Assert.Contains("a12, +3 more", starter);
    }

    [Fact]
    public void Preamble_IsTheFirstLineOfTheFirstMessageOnly()
    {
        var messages = ReleaseSummaryFormatter.Format(BigBeta(), "<@&123>");

        Assert.StartsWith("<@&123>\n## [Beta", Text(messages[0]));
        Assert.DoesNotContain("<@&123>", AllText(messages.Skip(1)));
        Assert.DoesNotContain("<@&", AllText(ReleaseSummaryFormatter.Format(BigBeta())));
    }

    [Fact]
    public void SmallSection_ListsEveryEntry()
    {
        var notes = Notes(Section("Improvements", Enumerable.Repeat(("UI", "x"), 1).Concat(Enumerable.Range(0, 4).Select(i => ("Editor", $"entry {i}")))));
        var text = Text(Assert.Single(ReleaseSummaryFormatter.Format(Summary(UnityStream.Lts, "6000.3.1f1", notes))));

        Assert.Contains("### Improvements · 5", text);
        Assert.Contains("- **Editor**: entry 0", text);
        Assert.Contains("- **Editor**: entry 3", text);
        Assert.Contains("- **UI**: x", text);
    }

    [Fact]
    public void BigSection_ListsSmallAreasAndCollapsesTheBigOnes()
    {
        var entries = Enumerable.Range(0, 4).Select(i => ("Editor", $"editor {i}"))
            .Concat(Enumerable.Range(0, 2).Select(i => ("Audio", $"audio {i}")))
            .Append(("Physics", "physics 0"));
        var text = Text(Assert.Single(ReleaseSummaryFormatter.Format(Summary(UnityStream.Lts, "6000.3.1f1", Notes(Section("Improvements", entries))))));

        Assert.Contains("### Improvements · 7", text);
        Assert.Contains("- **Editor** — 4 improvements", text);
        Assert.DoesNotContain("editor 0", text);
        Assert.Contains("- **Audio**: audio 0", text);
        Assert.Contains("- **Audio**: audio 1", text);
        Assert.Contains("- **Physics**: physics 0", text);
        Assert.True(text.IndexOf("- **Editor** —", StringComparison.Ordinal) < text.IndexOf("- **Audio**:", StringComparison.Ordinal), "biggest areas first");
    }

    [Theory]
    [InlineData(8, false)]
    [InlineData(9, true)]
    public void Fixes_AreCountsOnlyAboveEight(int count, bool countsOnly)
    {
        var fixes = Enumerable.Range(0, count).Select(i => ($"Area {i % 4}", $"fix {i}"));
        var text = Text(Assert.Single(ReleaseSummaryFormatter.Format(Summary(UnityStream.Lts, "6000.3.1f1", Notes(Section("Fixes", fixes))))));

        Assert.Contains($"### Fixes · {count}", text);
        Assert.Equal(countsOnly, !text.Contains("fix 0"));
        Assert.Equal(countsOnly, text.Contains("Area 0 **"));
    }

    [Fact]
    public void LongEntry_IsShortenedWithEllipsis_AndMentionsAreDefused()
    {
        var entries = new[] { ("UI", new string('a', 300)), ("UI", "ping @everyone and <@123> and @here") };
        var text = Text(Assert.Single(ReleaseSummaryFormatter.Format(Summary(UnityStream.Lts, "6000.3.1f1", Notes(Section("Changes", entries))))));

        var line = text.Split('\n').Single(l => l.StartsWith("- **UI**: aaa", StringComparison.Ordinal));
        Assert.Equal("- **UI**: ".Length + ReleaseSummaryFormatter.EntryMaxLength, line.Length);
        Assert.EndsWith("…", line);
        Assert.DoesNotContain("@everyone", text, StringComparison.Ordinal);
        Assert.DoesNotContain("@here", text, StringComparison.Ordinal);
        Assert.DoesNotContain("<@123>", text, StringComparison.Ordinal);
    }

    [Fact]
    public void OversizeSection_SplitsBetweenLinesAndRepeatsTheHeading()
    {
        // 45 areas of one entry each: nothing collapses, and the listing is over one message.
        var entries = Enumerable.Range(0, 45).Select(i => ($"Area {i:00}", new string('x', 100)));
        var messages = ReleaseSummaryFormatter.Format(Summary(UnityStream.Lts, "6000.3.1f1", Notes(Section("Changes", entries))));

        AssertWithinBudget(messages);
        Assert.Equal(2, messages.Count);
        Assert.Contains("### Changes · 45", Text(messages[0]));
        Assert.Contains("### Changes · 45 (continued)", Text(messages[1]));
        Assert.All(AllText(messages).Split('\n').Where(l => l.StartsWith("- **", StringComparison.Ordinal)), l => Assert.EndsWith(new string('x', 100), l));
        Assert.Equal(45, AllText(messages).Split('\n').Count(l => l.StartsWith("- **Area", StringComparison.Ordinal)));
    }

    [Fact]
    public void TooManyMessages_CollapseTheBiggestSectionsFirst()
    {
        // 600 entries in 300 two-entry areas would list in full over ~10 messages; collapsing leaves counts.
        var improvements = Enumerable.Range(0, 600).Select(i => ($"Area {i % 300:000}", $"improvement {i} " + new string('y', 80)));
        var features = Enumerable.Range(0, 3).Select(i => ("Editor", $"feature {i}"));
        var messages = ReleaseSummaryFormatter.Format(Summary(UnityStream.Beta, "6000.7.0b8", Notes(Section("Features", features), Section("Improvements", improvements))));
        var all = AllText(messages);

        AssertWithinBudget(messages);
        Assert.Contains("- **Editor**: feature 0", all);
        Assert.Contains("### Improvements · 600", all);
        Assert.Contains("Area 000 **2**", all);
        Assert.DoesNotContain("improvement 0 ", all);
        Assert.DoesNotContain("More in the [full release notes]", all);
    }

    [Fact]
    public void StillTooBigWhenCollapsed_IsTruncatedWithALinkToTheNotes()
    {
        var sections = Enumerable.Range(0, 8)
            .Select(s => Section($"Section {s}", Enumerable.Range(0, 400).Select(i => ($"Area number {i:000} of {s}", "entry"))))
            .ToArray();
        var messages = ReleaseSummaryFormatter.Format(Summary(UnityStream.Alpha, "7000.0.0a16", Notes(sections)));

        AssertWithinBudget(messages);
        Assert.Equal(ReleaseSummaryFormatter.MaxMessages, messages.Count);
        Assert.Contains("-# More in the [full release notes](<https://unity.com/releases/editor/alpha/7000.0.0a16>)", Text(messages[^1]));
    }

    [Fact]
    public void ManyPackages_AreCappedWithAMoreLine()
    {
        var packages = Enumerable.Range(0, 200).Select(i => new PackageChange($"com.unity.package{i:000}", PackageChangeKind.Updated, "1.0.0", "1.1.0")).ToList();
        var notes = new ReleaseNotes(null, [], packages, KnownIssues.None);

        var text = Text(Assert.Single(ReleaseSummaryFormatter.Format(Summary(UnityStream.Lts, "6000.3.1f1", notes))));

        Assert.Contains("Packages **200**", text);
        Assert.Matches(@"-# …and \d+ more", text);
    }

    [Fact]
    public void NoNotes_StillGivesAHeader()
    {
        var text = Text(Assert.Single(ReleaseSummaryFormatter.Format(Summary(UnityStream.Lts, "6000.3.1f1", ReleaseNotes.Empty))));

        Assert.StartsWith("## [LTS · 6000.3.1f1]", text);
        Assert.DoesNotContain("###", text);
    }
}
