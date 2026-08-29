using Discord.Commands;
using Discord.Interactions;
using Discord.WebSocket;
using DiscordBot.Attributes;
using DiscordBot.Components;
using DiscordBot.Modules.Base;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DiscordBot.Tests.Modules;

[TestClass]
public sealed class ModuleBaseTests
{
    [TestMethod]
    public async Task PinnedDiscordNet_DiscoversBothSharedModuleBases()
    {
        var services = new ServiceCollection()
            .AddSingleton<IComponentStateReader, AlwaysEnabledComponentState>()
            .BuildServiceProvider();
        var textCommands = new CommandService();
        using var client = new DiscordSocketClient();
        var interactions = new InteractionService(client);

        var textModules = await textCommands.AddModulesAsync(typeof(PinnedTextModule).Assembly, services);
        var interactionModules = await interactions.AddModulesAsync(typeof(PinnedInteractionModule).Assembly, services);

        Assert.IsTrue(textModules.Any(module => module.Name == nameof(PinnedTextModule)));
        Assert.IsTrue(interactionModules.Any(module => module.Name == nameof(PinnedInteractionModule)));
        Assert.IsTrue(typeof(BotCommandModuleBase).GetProperty(nameof(BotCommandModuleBase.ComponentState))?.SetMethod?.IsPublic);
        Assert.IsTrue(typeof(BotInteractionModuleBase).GetProperty(nameof(BotInteractionModuleBase.ComponentState))?.SetMethod?.IsPublic);
    }

    [TestMethod]
    public void ApplicationModules_UseSharedBasesAndKnownComponentIds()
    {
        var assembly = typeof(Program).Assembly;
        var moduleTypes = assembly.GetTypes()
            .Where(type => !type.IsAbstract && type.Namespace?.StartsWith("DiscordBot.Modules", StringComparison.Ordinal) == true)
            .ToArray();

        var textModules = moduleTypes.Where(type => typeof(ModuleBase).IsAssignableFrom(type)).ToArray();
        var interactionModules = moduleTypes
            .Where(type => IsInteractionModule(type))
            .ToArray();
        Assert.IsTrue(textModules.All(type => typeof(BotCommandModuleBase).IsAssignableFrom(type)));
        Assert.IsTrue(interactionModules.All(type => typeof(BotInteractionModuleBase).IsAssignableFrom(type)));

        var knownIds = typeof(ComponentIds).GetFields()
            .Where(field => field.IsLiteral && field.FieldType == typeof(string))
            .Select(field => (string)field.GetRawConstantValue()!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var gatedIds = moduleTypes
            .SelectMany(type => type.GetCustomAttributes(inherit: true)
                .Concat(type.GetMethods().SelectMany(method => method.GetCustomAttributes(inherit: true))))
            .Select(attribute => attribute switch
            {
                RequireComponentEnabledAttribute text => text.ComponentId,
                RequireInteractionComponentEnabledAttribute interaction => interaction.ComponentId,
                _ => null
            })
            .Where(componentId => componentId is not null)
            .Cast<string>()
            .ToArray();

        Assert.IsGreaterThan(0, gatedIds.Length);
        Assert.IsTrue(gatedIds.All(knownIds.Contains));

        static bool IsInteractionModule(Type type)
        {
            for (var current = type; current is not null; current = current.BaseType)
            {
                if (current.IsGenericType &&
                    current.GetGenericTypeDefinition() == typeof(InteractionModuleBase<>))
                {
                    return true;
                }
            }

            return false;
        }
    }

    public sealed class PinnedTextModule : BotCommandModuleBase
    {
        [Command("pinned-text")]
        public Task PinnedText() => Task.CompletedTask;
    }

    public sealed class PinnedInteractionModule : BotInteractionModuleBase
    {
        [SlashCommand("pinned-interaction", "Pinned interaction test command.")]
        public Task PinnedInteraction() => Task.CompletedTask;
    }

    private sealed class AlwaysEnabledComponentState : IComponentStateReader
    {
        public event EventHandler? StateChanged;

        public IReadOnlyList<ComponentSnapshot> GetAll() => [];
        public ComponentSnapshot? Get(string componentId) => null;
        public bool IsEnabled(string componentId) => true;
    }
}
