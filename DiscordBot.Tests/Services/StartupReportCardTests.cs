using Discord;
using DiscordBot.Services.Logging;
using DiscordBot.Services.Server;
using static DiscordBot.Services.Logging.StartupReportCard;

namespace DiscordBot.Tests.Services;

public class StartupReportCardTests
{
    private static readonly DateTimeOffset Start = new(2026, 11, 7, 12, 0, 0, TimeSpan.Zero);

    private static State Finished(params Entry[] entries) =>
        new(Start, Start.AddSeconds(4), Start.AddSeconds(9.5), entries, [], false);

    private static Entry At(double seconds, ExtendedLogSeverity severity, string text) =>
        new(TimeSpan.FromSeconds(seconds), severity, text);

    private static string Text(MessageComponent card) => AuditLogCardsTests.Text(card);

    private static Color? Accent(MessageComponent card) =>
        card.Components.OfType<ContainerComponent>().Single().AccentColor;

    [Theory]
    [InlineData(3.214, "+03.214")]
    [InlineData(0, "+00.000")]
    [InlineData(-1, "+00.000")]
    [InlineData(75.5, "+1:15.500")]
    public void Offset_Formats(double seconds, string expected)
    {
        Assert.Equal(expected, Offset(TimeSpan.FromSeconds(seconds)));
    }

    [Fact]
    public void Build_Finished_ShowsTimesDurationAndLines()
    {
        var text = Text(Build(Finished(At(5.1, ExtendedLogSeverity.Positive, "DatabaseService: Connected"))));

        Assert.Contains("✅ Bot started", text);
        Assert.Contains($"Started <t:{Start.ToUnixTimeSeconds()}:R> (<t:{Start.ToUnixTimeSeconds()}:T>)", text);
        Assert.Contains("Gateway ready `+04.000`", text);
        Assert.Contains($"Finished <t:{Start.AddSeconds(9.5).ToUnixTimeSeconds()}:T> · took 9.5 s", text);
        Assert.Contains("`+05.100` ✅ DatabaseService: Connected", text);
        Assert.Contains("-# 1 line", text);
    }

    [Theory]
    [InlineData(ExtendedLogSeverity.Info, "✅ Bot started", "•")]
    [InlineData(ExtendedLogSeverity.Positive, "✅ Bot started", "✅")]
    [InlineData(ExtendedLogSeverity.LowWarning, "⚠️ Bot started with warnings", "⚠️")]
    [InlineData(ExtendedLogSeverity.Warning, "⚠️ Bot started with warnings", "⚠️")]
    [InlineData(ExtendedLogSeverity.Error, "❌ Bot started with errors", "❌")]
    [InlineData(ExtendedLogSeverity.Critical, "❌ Bot started with errors", "❌")]
    public void Build_WorstSeverity_SetsHeaderMarkerAndAccent(ExtendedLogSeverity worst, string header, string marker)
    {
        var card = Build(Finished(At(1, ExtendedLogSeverity.Info, "plain"), At(2, worst, "worst")));
        var text = Text(card);

        Assert.Contains($"### {header}", text);
        Assert.Contains($"`+02.000` {marker} worst", text);
        var expected = worst switch
        {
            ExtendedLogSeverity.Error or ExtendedLogSeverity.Critical => ErrorColor,
            ExtendedLogSeverity.Warning or ExtendedLogSeverity.LowWarning => WarningColor,
            _ => CleanColor,
        };
        Assert.Equal(expected, Accent(card));
    }

    [Fact]
    public void Build_InProgress_ShowsStartingAndPendingSteps()
    {
        var card = Build(new State(Start, Start.AddSeconds(4), null, [], ["Commands", "Database"], false));
        var text = Text(card);

        Assert.Contains("### ⏳ Starting…", text);
        Assert.DoesNotContain("Finished", text);
        Assert.Contains("waiting for: Commands, Database", text);
        Assert.Equal(StartingColor, Accent(card));
    }

    [Fact]
    public void Build_TimedOut_WarnsWithPendingSteps()
    {
        var card = Build(new State(Start, Start.AddSeconds(4), Start.AddSeconds(60), [], ["Database"], true));
        var text = Text(card);

        Assert.Contains("### ⚠️ Bot started with warnings", text);
        Assert.Contains("Gave up waiting for: Database", text);
        Assert.Equal(WarningColor, Accent(card));
    }

    [Fact]
    public void LineText_MultiLine_KeepsFirstLine()
    {
        Assert.Equal("Failed to connect …", LineText("Failed to connect\nException: boom\n   at Foo()"));
    }

    [Fact]
    public void LineText_Long_CutToMaxLength()
    {
        var text = LineText(new string('a', 500));
        Assert.Equal(MaxLineLength, text.Length);
        Assert.EndsWith("…", text);
    }

    [Fact]
    public void Build_OverBudget_DropsInfoLinesFirst()
    {
        var entries = Enumerable.Range(0, 60)
            .Select(i => At(i, ExtendedLogSeverity.Info, $"info line {i} " + new string('x', 150)))
            .Append(At(61, ExtendedLogSeverity.Warning, "the warning"))
            .Append(At(62, ExtendedLogSeverity.Error, "the error"))
            .ToArray();

        var card = Build(Finished(entries));
        var text = Text(card);

        Assert.True(text.Length <= AuditCard.DefaultBudget, $"{text.Length} chars");
        Assert.Contains("the warning", text);
        Assert.Contains("the error", text);
        Assert.Contains("info line 0 ", text);
        Assert.DoesNotContain("info line 59 ", text);
        Assert.Matches(@"… \d+ more lines in the log file", text);
    }

    private static readonly MemorySample Sample = new(180 * 1024 * 1024L, 60 * 1024 * 1024L);

    [Fact]
    public void MemoryLine_EmptyWhileStarting()
    {
        Assert.Equal("", MemoryLine(new State(Start, Start, null, [], [], false, Sample)));
    }

    [Fact]
    public void MemoryLine_FirstSample_SaysWhenItMeasuresAgain()
    {
        var finished = Start.AddSeconds(10);
        var line = MemoryLine(new State(Start, Start, finished, [], [], false, Sample, null, finished.AddMinutes(1)));
        Assert.Equal($"Memory: 180 MB (GC heap 60 MB) · measuring again <t:{finished.AddMinutes(1).ToUnixTimeSeconds()}:R>", line);
    }

    [Fact]
    public void MemoryLine_SecondSample_ShowsChange()
    {
        var finished = Start.AddSeconds(10);
        var later = new MemorySample(190 * 1024 * 1024L, 60 * 1024 * 1024L);
        var line = MemoryLine(new State(Start, Start, finished, [], [], false, Sample, later, finished.AddMinutes(1)));
        Assert.Equal("Memory: 180 MB (GC heap 60 MB) · 1m later: 190 MB (+10 MB) · GC heap 60 MB (±0 MB)", line);
    }

    [Fact]
    public void Build_Finished_ShowsMemoryInHeader()
    {
        var card = Build(new State(Start, Start, Start.AddSeconds(10), [], [], false, Sample));
        Assert.Contains("Memory: 180 MB (GC heap 60 MB)", Text(card));
    }
}
