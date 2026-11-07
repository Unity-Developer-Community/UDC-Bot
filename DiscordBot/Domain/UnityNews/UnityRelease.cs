using System.Text.RegularExpressions;

namespace DiscordBot.Domain.UnityNews;

/// <summary>The release streams of Unity's Release API.</summary>
public enum UnityStream
{
    Alpha,
    Beta,
    Supported,
    Lts,
}

/// <summary>One Unity Editor release, for example <c>6000.3.28f1</c> on the LTS stream.</summary>
public sealed record UnityRelease(string Version, UnityStream Stream, DateTimeOffset ReleaseDate, string? NotesUrl, bool Recommended)
{
    /// <summary>The release line, for example <c>6000.3</c>.</summary>
    public string Line => UnityVersion.Line(Version);

    /// <summary>The marketing name of the line, for example <c>Unity 6.3</c>.</summary>
    public string DisplayLine => UnityVersion.DisplayLine(Version);

    public string StreamLabel => Stream switch
    {
        UnityStream.Alpha => "Alpha",
        UnityStream.Beta => "Beta",
        UnityStream.Supported => "Supported",
        _ => "LTS",
    };

    /// <summary>Short post title, for example <c>LTS · 6000.3.28f1</c>.</summary>
    public string Title => $"{StreamLabel} · {Version}";

    /// <summary>Version without the line for pre-releases (<c>b8</c>), the full version otherwise.</summary>
    public string ShortVersion => UnityVersion.Short(Version);

    /// <summary>The release page on unity.com.</summary>
    public string PageUrl => Stream switch
    {
        UnityStream.Alpha => $"https://unity.com/releases/editor/alpha/{Version}",
        UnityStream.Beta => $"https://unity.com/releases/editor/beta/{Version}",
        _ => $"https://unity.com/releases/editor/whats-new/{Version}",
    };
}

public static class UnityVersion
{
    private static readonly Regex VersionPattern = new(@"^(\d+)\.(\d+)\.(\d+)([a-z])(\d+)$", RegexOptions.Compiled);
    private static readonly Regex PreReleaseTail = new(@"[ab]\d+$", RegexOptions.Compiled);

    /// <summary><c>6000.3.28f1</c> becomes <c>6000.3</c>.</summary>
    public static string Line(string version)
    {
        var match = VersionPattern.Match(version);
        if (match.Success)
            return $"{match.Groups[1].Value}.{match.Groups[2].Value}";
        var parts = version.Split('.');
        return parts.Length >= 2 ? $"{parts[0]}.{parts[1]}" : version;
    }

    /// <summary><c>6000.3.28f1</c> becomes <c>Unity 6.3</c>, <c>2022.3.1f1</c> becomes <c>Unity 2022.3</c>.</summary>
    public static string DisplayLine(string version)
    {
        var line = Line(version).Split('.');
        if (line.Length == 2 && int.TryParse(line[0], out var major) && int.TryParse(line[1], out var minor) && major >= 6000)
            return $"Unity {major / 1000}.{minor}";
        return $"Unity {string.Join('.', line)}";
    }

    /// <summary><c>6000.7.0b8</c> becomes <c>b8</c>; other versions are returned as they are.</summary>
    public static string Short(string version)
    {
        var match = PreReleaseTail.Match(version);
        return match.Success ? match.Value : version;
    }
}
