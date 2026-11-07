namespace DiscordBot.Domain.UnityNews;

/// <summary>One Discord message of a release post: text blocks, each shown as its own text display.</summary>
public sealed record ReleaseMessage(IReadOnlyList<string> Blocks)
{
    public int TextLength => Blocks.Sum(b => b.Length);
}

/// <summary>
/// Lays a <see cref="ReleaseSummary"/> out as text blocks packed into messages. The first message starts with the
/// header (title, counts, known issues). Small sections are listed in full; in bigger sections areas with few
/// entries are listed and the others collapse to a count; long fix lists show area counts only. Sections are never
/// cut mid-line, and the number of messages is capped by collapsing the largest sections.
/// </summary>
public static class ReleaseSummaryFormatter
{
    /// <summary>Text per message, below Discord's 4000 for components V2 messages.</summary>
    public const int MessageBudget = 3800;
    public const int MaxMessages = 4;

    /// <summary>Sections up to this many entries are listed in full.</summary>
    public const int FullSectionMax = 5;

    /// <summary>In bigger sections, areas up to this many entries are listed in full.</summary>
    public const int FullAreaMax = 2;

    /// <summary>Fixes beyond this many are shown as area counts only.</summary>
    public const int FixesListMax = 8;

    public const int EntryMaxLength = 140;
    private const int PackageLineMaxLength = 100;
    private const int MaxPackagesChars = 1800;
    private const int MaxCountsChars = 2600;
    private const int MaxListedVersions = 12;
    private const int MinSplitRoom = 600;

    /// <param name="summary">The release or digest to show.</param>
    /// <param name="preamble">Optional first line of the first message, for example a role mention.</param>
    public static IReadOnlyList<ReleaseMessage> Format(ReleaseSummary summary, string? preamble = null)
    {
        var header = Header(summary, preamble);
        var sections = summary.Sections.Select(s => new SectionPlan(s)).ToList();
        var packages = PackagesBlock(summary.Packages);

        while (true)
        {
            var blocks = sections.Select(s => s.Lines).ToList();
            if (packages != null)
                blocks.Add(packages);
            var messages = Pack(header, blocks);
            if (messages.Count <= MaxMessages)
                return messages.Select(m => new ReleaseMessage(m)).ToList();

            var victim = sections.Where(s => s.CanCollapse).MaxBy(s => s.Savings);
            if (victim == null)
                return Truncate(messages, summary);
            victim.Collapsed = true;
        }
    }

    private static string Header(ReleaseSummary summary, string? preamble)
    {
        var latest = summary.Latest;
        var lines = new List<string>();
        if (!string.IsNullOrWhiteSpace(preamble))
            lines.Add(preamble);
        lines.Add($"## [{latest.Title}](<{latest.PageUrl}>)");

        var subtext = $"-# {latest.DisplayLine} {latest.StreamLabel} · released <t:{latest.ReleaseDate.ToUnixTimeSeconds()}:D>";
        if (latest.Recommended)
            subtext += " · ✅ recommended";
        if (summary.Since != null)
            subtext += $" · changes since {summary.Since}";
        lines.Add(subtext);

        if (summary.IsDigest)
        {
            var versions = summary.Releases.Select(r => r.ShortVersion).ToList();
            var listed = string.Join(", ", versions.Take(MaxListedVersions));
            if (versions.Count > MaxListedVersions)
                listed += $", +{versions.Count - MaxListedVersions} more";
            lines.Add($"**{versions.Count} {Plural(latest.Stream)} since last post:** {listed}");
        }

        var counts = summary.Sections.Select(s => $"{s.Name} **{s.Entries.Count}**").ToList();
        if (summary.Packages.Count > 0)
            counts.Add($"Packages **{summary.Packages.Count}**");
        if (counts.Count > 0)
            lines.Add(string.Join(" · ", counts));

        var known = summary.KnownIssues;
        if (known.Count > 0)
        {
            var fixedLater = known.FixedLater > 0 ? $" ({known.FixedLater} fixed in a later release)" : "";
            lines.Add($"⚠️ **{known.Count} known {(known.Count == 1 ? "issue" : "issues")}**{fixedLater} — [see release notes](<{latest.PageUrl}>)");
        }

        return string.Join('\n', lines);
    }

    private static string Plural(UnityStream stream) => stream switch
    {
        UnityStream.Alpha => "alphas",
        UnityStream.Beta => "betas",
        UnityStream.Supported => "supported releases",
        _ => "LTS releases",
    };

    private sealed class SectionPlan
    {
        private readonly NoteSection _section;
        private readonly List<string> _full;
        private readonly List<string> _compact;

        public SectionPlan(NoteSection section)
        {
            _section = section;
            _full = BuildLines(section, compact: false);
            _compact = BuildLines(section, compact: true);
        }

        public bool Collapsed { get; set; }

        public List<string> Lines => Collapsed ? _compact : _full;

        public bool CanCollapse => !Collapsed && Savings > 0;

        public int Savings => Length(_full) - Length(_compact);

        public string Name => _section.Name;

        private static int Length(List<string> lines) => lines.Sum(l => l.Length + 1);
    }

    private static List<string> BuildLines(NoteSection section, bool compact)
    {
        var count = section.Entries.Count;
        var lines = new List<string> { $"### {section.Name} · {count}" };
        var groups = section.Entries
            .GroupBy(e => e.Area, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(g => g.Count())
            .ThenBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var countsOnly = compact || (section.Name.Equals("Fixes", StringComparison.OrdinalIgnoreCase) && count > FixesListMax);
        if (countsOnly)
        {
            lines.Add(AreaCounts(groups.Select(g => (g.Key, g.Count())).ToList()));
            return lines;
        }

        foreach (var group in groups)
        {
            if (count <= FullSectionMax || group.Count() <= FullAreaMax)
                lines.AddRange(group.Select(e => $"- **{Safe(group.Key)}**: {Short(e.Text)}"));
            else
                lines.Add($"- **{Safe(group.Key)}** — {group.Count()} {section.Name.ToLowerInvariant()}");
        }
        return lines;
    }

    private static string AreaCounts(IReadOnlyList<(string Area, int Count)> areas)
    {
        var parts = new List<string>();
        var length = 0;
        for (var i = 0; i < areas.Count; i++)
        {
            var part = areas[i].Count > 1 ? $"{Safe(areas[i].Area)} **{areas[i].Count}**" : Safe(areas[i].Area);
            var more = $" · +{areas.Count - i} more";
            if (length + part.Length + 3 + more.Length > MaxCountsChars)
            {
                parts.Add($"+{areas.Count - i} more");
                break;
            }
            parts.Add(part);
            length += part.Length + 3;
        }
        return string.Join(" · ", parts);
    }

    private static List<string>? PackagesBlock(IReadOnlyList<PackageChange> packages)
    {
        if (packages.Count == 0)
            return null;

        var lines = new List<string> { $"### Packages · {packages.Count}" };
        var length = lines[0].Length;
        for (var i = 0; i < packages.Count; i++)
        {
            var line = "- " + PackageLine(packages[i]);
            if (length + line.Length + 1 > MaxPackagesChars)
            {
                lines.Add($"-# …and {packages.Count - i} more");
                break;
            }
            lines.Add(line);
            length += line.Length + 1;
        }
        return lines;
    }

    private static string PackageLine(PackageChange package)
    {
        var name = $"`{package.Name}`";
        var line = package.Kind switch
        {
            PackageChangeKind.Updated when package.From != null && package.To != null => $"{name} {package.From} → {package.To}",
            PackageChangeKind.Added => $"{name} *new* {package.To}".TrimEnd(),
            PackageChangeKind.Deprecated => package.Note is { Length: > 0 } ? $"{name} *deprecated* — {package.Note}" : $"{name} *deprecated*",
            PackageChangeKind.Removed => $"{name} *removed*",
            _ => name,
        };
        return Short(line, PackageLineMaxLength);
    }

    private static List<List<string>> Pack(string header, List<List<string>> blocks)
    {
        var messages = new List<List<string>> { new() { header } };
        var length = header.Length;

        void StartMessage()
        {
            messages.Add([]);
            length = 0;
        }

        foreach (var lines in blocks)
        {
            var text = string.Join('\n', lines);
            if (text.Length <= MessageBudget)
            {
                if (length + text.Length > MessageBudget)
                    StartMessage();
                messages[^1].Add(text);
                length += text.Length;
                continue;
            }

            // A block bigger than a message: split between lines, repeating the heading. It starts in the
            // current message unless that has little room left.
            if (MessageBudget - length < MinSplitRoom)
                StartMessage();
            var heading = lines[0];
            var chunk = new List<string> { heading };
            var chunkLength = heading.Length;
            foreach (var line in lines.Skip(1))
            {
                if (length + chunkLength + line.Length + 1 > MessageBudget && chunk.Count > 1)
                {
                    messages[^1].Add(string.Join('\n', chunk));
                    StartMessage();
                    chunk = [heading + " (continued)"];
                    chunkLength = chunk[0].Length;
                }
                chunk.Add(line);
                chunkLength += line.Length + 1;
            }
            messages[^1].Add(string.Join('\n', chunk));
            length += chunkLength;
        }
        return messages;
    }

    // Last resort when even collapsed sections need too many messages: keep the first messages and point to the notes.
    private static IReadOnlyList<ReleaseMessage> Truncate(List<List<string>> messages, ReleaseSummary summary)
    {
        var kept = messages.Take(MaxMessages).Select(m => m.ToList()).ToList();
        var note = $"-# More in the [full release notes](<{summary.Latest.PageUrl}>)";
        var last = kept[^1];
        while (last.Count > 1 && last.Sum(b => b.Length) + note.Length > MessageBudget)
            last.RemoveAt(last.Count - 1);
        last.Add(note);
        return kept.Select(m => new ReleaseMessage(m)).ToList();
    }

    /// <summary>Cuts text to at most <paramref name="max"/> characters, ending with an ellipsis when cut.</summary>
    private static string Short(string text, int max = EntryMaxLength)
    {
        text = Safe(text);
        if (text.Length <= max)
            return text;
        var cut = max - 1;
        if (char.IsHighSurrogate(text[cut - 1]))
            cut--;
        var shortened = text[..cut].TrimEnd();
        // An unclosed code span would swallow the rest of the line.
        if (shortened.Count(c => c == '`') % 2 == 1)
            shortened += "`";
        return shortened + "…";
    }

    // Release notes are untrusted text: keep them from pinging.
    private static string Safe(string text) => text
        .Replace("@everyone", "@\u200Beveryone", StringComparison.OrdinalIgnoreCase)
        .Replace("@here", "@\u200Bhere", StringComparison.OrdinalIgnoreCase)
        .Replace("<@", "<\u200B@", StringComparison.Ordinal);
}
