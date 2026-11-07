using Discord;
using DiscordBot.Services.Logging;

namespace DiscordBot.Tests.Services;

public class StartupReportTests
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(5);

    private sealed class FakeChannel : IStartupReportChannel
    {
        private readonly object _lock = new();
        public TaskCompletionSource<ulong?>? FirstPost { get; init; }
        public bool EditSucceeds { get; init; } = true;
        public List<string> Posts { get; } = [];
        public List<string> Edits { get; } = [];
        public List<string> Texts { get; } = [];

        public Task<ulong?> PostAsync(MessageComponent card)
        {
            lock (_lock)
            {
                Posts.Add(AuditLogCardsTests.Text(card));
                if (Posts.Count == 1 && FirstPost != null)
                    return FirstPost.Task;
            }
            return Task.FromResult<ulong?>(100);
        }

        public Task<bool> EditAsync(ulong messageId, MessageComponent card)
        {
            lock (_lock)
                Edits.Add(AuditLogCardsTests.Text(card));
            return Task.FromResult(EditSucceeds);
        }

        public Task SendTextAsync(string text)
        {
            lock (_lock)
                Texts.Add(text);
            return Task.CompletedTask;
        }
    }

    private static StartupReport Report(FakeChannel channel, TimeSpan? cap = null, TimeSpan? debounce = null,
        TimeSpan? memoryFollowUp = null, Func<StartupReportCard.MemorySample>? memory = null) =>
        new(channel, cap ?? TimeSpan.FromMinutes(5), debounce ?? TimeSpan.FromMinutes(5), TimeProvider.System,
            memoryFollowUp ?? TimeSpan.FromMinutes(5), memory ?? (() => new StartupReportCard.MemorySample(100 << 20, 40 << 20)));

    private static void Begin(StartupReport report)
    {
        var now = DateTimeOffset.UtcNow;
        report.Begin(now.AddSeconds(-5), now);
    }

    [Fact]
    public async Task TryCapture_OnlyBetweenBeginAndFinish()
    {
        var channel = new FakeChannel();
        var report = Report(channel);

        Assert.False(report.TryCapture("before", ExtendedLogSeverity.Info));
        Begin(report);
        Assert.True(report.TryCapture("during", ExtendedLogSeverity.Positive));
        report.MarkEagerServicesResolved();
        await report.Completed.Task.WaitAsync(Wait);
        Assert.False(report.TryCapture("after", ExtendedLogSeverity.Info));

        Assert.Contains("⏳ Starting…", Assert.Single(channel.Posts));
        var final = Assert.Single(channel.Edits);
        Assert.Contains("✅ Bot started", final);
        Assert.Contains("during", final);
        Assert.DoesNotContain("after", final);
    }

    [Fact]
    public async Task Finish_WaitsForLastStep()
    {
        var channel = new FakeChannel();
        var report = Report(channel);
        Begin(report);
        var commands = report.BeginStep("Commands");
        var database = report.BeginStep("Database");

        report.MarkEagerServicesResolved();
        commands.Dispose();
        Assert.True(report.TryCapture("still starting", ExtendedLogSeverity.Info));

        database.Dispose();
        await report.Completed.Task.WaitAsync(Wait);
        Assert.False(report.TryCapture("done", ExtendedLogSeverity.Info));
        Assert.Contains("✅ Bot started", Assert.Single(channel.Edits));
    }

    [Fact]
    public async Task BeginStep_AfterFinish_IsNoOp()
    {
        var channel = new FakeChannel();
        var report = Report(channel);
        Begin(report);
        report.MarkEagerServicesResolved();
        await report.Completed.Task.WaitAsync(Wait);

        report.BeginStep("Late").Dispose();
        report.MarkEagerServicesResolved();

        Assert.Single(channel.Posts);
        Assert.Single(channel.Edits);
    }

    [Fact]
    public async Task Cap_FinishesWithPendingSteps()
    {
        var channel = new FakeChannel();
        var report = Report(channel, cap: TimeSpan.FromMilliseconds(50));
        Begin(report);
        report.BeginStep("Database");
        report.MarkEagerServicesResolved();

        await report.Completed.Task.WaitAsync(Wait);

        Assert.False(report.TryCapture("late", ExtendedLogSeverity.Info));
        var final = Assert.Single(channel.Edits);
        Assert.Contains("⚠️ Bot started with warnings", final);
        Assert.Contains("Gave up waiting for: Database", final);
    }

    [Fact]
    public async Task Error_EditsEarly_ThenFinalEdit()
    {
        var channel = new FakeChannel();
        var report = Report(channel, debounce: TimeSpan.FromMilliseconds(20));
        Begin(report);
        var step = report.BeginStep("Commands");
        report.MarkEagerServicesResolved();

        report.TryCapture("first failure", ExtendedLogSeverity.Error);
        report.TryCapture("second failure", ExtendedLogSeverity.Critical);
        var deadline = DateTime.UtcNow + Wait;
        while (channel.Edits.Count == 0 && DateTime.UtcNow < deadline)
            await Task.Delay(10);

        var early = Assert.Single(channel.Edits);
        Assert.Contains("⏳ Starting…", early);
        Assert.Contains("second failure", early);

        step.Dispose();
        await report.Completed.Task.WaitAsync(Wait);
        Assert.Equal(2, channel.Edits.Count);
        Assert.Contains("❌ Bot started with errors", channel.Edits[1]);
    }

    [Fact]
    public async Task MemoryFollowUp_EditsOnceMoreWithSecondSample()
    {
        var samples = new Queue<StartupReportCard.MemorySample>([new(100 << 20, 40 << 20), new(112 << 20, 38 << 20)]);
        var channel = new FakeChannel();
        var report = Report(channel, memoryFollowUp: TimeSpan.FromMilliseconds(30), memory: samples.Dequeue);
        Begin(report);
        report.MarkEagerServicesResolved();

        await report.FollowUpCompleted.Task.WaitAsync(Wait);

        Assert.Equal(2, channel.Edits.Count);
        Assert.Contains("Memory: 100 MB (GC heap 40 MB) · measuring again", channel.Edits[0]);
        Assert.Contains("Memory: 100 MB (GC heap 40 MB) · 0s later: 112 MB (+12 MB) · GC heap 38 MB (-2 MB)", channel.Edits[1]);
    }

    [Fact]
    public async Task FinalEditFails_PostsReportAsNewMessage()
    {
        var channel = new FakeChannel { EditSucceeds = false };
        var report = Report(channel);
        Begin(report);
        report.MarkEagerServicesResolved();

        await report.Completed.Task.WaitAsync(Wait);

        Assert.Equal(2, channel.Posts.Count);
        Assert.Contains("✅ Bot started", channel.Posts[1]);
    }

    [Fact]
    public async Task FirstPostFails_PostsCollectedLinesAndStopsCollecting()
    {
        var firstPost = new TaskCompletionSource<ulong?>();
        var channel = new FakeChannel { FirstPost = firstPost };
        var report = Report(channel);
        Begin(report);
        Assert.True(report.TryCapture("collected", ExtendedLogSeverity.Positive));

        firstPost.SetResult(null);
        await report.Completed.Task.WaitAsync(Wait);

        Assert.Equal(["collected"], channel.Texts);
        Assert.False(report.TryCapture("passes through", ExtendedLogSeverity.Info));
        report.MarkEagerServicesResolved();
        Assert.Empty(channel.Edits);
    }
}
