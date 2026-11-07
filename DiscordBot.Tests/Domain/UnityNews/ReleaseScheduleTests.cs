using DiscordBot.Domain.UnityNews;

namespace DiscordBot.Tests.Domain.UnityNews;

public class ReleaseScheduleTests
{
    // 2026-11-05 is a Thursday, 2026-11-06 a Friday.
    private static DateTimeOffset Utc(int month, int day, int hour = 12, int minute = 0) =>
        new(2026, month, day, hour, minute, 0, TimeSpan.Zero);

    private static UnityRelease Rel(string version, UnityStream stream, DateTimeOffset date) =>
        UnityNewsFixtures.Release(version, stream, date);

    private static ReleaseStreamState Seeded(UnityStream stream, DateTimeOffset now, params UnityRelease[] existing)
    {
        var state = new ReleaseStreamState();
        ReleaseSchedule.Seed(stream, state, existing, now);
        return state;
    }

    [Fact]
    public void Unseeded_StreamSeedsAndPostsNothing()
    {
        var releases = new[] { Rel("6000.3.28f1", UnityStream.Lts, Utc(11, 3, 16, 15)) };
        var result = ReleaseSchedule.Plan(UnityStream.Lts, new ReleaseStreamState(), releases, Utc(11, 7));

        Assert.True(result.Seed);
        Assert.Empty(result.Posts);
    }

    [Fact]
    public void EmptyFetch_DoesNotSeed()
    {
        var result = ReleaseSchedule.Plan(UnityStream.Lts, new ReleaseStreamState(), [], Utc(11, 7));

        Assert.False(result.Seed);
        Assert.Empty(result.Posts);
    }

    [Fact]
    public void Seed_RecordsNewestPerLineWithoutBlockingSameDayLts()
    {
        var existing = new[]
        {
            Rel("6000.3.27f2", UnityStream.Lts, Utc(11, 3, 16, 15)),
            Rel("6000.3.26f1", UnityStream.Lts, Utc(10, 27)),
            Rel("6000.0.85f2", UnityStream.Lts, Utc(11, 3, 16, 15)),
        };
        var state = Seeded(UnityStream.Lts, Utc(11, 5, 9), existing);

        Assert.True(state.Seeded);
        Assert.Equal("6000.3.27f2", state.Lines["6000.3"].LastVersion);
        Assert.Equal("6000.0.85f2", state.Lines["6000.0"].LastVersion);

        var later = existing.Append(Rel("6000.3.28f1", UnityStream.Lts, Utc(11, 5, 14))).ToList();
        var result = ReleaseSchedule.Plan(UnityStream.Lts, state, later, Utc(11, 5, 15));
        Assert.Equal(["6000.3.28f1"], result.Posts.Single().Releases.Select(r => r.Version));
    }

    [Fact]
    public void Lts_PostsNewReleaseOfLine_AndTwoLinesPostSeparately()
    {
        var old = new[]
        {
            Rel("6000.3.27f2", UnityStream.Lts, Utc(10, 30)),
            Rel("6000.0.84f1", UnityStream.Lts, Utc(10, 30)),
        };
        var state = Seeded(UnityStream.Lts, Utc(11, 1), old);
        var fetched = old.Concat([
            Rel("6000.3.28f1", UnityStream.Lts, Utc(11, 3, 16, 15)),
            Rel("6000.0.85f2", UnityStream.Lts, Utc(11, 3, 16, 15)),
        ]).ToList();

        var result = ReleaseSchedule.Plan(UnityStream.Lts, state, fetched, Utc(11, 3, 16, 20));

        Assert.Equal(2, result.Posts.Count);
        Assert.Equal(["6000.0", "6000.3"], result.Posts.Select(p => p.Line).Order());
        Assert.All(result.Posts, p => Assert.Single(p.Releases));
    }

    [Fact]
    public void Lts_AtMostOnePostPerLinePerUtcDay_ThenMergesPending()
    {
        var first = Rel("6000.3.28f1", UnityStream.Lts, Utc(11, 3, 8));
        var state = Seeded(UnityStream.Lts, Utc(11, 1), Rel("6000.3.27f2", UnityStream.Lts, Utc(10, 30)));
        ReleaseSchedule.MarkPosted(state, new ReleasePost("6000.3", [first]), Utc(11, 3, 8, 5));

        var respin = Rel("6000.3.28f2", UnityStream.Lts, Utc(11, 3, 18));
        var again = Rel("6000.3.29f1", UnityStream.Lts, Utc(11, 4, 1));
        var fetched = new[] { first, respin };

        Assert.Empty(ReleaseSchedule.Plan(UnityStream.Lts, state, fetched, Utc(11, 3, 18, 5)).Posts);

        var nextDay = ReleaseSchedule.Plan(UnityStream.Lts, state, [first, respin, again], Utc(11, 4, 2)).Posts.Single();
        Assert.Equal(["6000.3.28f2", "6000.3.29f1"], nextDay.Releases.Select(r => r.Version));
    }

    [Fact]
    public void Supported_BehavesLikeLts()
    {
        var state = Seeded(UnityStream.Supported, Utc(11, 1), Rel("6000.6.7f1", UnityStream.Supported, Utc(10, 29)));
        var fresh = Rel("6000.6.8f1", UnityStream.Supported, Utc(11, 4, 10));

        var post = ReleaseSchedule.Plan(UnityStream.Supported, state, [fresh, Rel("6000.6.7f1", UnityStream.Supported, Utc(10, 29))], Utc(11, 4, 10, 10)).Posts.Single();

        Assert.Equal("6000.6.8f1", post.Latest.Version);
    }

    [Fact]
    public void Beta_WaitsForFridayNoon_ThenPostsDigestOfEveryPendingRelease()
    {
        var b5 = Rel("6000.7.0b5", UnityStream.Beta, Utc(10, 22, 10));
        var state = Seeded(UnityStream.Beta, Utc(10, 30), b5);
        var b6 = Rel("6000.7.0b6", UnityStream.Beta, Utc(11, 2, 10));
        var b7 = Rel("6000.7.0b7", UnityStream.Beta, Utc(11, 4, 10));
        var b8 = Rel("6000.7.0b8", UnityStream.Beta, Utc(11, 5, 10));
        var fetched = new[] { b8, b7, b6, b5 };

        Assert.Empty(ReleaseSchedule.Plan(UnityStream.Beta, state, fetched, Utc(11, 5, 15)).Posts); // Thursday
        Assert.Empty(ReleaseSchedule.Plan(UnityStream.Beta, state, fetched, Utc(11, 6, 11, 59)).Posts); // Friday before noon

        var post = ReleaseSchedule.Plan(UnityStream.Beta, state, fetched, Utc(11, 6, 12)).Posts.Single();
        Assert.Equal(["6000.7.0b6", "6000.7.0b7", "6000.7.0b8"], post.Releases.Select(r => r.Version));
    }

    [Fact]
    public void Beta_PostsOncePerWindow_AndCatchesUpAfterMissingTheFriday()
    {
        var b5 = Rel("6000.7.0b5", UnityStream.Beta, Utc(10, 22, 10));
        var state = Seeded(UnityStream.Beta, Utc(10, 30), b5);
        var b6 = Rel("6000.7.0b6", UnityStream.Beta, Utc(11, 2, 10));
        var fetched = new[] { b6, b5 };

        // The bot was down on Friday 11-06 and is back on Sunday: the missed window still posts.
        var post = ReleaseSchedule.Plan(UnityStream.Beta, state, fetched, Utc(11, 8, 9)).Posts.Single();
        ReleaseSchedule.MarkPosted(state, post, Utc(11, 8, 9));

        var b7 = Rel("6000.7.0b7", UnityStream.Beta, Utc(11, 8, 20));
        Assert.Empty(ReleaseSchedule.Plan(UnityStream.Beta, state, [b7, b6, b5], Utc(11, 9)).Posts);
        Assert.Empty(ReleaseSchedule.Plan(UnityStream.Beta, state, [b7, b6, b5], Utc(11, 12, 12)).Posts); // Thursday
        Assert.Single(ReleaseSchedule.Plan(UnityStream.Beta, state, [b7, b6, b5], Utc(11, 13, 12)).Posts);
    }

    [Fact]
    public void Beta_NothingPending_PostsNothing()
    {
        var b8 = Rel("6000.7.0b8", UnityStream.Beta, Utc(11, 5, 10));
        var state = Seeded(UnityStream.Beta, Utc(11, 5, 11), b8);

        Assert.Empty(ReleaseSchedule.Plan(UnityStream.Beta, state, [b8], Utc(11, 6, 13)).Posts);
    }

    [Fact]
    public void Alpha_PostsOnThursdayNoon()
    {
        var a12 = Rel("7000.0.0a12", UnityStream.Alpha, Utc(10, 20));
        var state = Seeded(UnityStream.Alpha, Utc(10, 30), a12);
        var a16 = Rel("7000.0.0a16", UnityStream.Alpha, Utc(11, 3));
        var fetched = new[] { a16, a12 };

        Assert.Empty(ReleaseSchedule.Plan(UnityStream.Alpha, state, fetched, Utc(11, 5, 11)).Posts);
        Assert.Single(ReleaseSchedule.Plan(UnityStream.Alpha, state, fetched, Utc(11, 5, 12)).Posts);
    }

    [Fact]
    public void Digest_KeepsTheNewestReleasesOnly()
    {
        var old = Rel("6000.7.0b1", UnityStream.Beta, Utc(9, 1));
        var state = Seeded(UnityStream.Beta, Utc(9, 2), old);
        var many = Enumerable.Range(2, 14)
            .Select(i => Rel($"6000.7.0b{i}", UnityStream.Beta, Utc(9, 2).AddDays(i)))
            .ToList();

        var post = ReleaseSchedule.Plan(UnityStream.Beta, state, many, Utc(11, 6, 12)).Posts.Single();

        Assert.Equal(ReleaseSchedule.MaxDigestReleases, post.Releases.Count);
        Assert.Equal("6000.7.0b15", post.Latest.Version);
    }

    [Fact]
    public void NewLineAfterSeeding_PostsItsFirstRelease_ButNotOlderOnes()
    {
        var state = Seeded(UnityStream.Lts, Utc(11, 1), Rel("6000.3.27f2", UnityStream.Lts, Utc(10, 30)));
        var fetched = new[]
        {
            Rel("6000.4.1f1", UnityStream.Lts, Utc(11, 4)),
            Rel("6000.3.27f2", UnityStream.Lts, Utc(10, 30)),
            Rel("6000.0.80f1", UnityStream.Lts, Utc(9, 1)), // old line the first fetch did not cover
        };

        var result = ReleaseSchedule.Plan(UnityStream.Lts, state, fetched, Utc(11, 4, 12));

        Assert.Equal("6000.4", result.Posts.Single().Line);
    }

    [Fact]
    public void ReleasesOfOtherStreamsAreIgnored()
    {
        var state = Seeded(UnityStream.Lts, Utc(11, 1), Rel("6000.3.27f2", UnityStream.Lts, Utc(10, 30)));
        var result = ReleaseSchedule.Plan(UnityStream.Lts, state, [Rel("6000.7.0b8", UnityStream.Beta, Utc(11, 5))], Utc(11, 6));

        Assert.Empty(result.Posts);
    }

    [Theory]
    [InlineData(11, 6, 12, 0, 11, 6)]   // Friday at noon: starts now
    [InlineData(11, 6, 11, 59, 10, 30)] // Friday before noon: previous Friday
    [InlineData(11, 8, 0, 0, 11, 6)]    // Sunday
    [InlineData(11, 12, 23, 0, 11, 6)]  // Thursday night: still last Friday's window
    public void LatestWindowStart_IsTheMostRecentFridayNoon(int month, int day, int hour, int minute, int expectedMonth, int expectedDay)
    {
        var start = ReleaseSchedule.LatestWindowStart(DayOfWeek.Friday, Utc(month, day, hour, minute));

        Assert.Equal(Utc(expectedMonth, expectedDay), start);
    }
}
