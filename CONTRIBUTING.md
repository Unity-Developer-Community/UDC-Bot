# Contributing to UDC-Bot

Setup and running the bot locally are in the [README](README.md). Bugs and feature requests go to [GitHub Issues](https://github.com/Unity-Developer-Community/UDC-Bot/issues): for a bug, include the console logs and steps to reproduce.

Open pull requests against `master`. CI builds and runs the tests (`dotnet test`) on every PR; new logic should come with tests in `DiscordBot.Tests/`. A returning contributor or maintainer can comment `/build` on a PR to build a dev image; the bot replies with the steps to deploy it to the dev server.

## Architecture

This bot follows a **Service-Module** architecture pattern designed for maintainability and separation of concerns. Both are grouped by domain: `Profiles`, `Server`, `Fun`, `Utils`, `Code`; [docs/features.md](docs/features.md) maps each feature to its modules and services.

### Services vs Modules

**Services** (`DiscordBot/Services/<domain>/`) contain the core business logic and data operations:

- Handle database interactions, API calls, and background tasks
- Maintain state and provide reusable functionality
- Examples: `XpService`, `CasinoService`, `DatabaseService`, `LoggingService`
- Registered as singletons in the dependency injection container

**Modules** (`DiscordBot/Modules/<domain>/`) handle Discord command interactions:

- Expose functionality to users via chat commands
- Use `[Command]` (text) or `[SlashCommand]` (slash) attributes to define command behavior
- Receive services via dependency injection
- Examples: `TipModule`, `ProfileSlashModule`, `CasinoSlashModule`

### Dependency Injection

The bot uses .NET's built-in dependency injection system:

- Services are registered in `Program.cs` using `ConfigureServices()`
- Modules receive services via public property injection
- This allows for loose coupling and easier testing

### Command System

The bot supports both **text commands** and **slash commands**:

**Text Commands** (via `CommandService`):

- Use attributes like `[Command("commandname")]` and `[Summary("description")]`
- Triggered by the configured prefix (default `!`)

**Slash Commands** (via `InteractionService`):

- Use `[SlashCommand]`, `[Group]`, and `[ComponentInteraction]` attributes
- Registered per-guild using `GuildId` from settings
- Support autocomplete, buttons, modals, and select menus

**Shared:**

- Custom attributes provide authorization: `[RequireModerator]`, `[RequireAdmin]`
- Command routing is handled by `CommandHandlingService`

## Adding a New Command

1. **Choose the appropriate module** or create a new one in `DiscordBot/Modules/<domain>/`
2. **Add the command method** with proper attributes:

```csharp
[Command("mycommand")]
[Summary("Description of what this command does")]
[RequireModerator] // Optional: Add permission requirements
public async Task MyCommand(string parameter)
{
    // Your command logic here
    await ReplyAsync("Command executed!");
}
```

1. **Inject required services** via public properties:

```csharp
public XpService XpService { get; set; }
public DatabaseService DatabaseService { get; set; }
```

## Adding a New Slash Command

1. **Choose an existing interaction module** or create a new one in `DiscordBot/Modules/<domain>/`
2. **Add the slash command method** with proper attributes:

```csharp
[SlashCommand("mycommand", "Description of what this command does")]
public async Task MyCommand(
    [Summary(description: "A parameter")] string parameter)
{
    await RespondAsync("Command executed!");
}
```

1. **For grouped commands**, use the `[Group]` attribute on the class:

```csharp
[Group("mygroup", "Group description")]
public class MySlashModule : InteractionModuleBase<SocketInteractionContext>
{
    [SlashCommand("subcommand", "Subcommand description")]
    public async Task SubCommand() => await RespondAsync("Done!");
}
```

> Slash commands are registered per-guild on startup using the `GuildId` setting.

## Creating a New Service

1. **Create your service class** in `DiscordBot/Services/<domain>/`:

```csharp
public class MyNewService
{
    private readonly DatabaseService _databaseService;
    
    public MyNewService(DatabaseService databaseService)
    {
        _databaseService = databaseService;
    }
    
    public async Task DoSomethingAsync()
    {
        // Your service logic here
    }
}
```

1. **Register the service** in `Program.cs` within `ConfigureServices()`:

```csharp
.AddSingleton<MyNewService>()
```

1. **Inject it into modules** that need it:

```csharp
public MyNewService MyNewService { get; set; }
```

## Custom Attributes

Create custom precondition attributes in `DiscordBot/Attributes/`:

```csharp
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public class RequireMyRoleAttribute : PreconditionAttribute
{
    public override Task<PreconditionResult> CheckPermissionsAsync(
        ICommandContext context, CommandInfo command, IServiceProvider services)
    {
        var user = (SocketGuildUser)context.Message.Author;
        var settings = services.GetRequiredService<BotSettings>();
        
        if (user.Roles.Any(x => x.Id == settings.MyRoleId))
            return Task.FromResult(PreconditionResult.FromSuccess());
            
        return Task.FromResult(PreconditionResult.FromError("Access denied!"));
    }
}
```
