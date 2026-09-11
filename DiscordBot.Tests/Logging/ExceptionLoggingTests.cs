using System.Runtime.CompilerServices;
using Discord;
using DiscordBot.Services.Logging;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DiscordBot.Tests.Logging;

[TestClass]
[DoNotParallelize]
public sealed class ExceptionLoggingTests
{
    [TestMethod]
    public void CompactReportIncludesMessageAndSourceWithoutFullPath()
    {
        var report = ExceptionLogFormatter.Format(Capture(), "Import failed", detailed: false);
        StringAssert.Contains(report, "Import failed | FormatException: invalid date");
        StringAssert.Contains(report, "ExceptionLoggingTests.cs:");
        Assert.IsFalse(report.Contains('\n'));
        Assert.IsFalse(report.Contains(Path.GetTempPath()));
        Assert.IsFalse(report.Contains("/home/"));
    }

    [TestMethod]
    public void DevelopmentReportHasBoundedTrace()
    {
        var report = ExceptionLogFormatter.Format(Capture(), detailed: true);
        StringAssert.Contains(report, "\n  at ");
        Assert.IsTrue(report.Split("\n  at ").Length <= 4);
        StringAssert.Contains(report, nameof(ThrowFormat));
    }

    [TestMethod]
    public void WrappedAndAggregateFailuresRetainInnerMessages()
    {
        var exception = new AggregateException("batch", new InvalidOperationException("wrapper", Capture()),
            new IOException("second failure"));
        var report = ExceptionLogFormatter.Format(exception, detailed: false);
        StringAssert.Contains(report, "InvalidOperationException: wrapper");
        StringAssert.Contains(report, "FormatException: invalid date");
        StringAssert.Contains(report, "IOException: second failure");
        Assert.IsFalse(report.Contains('\n'));
    }

    [TestMethod]
    public void UnthrownExceptionUsesClearlyLabelledCallerFallback()
    {
        var report = ExceptionLogFormatter.Format(new Exception("failure"), detailed: false,
            callerFile: "/private/build/Worker.cs", callerLine: 42, callerMember: "Run");
        StringAssert.Contains(report, "Worker.cs:42 (Run; logged here)");
        Assert.IsFalse(report.Contains("/private/"));
    }

    [TestMethod]
    public void OversizedReportsAreBoundedAndSingleLineInCompactMode()
    {
        var exception = new AggregateException(Enumerable.Range(0, 100)
            .Select(_ => new Exception(new string('x', 1000) + "\r\nsecond line")));
        var report = ExceptionLogFormatter.Format(exception, new string('y', 2000), detailed: false);
        Assert.IsTrue(report.Length <= 1800);
        Assert.IsFalse(report.Contains('\n'));
        StringAssert.Contains(report, "truncated");
    }

    [TestMethod]
    public async Task DiscordNetIncludesExceptionEvenWithoutMessage()
    {
        var output = await CaptureConsole(() => LoggingService.DiscordNetLogger(
            new LogMessage(LogSeverity.Error, "Gateway", null, Capture())));
        StringAssert.Contains(output, "Gateway");
        StringAssert.Contains(output, "FormatException: invalid date");
    }

    [TestMethod]
    public async Task HostLoggerRetainsExceptionDetails()
    {
        var output = await CaptureConsole(() =>
        {
            using var provider = new BotConsoleLoggerProvider();
            provider.CreateLogger("Worker").LogError(Capture(), "Worker failed");
            return Task.CompletedTask;
        });
        StringAssert.Contains(output, "Worker failed");
        StringAssert.Contains(output, "FormatException: invalid date");
    }

    [TestMethod]
    public async Task ExceptionApiPreservesRoutingAndKeepsDiscordCompact()
    {
        var logging = new RecordingLogger();
        await logging.LogException(Capture(), "Import failed", LogBehaviour.ConsoleChannelAndFile,
            ExtendedLogSeverity.Warning);
        Assert.HasCount(2, logging.Entries);
        Assert.AreEqual(LogBehaviour.Console | LogBehaviour.File, logging.Entries[0].Behaviour);
        Assert.AreEqual(LogBehaviour.Channel, logging.Entries[1].Behaviour);
        Assert.AreEqual(ExtendedLogSeverity.Warning, logging.Entries[1].Severity);
        Assert.IsFalse(logging.Entries[1].Message.Contains('\n'));
        StringAssert.Contains(logging.Entries[1].Message, "invalid date");
    }

    [TestMethod]
    public void FirstChanceTracingFiltersCapsAndUnsubscribes()
    {
        var messages = new List<string>();
        using (var diagnostics = new FirstChanceExceptionDiagnostics(typeof(FormatException).FullName!, messages.Add))
        {
            try { throw new InvalidOperationException("unrelated"); } catch (InvalidOperationException) { }
            for (var i = 0; i < 25; i++) Capture();
        }
        StringAssert.Contains(messages[0], "ExceptionLoggingTests.cs:");
        Assert.AreEqual(20, messages.Count(message => message.Contains("FormatException: invalid date")));
        Assert.AreEqual(1, messages.Count(message => message.Contains("limit reached")));
        Capture();
        Assert.HasCount(21, messages);
    }

    [TestMethod]
    public void FirstChanceWriterFailureCannotRecurseOrEscape()
    {
        var calls = 0;
        using var diagnostics = new FirstChanceExceptionDiagnostics(typeof(FormatException).FullName!, _ =>
        {
            calls++;
            throw new FormatException("writer failed");
        });
        Capture();
        Assert.AreEqual(1, calls);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static Exception Capture()
    {
        try { ThrowFormat(); }
        catch (FormatException exception) { return exception; }
        throw new InvalidOperationException("unreachable");
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ThrowFormat() => throw new FormatException("invalid date");

    internal static async Task<string> CaptureConsole(Func<Task> action)
    {
        var original = Console.Out;
        using var writer = new StringWriter();
        try
        {
            Console.SetOut(writer);
            await action();
            return writer.ToString();
        }
        finally { Console.SetOut(original); }
    }

    private sealed class RecordingLogger : ILoggingService
    {
        public List<(LogBehaviour Behaviour, string Message, ExtendedLogSeverity Severity)> Entries { get; } = [];
        public Task Log(LogBehaviour behaviour, string message, ExtendedLogSeverity severity = ExtendedLogSeverity.Info,
            Embed? embed = null)
        {
            Entries.Add((behaviour, message, severity));
            return Task.CompletedTask;
        }
        public void LogXp(string channel, string user, float baseXp, float bonusXp, float xpReduce, int totalXp) { }
    }
}
