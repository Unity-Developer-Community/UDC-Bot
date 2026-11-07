using System.Net;
using System.Text.RegularExpressions;

namespace DiscordBot.Domain.UnityNews;

/// <summary>
/// Parses the release notes markdown Unity publishes for every Editor release. Only the delta block is
/// read (<c>New X Entries since Y</c> for alpha and beta, <c>X Release Notes</c> for LTS and supported)
/// plus the known issues; the cumulative <c>Preview of Final X Release Notes</c> of pre-releases is ignored.
/// </summary>
public static partial class ReleaseNotesMarkdownParser
{
    private static readonly string[] SectionOrder = ["Features", "Improvements", "API Changes", "Changes", "Fixes"];
    private const string IssueRef = @"(?:\[[A-Z][A-Z0-9]*-\d+\]\([^)]*\)|[A-Z][A-Z0-9]*-\d+)";

    [GeneratedRegex(@"^(#{3,4})\s+(.*?)\s*$")]
    private static partial Regex HeadingPattern();

    [GeneratedRegex(@"^\*\*(.+?)\*\*\s*$")]
    private static partial Regex BoldHeadingPattern();

    [GeneratedRegex(@"Entries since (\S+)")]
    private static partial Regex SincePattern();

    // "([UUM-1](url))", "(UUM-1)" and lists of them.
    [GeneratedRegex(@"\(\s*" + IssueRef + @"(?:\s*[,;]\s*" + IssueRef + @")*\s*\)")]
    private static partial Regex IssueRefsPattern();

    [GeneratedRegex(@"\*(?:First seen|Fixed) in [^*]*\*")]
    private static partial Regex SeenFixedPattern();

    [GeneratedRegex(@"\[([^\]]*)\]\([^)]*\)")]
    private static partial Regex LinkPattern();

    [GeneratedRegex(@"\\([\\`*_{}\[\]()#+\-.!|~<>])")]
    private static partial Regex EscapePattern();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespacePattern();

    [GeneratedRegex(@"^([^:]{0,40}):\s+(.*)$", RegexOptions.Singleline)]
    private static partial Regex AreaPattern();

    [GeneratedRegex(@"^(Added|Changed|Removed|Obsoleted|Deprecated):\s*(.*)$", RegexOptions.Singleline)]
    private static partial Regex ApiPrefixPattern();

    [GeneratedRegex(@"^(\S+?):\s*(\S+)\s+to\s+(\S+)$")]
    private static partial Regex UpdatedPackagePattern();

    [GeneratedRegex(@"^([^@\s]+)@(\S+)$")]
    private static partial Regex AddedPackagePattern();

    [GeneratedRegex(@"^(\S+)\s+-\s+(.*)$", RegexOptions.Singleline)]
    private static partial Regex DeprecatedPackagePattern();

    private enum Mode { Ignore, KnownIssues, Delta }

    private sealed class Bullet(string? section, string? subHeading, string text)
    {
        public string? Section { get; } = section;
        public string? SubHeading { get; } = subHeading;
        public string Text { get; set; } = text;
    }

    public static ReleaseNotes Parse(string markdown)
    {
        string? since = null;
        var mode = Mode.Ignore;
        var deltaSeen = false;
        string? h4 = null;
        string? subHeading = null;
        Bullet? current = null;

        var knownIssues = new List<Bullet>();
        var entries = new List<Bullet>();
        var packages = new List<Bullet>();

        foreach (var line in markdown.Split('\n').Select(l => l.TrimEnd('\r')))
        {
            var heading = HeadingPattern().Match(line);
            if (heading.Success)
            {
                var title = heading.Groups[2].Value;
                current = null;
                subHeading = null;
                if (heading.Groups[1].Length == 4)
                {
                    h4 = title;
                    continue;
                }

                h4 = null;
                if (title.StartsWith("Known Issues", StringComparison.OrdinalIgnoreCase))
                    mode = Mode.KnownIssues;
                else if (!deltaSeen && IsDeltaHeading(title))
                {
                    deltaSeen = true;
                    mode = Mode.Delta;
                    var match = SincePattern().Match(title);
                    since = match.Success ? match.Groups[1].Value : null;
                }
                else
                    mode = Mode.Ignore;
                continue;
            }

            if (mode == Mode.Ignore)
                continue;

            var bold = BoldHeadingPattern().Match(line);
            if (bold.Success && mode == Mode.Delta)
            {
                subHeading = bold.Groups[1].Value;
                current = null;
                continue;
            }

            if (line.StartsWith("- ", StringComparison.Ordinal))
            {
                current = null;
                if (mode == Mode.KnownIssues)
                {
                    // Known issues are bullets directly under the "###" heading.
                    if (h4 != null) continue;
                    current = new Bullet(null, null, line[2..].Trim());
                    knownIssues.Add(current);
                }
                else if (h4 != null)
                {
                    current = new Bullet(h4, subHeading, line[2..].Trim());
                    (IsPackageSection(h4) ? packages : entries).Add(current);
                }
                continue;
            }

            // Continuation of the previous bullet: issue links, "First seen in" notes.
            if (current != null && !string.IsNullOrWhiteSpace(line))
                current.Text += " " + line.Trim();
        }

        var sections = entries
            .GroupBy(b => b.Section!, StringComparer.OrdinalIgnoreCase)
            .Select(g => new NoteSection(g.Key, g.Select(b => ToEntry(b.Text, g.Key)).Where(e => e.Text.Length > 0).ToList()))
            .Where(s => s.Entries.Count > 0)
            .OrderBy(s => OrderIndex(s.Name))
            .ToList();

        var known = new KnownIssues(knownIssues.Count, knownIssues.Count(k => k.Text.Contains("Fixed in", StringComparison.OrdinalIgnoreCase)));
        return new ReleaseNotes(since, sections, packages.Select(ToPackage).OfType<PackageChange>().ToList(), known);
    }

    private static bool IsDeltaHeading(string title) =>
        !title.StartsWith("Preview of Final", StringComparison.OrdinalIgnoreCase)
        && (title.Contains("Entries since", StringComparison.Ordinal) || title.EndsWith("Release Notes", StringComparison.OrdinalIgnoreCase));

    private static bool IsPackageSection(string h4) => h4.Contains("package", StringComparison.OrdinalIgnoreCase);

    // Known sections first in their usual order, unknown ones after them (the sort is stable).
    internal static int OrderIndex(string name)
    {
        var index = Array.FindIndex(SectionOrder, n => n.Equals(name, StringComparison.OrdinalIgnoreCase));
        return index < 0 ? SectionOrder.Length : index;
    }

    /// <summary>Removes issue references, "First seen in" notes, links and markdown escapes from a bullet.</summary>
    public static string Clean(string raw)
    {
        var text = WebUtility.HtmlDecode(raw).Replace("<br>", " ", StringComparison.OrdinalIgnoreCase);
        text = IssueRefsPattern().Replace(text, " ");
        text = SeenFixedPattern().Replace(text, " ");
        text = LinkPattern().Replace(text, "$1");
        text = EscapePattern().Replace(text, "$1");
        return WhitespacePattern().Replace(text, " ").Trim();
    }

    private static NoteEntry ToEntry(string raw, string section)
    {
        var text = Clean(raw);
        var area = "Other";
        var match = AreaPattern().Match(text);
        if (match.Success)
        {
            if (match.Groups[1].Value.Trim().Length > 0)
                area = match.Groups[1].Value.Trim();
            text = match.Groups[2].Value;
        }

        if (section.Equals("API Changes", StringComparison.OrdinalIgnoreCase))
        {
            var api = ApiPrefixPattern().Match(text);
            if (api.Success)
            {
                var verb = api.Groups[1].Value;
                var rest = api.Groups[2].Value;
                // "Added: Added X" already says it.
                text = rest.StartsWith(verb, StringComparison.OrdinalIgnoreCase) ? rest : $"{verb} · {rest}";
            }
        }
        return new NoteEntry(area, text);
    }

    private static PackageChange? ToPackage(Bullet bullet)
    {
        var text = Clean(bullet.Text);
        var sub = bullet.SubHeading ?? string.Empty;
        var kind = sub.Contains("added", StringComparison.OrdinalIgnoreCase) ? PackageChangeKind.Added
            : sub.Contains("deprecated", StringComparison.OrdinalIgnoreCase) ? PackageChangeKind.Deprecated
            : sub.Contains("removed", StringComparison.OrdinalIgnoreCase) ? PackageChangeKind.Removed
            : PackageChangeKind.Updated;

        var updated = UpdatedPackagePattern().Match(text);
        if (updated.Success)
            return new PackageChange(updated.Groups[1].Value, PackageChangeKind.Updated, updated.Groups[2].Value, updated.Groups[3].Value);

        var added = AddedPackagePattern().Match(text);
        if (added.Success)
            return new PackageChange(added.Groups[1].Value, kind == PackageChangeKind.Updated ? PackageChangeKind.Added : kind, null, added.Groups[2].Value);

        var deprecated = DeprecatedPackagePattern().Match(text);
        if (deprecated.Success)
            return new PackageChange(deprecated.Groups[1].Value, kind == PackageChangeKind.Updated ? PackageChangeKind.Deprecated : kind, null, null, deprecated.Groups[2].Value.Trim('"', ' '));

        var name = text.Split(' ', 2)[0];
        return name.Length == 0 ? null : new PackageChange(name, kind, null, null);
    }
}
