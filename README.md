# UDC-Bot

A [Discord.NET](https://github.com/discord-net/Discord.Net) bot made for the server Unity Developer Community
Join us on [Discord](https://discord.gg/bu3bbby) !

The code is provided as-is and there will be no guaranteed support to help make it run.

## Table Of Contents

- [Features](#features)
- [Architecture](#architecture)
  - [Services vs Modules](#services-vs-modules)
  - [Dependency Injection](#dependency-injection)
  - [Command System](#command-system)
- [Contributing](#contributing)
  - [Adding a New Command](#adding-a-new-command)
  - [Adding a New Slash Command](#adding-a-new-slash-command)
  - [Creating a New Service](#creating-a-new-service)
  - [Custom Attributes](#custom-attributes)
- [Compiling](#compiling)
  - [Dependencies](#dependencies)
- [Running](#running)
  - [Docker](#docker)
  - [Runtime Dependencies](#runtime-dependencies)
- [Notes](#notes)
  - [Logging](#logging)
  - [Discord.Net](#discordnet)
- [FAQ](#faq)

## Features

| Feature | Description |
|---------|-------------|
| **User Profiles** | XP/level system, karma tracking, profile cards with customizable skins |
| **Moderation** | Mute, kick, ban, slowmode, message clearing, audit logging |
| **Slash Commands** | Modern Discord slash command support alongside text commands |
| **Casino** | Token economy with Blackjack, Poker, and Rock Paper Scissors ([details](docs/casino.md)) |
| **Weather** | Temperature, conditions, air quality, and local time via OpenWeatherMap |
| **Reminders** | Persistent scheduled reminders with natural time parsing |
| **Tips** | Searchable tip database with image support |
| **Tickets** | Private complaint/support ticket system |
| **Unity Help** | Help forum thread management, auto-archive, canned responses, FAQ/resources |
| **Recruitment** | Configurable recruitment workflow |
| **Birthday Announcements** | Scheduled birthday notifications |
| **Currency Conversion** | Real-time currency conversion |
| **Flight Data** | Airport and flight lookups |
| **RSS Feeds** | Feed parsing and management |
| **Leaderboards** | XP, karma (weekly/monthly/yearly), and casino token leaderboards |

## Architecture

This bot follows a **Service-Module** architecture pattern designed for maintainability and separation of concerns.

### Services vs Modules

**Services** (`/DiscordBot/Services/`) contain the core business logic and data operations:

- Handle database interactions, API calls, and background tasks
- Maintain state and provide reusable functionality
- Examples: `UserService`, `DatabaseService`, `ModerationService`, `LoggingService`
- Registered as singletons in the dependency injection container

**Modules** (`/DiscordBot/Modules/`) handle Discord command interactions:

- Expose functionality to users via chat commands
- Use `[Command]` attributes to define command behavior
- Receive services via dependency injection
- Examples: `UserModule`, `TipModule`, `ModerationModule`

### Dependency Injection

The bot uses the .NET Generic Host, options validation, and built-in dependency injection:

- Services are registered in `Program.cs` using `ConfigureServices()`
- Configuration is bound to narrow domain options; modules use service/policy interfaces and never receive secret options
- Text and interaction modules derive from parallel shared bases and can receive services through Discord.Net injection
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
- `[RequireComponentEnabled]` and its interaction equivalent gate commands whose component is unavailable
- Command routing is handled by `CommandHandlingService`
- Administrators can inspect safe component state with `/bot components`, `/bot status`, or the `!bot` text fallbacks

## Contributing

### Adding a New Command

1. **Choose the appropriate module** or create a new one in `/DiscordBot/Modules/`
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
public UserService UserService { get; set; }
public DatabaseService DatabaseService { get; set; }
```

### Adding a New Slash Command

1. **Choose an existing interaction module** or create a new one in `/DiscordBot/Modules/`
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

### Creating a New Service

1. **Create your service class** in `/DiscordBot/Services/`:

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

### Custom Attributes

Create custom precondition attributes in `/DiscordBot/Attributes/`:

```csharp
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public class RequireMyRoleAttribute : PreconditionAttribute
{
    public override Task<PreconditionResult> CheckPermissionsAsync(
        ICommandContext context, CommandInfo command, IServiceProvider services)
    {
        var user = (SocketGuildUser)context.Message.Author;
        var authorization = services.GetRequiredService<IBotAuthorizationPolicy>();

        if (authorization.IsModerator(user))
            return Task.FromResult(PreconditionResult.FromSuccess());

        return Task.FromResult(PreconditionResult.FromError("Access denied!"));
    }
}
```

## Compiling

### Dependencies

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0), or a newer SDK capable of targeting .NET 8 with the .NET 8 runtime installed
- Git
- PostgreSQL 16, or [Docker](https://www.docker.com/get-started/) with `docker compose`
- An editor or IDE; VS Code users should install the workspace-recommended C# Dev Kit extension

Run solution commands from the repository root:

```bash
dotnet restore DiscordBot.sln
dotnet build DiscordBot.sln
dotnet test DiscordBot.sln --configuration Release
```

See the [local development and debugging guide](docs/development.md) for platform notes, VS Code tasks, F5 launch, process attachment, and troubleshooting.

## Running

### Quick Setup

1. Copy `CoreSettings.example.json` and `FeatureSettings.example.json` to their non-example names.
2. Configure the guild, channel, and role IDs for the features you will exercise.
3. Supply the required secrets through `UDCBOT_` environment variables.
4. Start PostgreSQL: `docker compose up --detach db`.
5. Run from the repository root:

```bash
cp DiscordBot/Settings/CoreSettings.example.json DiscordBot/Settings/CoreSettings.json
cp DiscordBot/Settings/FeatureSettings.example.json DiscordBot/Settings/FeatureSettings.json
export UDCBOT_DiscordConnection__Token='development-token'
export UDCBOT_Database__ConnectionString='Host=localhost;Port=5432;Database=udcbot;Username=udcbot;Password=123456789'
```

```bash
dotnet run --project DiscordBot/DiscordBot.csproj
```

VS Code users can instead select `C#: Debug DiscordBot` under **Run and Debug** and press F5. The launch configuration is project-based, so it does not contain a target-framework-specific DLL path.

The modular JSON files are non-secret. Token, database, weather, and airport credentials belong in environment/secret providers and must not be copied into `.vscode`, build output, documentation, or tracked environment files. The old flat `Settings.json` remains a read-only compatibility source for one migration window and logs a deprecation warning.

_For production deployment, see the [Deployment Guide](docs/deployment.md)._

### Docker

For active development, run only the database in Compose and run the bot under the CLI or debugger:

```bash
docker compose up --detach db
```

Use this host-side connection string with the checked-in local database values:

```text
Host=localhost;Port=5432;Database=udcbot;Username=udcbot;Password=123456789
```

To build and run the complete local stack, export the Discord token first. Compose constructs the
container-only database connection from `POSTGRES_PASSWORD` (defaulting to the checked-in local
development password) and forwards the optional weather/airport variables when present.

```bash
export UDCBOT_DiscordConnection__Token='development-token'
docker compose up --build --remove-orphans
```

When the bot runs inside Compose, use `Host=db` instead of `Host=localhost`. The Compose file is a local-development environment, not a production deployment.

### Runtime Dependencies

If you do not use Docker, install PostgreSQL 16, create a database and user, then set `UDCBOT_Database__ConnectionString`:

```text
Host=localhost;Port=5432;Database=udcbot;Username=udcbot;Password=YOUR_PASSWORD
```

The bot attempts to create its required tables on first run, so the database user needs suitable permissions.

**Profile rendering diagnostics:**

Profile cards use font files under `DiscordBot/Assets/fonts`; no host font discovery or
system font package is required for this path. Verify the native Magick runtime, assets,
PNG encoding, and fonts without connecting to Discord or the database:

```bash
dotnet run --project DiscordBot/DiscordBot.csproj -- \
  --render-smoke /tmp/profile-card.png Assets

dotnet run --project DiscordBot/DiscordBot.csproj -- \
  --render-stress 100 4 Assets
```

The production image is Linux AMD64 because it uses `Magick.NET-Q8-x64`; its Docker build
fails early for other target architectures. The image currently retains Microsoft Core
Fonts pending a separate bundled-font licensing review, although the profile path is proven
independent of them.

## Notes

### Logging

The bot includes comprehensive logging to help with troubleshooting:

**Log Levels and Colors:**

- **Critical/Error:** Red text - Something is broken and needs immediate attention
- **Warning:** Yellow text - Potential issues that should be investigated
- **Info:** White text - General operational information
- **Verbose/Debug:** Gray text - Detailed information for development

**During startup:** Any yellow or red messages likely indicate configuration or connectivity issues.

**Log Locations:**

- Console output for immediate feedback
- Channel logging (if configured) for persistent records
- Moderation audit records for single and bulk message deletions, bounded thread/forum-post deletion summaries, and genuine message edits
- See [LoggingService](https://github.com/Unity-Developer-Community/UDC-Bot/blob/dev/DiscordBot/Services/LoggingService.cs) for implementation details

### Discord.Net Framework

This bot is built on [Discord.Net](https://discordnet.dev/), a powerful .NET library for Discord bots.

**Key Concepts to Understand:**

- **Asynchronous Programming:** Extensive use of `async`/`await` patterns
- **Event-Driven Architecture:** Reactions to Discord events (messages, user joins, etc.)
- **Polymorphism:** Rich type hierarchy for Discord entities (users, channels, guilds)

**Helpful Resources:**

- [Discord.Net Documentation](https://discordnet.dev/guides/introduction/intro.html)
- [Discord.Net API Reference](https://discordnet.dev/api/index.html)
- [Discord Developer Portal](https://discord.com/developers/docs) for Discord API specifics

**Common Patterns in this Bot:**

- Commands return `Task` for async operations
- Heavy use of dependency injection for service access
- Event handlers for background functionality (user joins, message processing)

## FAQ

### Common Setup Issues

**Q: The bot won't start - what should I check?**
A: Verify these in order:

1. `UDCBOT_DiscordConnection__Token` is set in the bot process environment
2. Database connection string is correct and database is accessible
3. The bot was started through the project command, VS Code task, or F5 profile so the runtime working directory is correct
4. Check console output for red/yellow log messages indicating specific errors

**Q: "Unable to load the service index" or NuGet restore errors**
A: This is usually a temporary network issue with package sources. Try:
**Warning:** Clearing NuGet locals will remove all cached packages and temporary files. This may require re-downloading dependencies, which could take significant time on slower connections.

```bash
dotnet nuget locals all --clear
dotnet restore DiscordBot.sln
```

**Q: Database connection fails**
A: Common causes:

- Incorrect connection string format
- Database server not running
- User permissions insufficient
- Firewall blocking database port

**Q: How do I get a Discord bot token?**
A:

1. Go to [Discord Developer Portal](https://discord.com/developers/applications)
2. Create a new application
3. Go to "Bot" section
4. Click "Add Bot" and copy the token
5. Invite the bot to your server with appropriate permissions

**Q: What permissions does the bot need?**
A: The bot requires:

- Read Messages
- Send Messages
- Manage Messages (for moderation features)
- Add Reactions
- Use Slash Commands
- Additional permissions based on enabled features

**Q: How can I contribute or report bugs?**
A:

- Check existing issues on GitHub
- For bugs: provide console logs and steps to reproduce
- For contributions: see the [Contributing](#contributing) section above

### Development Tips

**Q: How do I debug commands?**
A:

- Follow the [local development and debugging guide](docs/development.md)
- In VS Code, select `C#: Debug DiscordBot`, set a breakpoint, and press F5
- To debug a process started in a terminal, use `C#: Attach to .NET process`
- Use the logging system: `LoggingService.LogToConsole(message, ExtendedLogSeverity.Info)`
- Check the command history in `CommandHandlingService`

**Q: My command isn't working**
A: Common issues:

- Missing `[Command]` attribute
- Incorrect parameter types
- Missing dependency injection setup
- Permission attribute blocking execution
