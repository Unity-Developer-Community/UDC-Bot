using System.Reflection;
using Discord;
using Discord.Commands;
using Discord.Interactions;
using Discord.WebSocket;
using DiscordBot.Components;
using DiscordBot.Services;
using DiscordBot.Services.Logging;
using DiscordBot.Settings.Options;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DiscordBot.Tests.Logging;

[TestClass]
[DoNotParallelize]
public sealed class CommandFailureLoggingTests
{
    [TestMethod]
    public async Task AsyncCommandFailureIsLoggedOnceAfterRepeatedStartAndStopsWithService()
    {
        using var client = new DiscordSocketClient();
        using var commands = new CommandService(new CommandServiceConfig { DefaultRunMode = Discord.Commands.RunMode.Async });
        using var interactions = new InteractionService(client);
        using var services = new ServiceCollection().BuildServiceProvider();
        await commands.AddModuleAsync<FailingModule>(services);
        var logging = new RecordingLogger();
        var service = new CommandHandlingService(client, commands, interactions, services,
            Options.Create(new CommandOptions { Prefix = '!' }), Options.Create(new DiscordGuildOptions()),
            logging, new ComponentState());
        // Skip real guild registration; test the actual lifecycle subscriptions and Discord.Net execution.
        typeof(CommandHandlingService).GetField("_modulesRegistered", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(service, true);
        await service.StartAsync(CancellationToken.None);
        await service.StartAsync(CancellationToken.None);
        var context = new TestContext(client);
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        commands.CommandExecuted += (_, _, _) => { completed.TrySetResult(); return Task.CompletedTask; };
        await commands.ExecuteAsync(context, "fail", services);
        await completed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.AreEqual(1, logging.Failures.Count);
        StringAssert.Contains(logging.Failures[0], "FormatException: command exploded");
        StringAssert.Contains(logging.Failures[0], "Command fail failed");

        await service.StopAsync(CancellationToken.None);
        completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await commands.ExecuteAsync(context, "fail", services);
        await completed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.AreEqual(1, logging.Failures.Count);

        await service.StartAsync(CancellationToken.None);
        // This observer was subscribed before the restarted handler; wait on the logger itself.
        await commands.ExecuteAsync(context, "fail", services);
        await logging.SecondFailure.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.AreEqual(2, logging.Failures.Count);
        await service.StopAsync(CancellationToken.None);
    }

    public sealed class FailingModule : Discord.Commands.ModuleBase<ICommandContext>
    {
        [Command("fail")]
        public Task Fail() => throw new FormatException("command exploded");
    }

    private sealed class TestContext(IDiscordClient client) : ICommandContext
    {
        public IDiscordClient Client => client;
        public IGuild Guild => null!;
        public IMessageChannel Channel => null!;
        public IUser User => null!;
        public IUserMessage Message => null!;
    }

    private sealed class RecordingLogger : ILoggingService
    {
        public List<string> Failures { get; } = [];
        public TaskCompletionSource SecondFailure { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task Log(LogBehaviour behaviour, string message, ExtendedLogSeverity severity = ExtendedLogSeverity.Info, Embed? embed = null)
        {
            if (severity == ExtendedLogSeverity.Error)
            {
                Failures.Add(message);
                if (Failures.Count == 2) SecondFailure.TrySetResult();
            }
            return Task.CompletedTask;
        }
        public void LogXp(string channel, string user, float baseXp, float bonusXp, float xpReduce, int totalXp) { }
    }

    private sealed class ComponentState : IComponentStateReader
    {
        public event EventHandler? StateChanged { add { } remove { } }
        public IReadOnlyList<ComponentSnapshot> GetAll() => [];
        public ComponentSnapshot? Get(string componentId) => null;
        public bool IsEnabled(string componentId) => true;
    }
}
