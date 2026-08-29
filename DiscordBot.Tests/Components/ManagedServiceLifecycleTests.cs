using Discord;
using Discord.WebSocket;
using DiscordBot.Services;
using DiscordBot.Services.Logging;
using DiscordBot.Settings.Options;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DiscordBot.Tests.Components;

[TestClass]
public sealed class ManagedServiceLifecycleTests
{
    [TestMethod]
    public async Task BirthdayWorker_RepeatedStartStopDoesNotDuplicateTheLoop()
    {
        using var client = new DiscordSocketClient();
        var source = new FakeBirthdaySource();
        var service = new BirthdayAnnouncementService(
            client,
            new FakeLoggingService(),
            source,
            Options.Create(new BirthdayOptions
            {
                Enabled = true,
                AnnouncementChannelId = 1,
                CheckIntervalMinutes = 60
            }));

        await service.StartAsync(CancellationToken.None);
        await service.StartAsync(CancellationToken.None);
        Assert.IsTrue(service.IsRunning);
        Assert.AreEqual(1, source.CallCount);

        await service.StopAsync(CancellationToken.None);
        await service.StopAsync(CancellationToken.None);
        Assert.IsFalse(service.IsRunning);

        await service.StartAsync(CancellationToken.None);
        Assert.AreEqual(2, source.CallCount);
        await service.StopAsync(CancellationToken.None);
    }

    [TestMethod]
    public async Task ReminderWorker_RepeatedStartStopPersistsAndRecreatesTheLoop()
    {
        var root = Path.Combine(Path.GetTempPath(), $"udc-reminders-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            using var client = new DiscordSocketClient();
            var service = new ReminderService(
                client,
                new FakeLoggingService(),
                Options.Create(new ReminderOptions { FallbackChannelId = 1 }),
                Options.Create(new StorageOptions
                {
                    ServerRootPath = root,
                    AssetsRootPath = root
                }));

            await service.StartAsync(CancellationToken.None);
            await service.StartAsync(CancellationToken.None);
            Assert.IsTrue(service.IsRunning);

            await service.StopAsync(CancellationToken.None);
            await service.StopAsync(CancellationToken.None);
            Assert.IsFalse(service.IsRunning);
            Assert.IsTrue(File.Exists(Path.Combine(root, "reminders.json")));

            await service.StartAsync(CancellationToken.None);
            Assert.IsTrue(service.IsRunning);
            await service.StopAsync(CancellationToken.None);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private sealed class FakeBirthdaySource : IBirthdaySource
    {
        public int CallCount { get; private set; }

        public Task<IReadOnlyList<BirthdayInfo>> GetTodaysBirthdaysAsync(
            CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult<IReadOnlyList<BirthdayInfo>>([]);
        }
    }

    private sealed class FakeLoggingService : ILoggingService
    {
        public void LogXp(string channel, string user, float baseXp, float bonusXp, float xpReduce, int totalXp)
        {
        }

        public Task Log(
            LogBehaviour behaviour,
            string message,
            ExtendedLogSeverity severity = ExtendedLogSeverity.Info,
            Embed? embed = null) => Task.CompletedTask;
    }
}
