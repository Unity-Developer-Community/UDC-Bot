using DiscordBot.Modules.Profiles;

namespace DiscordBot.Tests.Modules.Profiles;

public class RankEmbedFormatterTests
{
    private static string Build(params string[] names) =>
        RankEmbedFormatter.BuildDescription(
            names.Select((name, index) => (name, index + 1)).ToList(), "Karma");

    [Fact]
    public void BuildDescription_NoRows_ReturnsEmpty()
    {
        Assert.Equal(string.Empty, RankEmbedFormatter.BuildDescription([], "Karma"));
    }

    [Fact]
    public void BuildDescription_SingleRow_FormatsWithoutPadding()
    {
        Assert.Equal("`1.` **`Pierre`** `Karma: 1`\n", Build("Pierre"));
    }

    [Fact]
    public void BuildDescription_PadsRanksForTwoDigitCounts()
    {
        var names = Enumerable.Range(1, 10).Select(i => $"User{i}").ToArray();

        var lines = Build(names).Split('\n', StringSplitOptions.RemoveEmptyEntries);

        Assert.Equal(10, lines.Length);
        Assert.StartsWith("` 1.`", lines[0]);
        Assert.StartsWith("`10.`", lines[9]);
    }

    [Fact]
    public void BuildDescription_DoesNotPadRanksForSmallCounts()
    {
        var lines = Build("A", "B", "C").Split('\n', StringSplitOptions.RemoveEmptyEntries);

        Assert.StartsWith("`1.`", lines[0]);
        Assert.StartsWith("`3.`", lines[2]);
    }

    [Fact]
    public void BuildDescription_PadsShorterNamesToTheLongest()
    {
        var lines = Build("Bo", "Alexander").Split('\n', StringSplitOptions.RemoveEmptyEntries);

        // "Bo" is padded out to the nine characters of "Alexander".
        Assert.Contains($"`Bo{new string('\u2000', 7)}`", lines[0]);
        Assert.Contains("`Alexander`", lines[1]);
    }

    [Fact]
    public void BuildDescription_UsesValueAndLabelPerRow()
    {
        var rows = new List<(string Name, int Value)> { ("Bo", 12), ("Al", 7) };

        var description = RankEmbedFormatter.BuildDescription(rows, "Weekly Karma");

        Assert.Contains("`Weekly Karma: 12`", description);
        Assert.Contains("`Weekly Karma: 7`", description);
    }

    [Fact]
    public void BuildDescription_OrdersRowsAsGiven()
    {
        var rows = new List<(string Name, int Value)> { ("First", 99), ("Second", 1) };

        var lines = RankEmbedFormatter.BuildDescription(rows, "Karma")
            .Split('\n', StringSplitOptions.RemoveEmptyEntries);

        Assert.StartsWith("`1.`", lines[0]);
        Assert.Contains("First", lines[0]);
        Assert.Contains("Second", lines[1]);
    }
}
