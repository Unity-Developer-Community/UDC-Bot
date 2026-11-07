namespace DiscordBot.Domain.UnityNews;

/// <summary>What the bot remembers about the releases it posted for one stream (persisted in <c>feeds.json</c>).</summary>
public class ReleaseStreamState
{
    /// <summary>False until the first check of the stream has recorded the existing releases without posting them.</summary>
    public bool Seeded { get; set; }

    /// <summary>Release date of the newest release present when the stream was seeded; the starting point of lines first seen later.</summary>
    public DateTimeOffset SeededThrough { get; set; }

    /// <summary>Per release line (<c>6000.3</c>).</summary>
    public Dictionary<string, ReleaseLineState> Lines { get; set; } = new();
}

public class ReleaseLineState
{
    /// <summary>Release date of the newest release posted (or seeded) for the line; later releases are pending.</summary>
    public DateTimeOffset LastReleaseDate { get; set; }

    public string LastVersion { get; set; } = "";

    /// <summary>When the bot last posted this line (UTC); <see cref="DateTimeOffset.MinValue"/> when never.</summary>
    public DateTimeOffset LastPostUtc { get; set; } = DateTimeOffset.MinValue;
}

/// <summary>One post to make: every pending release of a line, oldest to newest.</summary>
public sealed record ReleasePost(string Line, IReadOnlyList<UnityRelease> Releases)
{
    public UnityRelease Latest => Releases[^1];
}

/// <summary>The decision for one stream: either seed its state or post <see cref="Posts"/> (possibly none).</summary>
public sealed record ScheduleResult(bool Seed, IReadOnlyList<ReleasePost> Posts)
{
    public static readonly ScheduleResult Nothing = new(false, []);
}

/// <summary>
/// Decides what to post for a Unity release stream. LTS and Supported post the newest pending releases of a line at most once
/// per UTC day; Beta (Friday) and Alpha (Thursday) post a weekly digest from 12:00 UTC on that day. A stream seen for the first
/// time is only recorded (seeded), so nothing posts retroactively.
/// </summary>
public static class ReleaseSchedule
{
    /// <summary>A digest merges at most this many releases (the newest); older pending ones are skipped.</summary>
    public const int MaxDigestReleases = 10;

    public static readonly TimeSpan WeeklyPostTime = TimeSpan.FromHours(12);

    public static DayOfWeek? WeeklyDay(UnityStream stream) => stream switch
    {
        UnityStream.Alpha => DayOfWeek.Thursday,
        UnityStream.Beta => DayOfWeek.Friday,
        _ => null,
    };

    /// <summary>The most recent moment at or before <paramref name="now"/> that is <paramref name="day"/> at <see cref="WeeklyPostTime"/> (UTC).</summary>
    public static DateTimeOffset LatestWindowStart(DayOfWeek day, DateTimeOffset now)
    {
        var utc = now.ToUniversalTime();
        var daysBack = ((int)utc.DayOfWeek - (int)day + 7) % 7;
        var start = new DateTimeOffset(utc.Date, TimeSpan.Zero).AddDays(-daysBack) + WeeklyPostTime;
        return start > utc ? start.AddDays(-7) : start;
    }

    /// <param name="fetched">The releases of <paramref name="stream"/>, in any order.</param>
    public static ScheduleResult Plan(UnityStream stream, ReleaseStreamState state, IReadOnlyList<UnityRelease> fetched, DateTimeOffset now)
    {
        var releases = fetched.Where(r => r.Stream == stream).ToList();
        if (releases.Count == 0)
            return ScheduleResult.Nothing;
        if (!state.Seeded)
            return new ScheduleResult(true, []);

        var weeklyDay = WeeklyDay(stream);
        var utc = now.ToUniversalTime();
        var posts = new List<ReleasePost>();

        foreach (var line in releases.GroupBy(r => r.Line))
        {
            state.Lines.TryGetValue(line.Key, out var lineState);
            var watermark = lineState?.LastReleaseDate ?? state.SeededThrough;
            var pending = line.Where(r => r.ReleaseDate > watermark)
                .OrderBy(r => r.ReleaseDate).ThenBy(r => r.Version, StringComparer.Ordinal)
                .ToList();
            if (pending.Count == 0)
                continue;

            var lastPost = lineState?.LastPostUtc ?? DateTimeOffset.MinValue;
            var due = weeklyDay is { } day
                ? lastPost < LatestWindowStart(day, utc)
                : lastPost.ToUniversalTime().Date < utc.Date;
            if (!due)
                continue;

            if (pending.Count > MaxDigestReleases)
                pending = pending.GetRange(pending.Count - MaxDigestReleases, MaxDigestReleases);
            posts.Add(new ReleasePost(line.Key, pending));
        }

        return new ScheduleResult(false, posts.OrderBy(p => p.Latest.ReleaseDate).ToList());
    }

    /// <summary>Records the current releases as handled without posting them.</summary>
    public static void Seed(UnityStream stream, ReleaseStreamState state, IReadOnlyList<UnityRelease> fetched, DateTimeOffset now)
    {
        var releases = fetched.Where(r => r.Stream == stream).ToList();
        if (releases.Count == 0)
            return;

        state.Seeded = true;
        state.SeededThrough = releases.Max(r => r.ReleaseDate);
        // A weekly stream waits for its next window; the daily streams may post a new release the same day.
        var lastPost = WeeklyDay(stream) != null ? now.ToUniversalTime() : DateTimeOffset.MinValue;
        foreach (var line in releases.GroupBy(r => r.Line))
        {
            var newest = line.OrderBy(r => r.ReleaseDate).ThenBy(r => r.Version, StringComparer.Ordinal).Last();
            state.Lines[line.Key] = new ReleaseLineState
            {
                LastReleaseDate = newest.ReleaseDate,
                LastVersion = newest.Version,
                LastPostUtc = lastPost,
            };
        }
    }

    /// <summary>Records that <paramref name="post"/> was posted at <paramref name="now"/>.</summary>
    public static void MarkPosted(ReleaseStreamState state, ReleasePost post, DateTimeOffset now)
    {
        state.Lines[post.Line] = new ReleaseLineState
        {
            LastReleaseDate = post.Latest.ReleaseDate,
            LastVersion = post.Latest.Version,
            LastPostUtc = now.ToUniversalTime(),
        };
    }
}
