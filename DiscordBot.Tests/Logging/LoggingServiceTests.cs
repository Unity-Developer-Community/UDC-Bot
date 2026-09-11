using Discord.WebSocket;
using DiscordBot.Services.Logging;
using DiscordBot.Settings.Options;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DiscordBot.Tests.Logging;

[TestClass]
[DoNotParallelize]
public sealed class LoggingServiceTests
{
    [TestMethod]
    public async Task MissingFilesAndConcurrentWritesPreserveAllEntries()
    {
        await WithLogger(async (logger, root) =>
        {
            await Task.WhenAll(Enumerable.Range(0, 50).Select(i => logger.LogToFile($"entry-{i}")));
            await Task.WhenAll(Enumerable.Range(0, 20).Select(i => Task.Run(() => logger.LogXp("test", $"user-{i}", 1, 0, 0, 1))));
            var lines = await File.ReadAllLinesAsync(Path.Combine(root, "log.txt"));
            Assert.HasCount(50, lines);
            Assert.AreEqual(50, lines.Distinct().Count());
            Assert.HasCount(20, await File.ReadAllLinesAsync(Path.Combine(root, "logXP.txt")));
        });
    }

    [TestMethod]
    public async Task EachFileRotatesIndependentlyAndRepeatedRotationDoesNotCollide()
    {
        await WithLogger(async (logger, root) =>
        {
            Directory.CreateDirectory(root);
            for (var i = 0; i < 2; i++)
            {
                await File.WriteAllTextAsync(Path.Combine(root, "log.txt"), new string('a', 2 * 1024 * 1024 + 1));
                await File.WriteAllTextAsync(Path.Combine(root, "logXP.txt"), new string('b', 2 * 1024 * 1024 + 1));
                await logger.LogToFile("after rotation");
                logger.LogXp("test", "user", 1, 0, 0, 1);
            }
            var backups = Directory.GetFiles(Path.Combine(root, "log_backups"));
            Assert.HasCount(4, backups);
            Assert.AreEqual(2, backups.Count(path => Path.GetFileName(path).StartsWith("logXP_")));
            StringAssert.Contains(await File.ReadAllTextAsync(Path.Combine(root, "log.txt")), "after rotation");
        });
    }

    [TestMethod]
    public async Task CombinedFileFlagsWriteOnceAndRespectCommandLoggingSetting()
    {
        await WithLogger(async (logger, root) =>
        {
            await logger.Log(LogBehaviour.File | LogBehaviour.CommandFile, "only once");
            Assert.HasCount(1, await File.ReadAllLinesAsync(Path.Combine(root, "log.txt")));
        });
        await WithLogger(async (logger, root) =>
        {
            await logger.Log(LogBehaviour.CommandFile, "disabled");
            Assert.IsFalse(File.Exists(Path.Combine(root, "log.txt")));
            await logger.Log(LogBehaviour.File | LogBehaviour.CommandFile, "explicit file");
            Assert.HasCount(1, await File.ReadAllLinesAsync(Path.Combine(root, "log.txt")));
        }, logCommands: false);
    }

    [TestMethod]
    public async Task FileFailureFallsBackToConsoleWithoutMaskingOriginalMessage()
    {
        await WithLogger(async (logger, root) =>
        {
            Directory.CreateDirectory(Path.Combine(root, "log.txt"));
            var output = await ExceptionLoggingTests.CaptureConsole(() => logger.LogToFile("original failure"));
            StringAssert.Contains(output, "original failure");
            StringAssert.Contains(output, "Failed to write log file");
            Directory.Delete(Path.Combine(root, "log.txt"));
            await logger.LogToFile("recovered");
            StringAssert.Contains(await File.ReadAllTextAsync(Path.Combine(root, "log.txt")), "recovered");
        });
    }

    private static async Task WithLogger(Func<LoggingService, string, Task> action, bool logCommands = true)
    {
        var root = Path.Combine(Path.GetTempPath(), $"udc-logging-{Guid.NewGuid():N}");
        try
        {
            using var client = new DiscordSocketClient();
            var logger = new LoggingService(client,
                Options.Create(new LoggingOptions { LogCommandExecutions = logCommands }),
                Options.Create(new StorageOptions { ServerRootPath = root }));
            await action(logger, root);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
}
