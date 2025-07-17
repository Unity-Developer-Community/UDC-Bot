# UDC-Bot

A [Discord.NET](https://github.com/discord-net/Discord.Net) bot made for the server Unity Developer Community
Join us on [Discord](https://discord.gg/bu3bbby) !

The code is provided as-is and there will be no guaranteed support to help make it run.

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

The bot uses .NET's built-in dependency injection system:
- Services are registered in `Program.cs` using `ConfigureServices()`
- Modules receive services via public property injection
- This allows for loose coupling and easier testing

### Command System

Commands are implemented using Discord.Net's command framework:
- Commands use attributes like `[Command("commandname")]` and `[Summary("description")]`
- Custom attributes provide authorization: `[RequireModerator]`, `[RequireAdmin]`
- Command routing is handled by `CommandHandlingService`

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

3. **Inject required services** via public properties:
```csharp
public UserService UserService { get; set; }
public DatabaseService DatabaseService { get; set; }
```

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

2. **Register the service** in `Program.cs` within `ConfigureServices()`:
```csharp
.AddSingleton<MyNewService>()
```

3. **Inject it into modules** that need it:
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
        var settings = services.GetRequiredService<BotSettings>();
        
        if (user.Roles.Any(x => x.Id == settings.MyRoleId))
            return Task.FromResult(PreconditionResult.FromSuccess());
            
        return Task.FromResult(PreconditionResult.FromError("Access denied!"));
    }
}
```

# Table Of Contents
<!-- Link to all the headers -->
- [Architecture](#architecture)
  - [Services vs Modules](#services-vs-modules)
  - [Dependency Injection](#dependency-injection)
  - [Command System](#command-system)
- [Contributing](#contributing)
  - [Adding a New Command](#adding-a-new-command)
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

## Compiling

### Dependencies

To successfully compile you will need the following:

**Required:**
- [.NET 6.0 SDK](https://dotnet.microsoft.com/download/dotnet/6.0) or later
- An IDE such as [Visual Studio](https://visualstudio.microsoft.com/vs/community/), [VS Code](https://code.visualstudio.com/), or [JetBrains Rider](https://www.jetbrains.com/rider/)

**Recommended for Development:**
- [Docker](https://www.docker.com/get-started) and [Docker Compose](https://docs.docker.com/compose/install/) for database containerization

**Build the project:**
```bash
dotnet restore
dotnet build
```

> **Note:** Docker is highly recommended for local development as it simplifies database setup and ensures consistency across development environments.

## Running

### Quick Setup

1. **Copy required folders:**
   - Copy the `DiscordBot/SERVER` folder to your build output directory
   - If unsure of the location, run the bot once - it will show an error with the expected path

2. **Configure settings:**
   - Copy `DiscordBot/Settings` folder to the `SERVER` folder (exclude the `Deserialized` subfolder)
   - Copy `Settings.example.json` and rename it to `Settings.json`
   - Edit `Settings.json` and configure:
     - **Bot Token:** Get this from the [Discord Developer Portal](https://discord.com/developers/applications)
     - **DbConnectionString:** Database connection details (see database setup below)

3. **Choose your database setup:** [Docker](#docker) (recommended) or [Manual setup](#runtime-dependencies)

> **Important:** Read the comments in `Settings.json` carefully - they explain which settings need to be changed and which are optional.

_For production deployment, consult the [Discord.Net Deployment Guide](https://discord.foxbot.me/docs/guides/deployment/deployment.html)._

### Docker

**Recommended for development:** Docker simplifies database setup and ensures consistency.

**To run with Docker:**
```bash
# Start both database and bot
docker-compose up

# Start only the database (run bot from IDE for faster development)
docker-compose up database
```

**Development workflow:**
1. Start the database container: `docker-compose up database`
2. Update the `DbConnectionString` in `Settings.json` to match your docker-compose configuration
3. Run the bot from your IDE for faster development iteration

**Full Docker deployment:**
```bash
# Build and start everything
docker-compose up --build --remove-orphans

# Run in background
docker-compose up -d
```

> **Tip:** For active development, use Docker only for the database and run the bot from your IDE - this gives you faster restart times and better debugging capabilities.

### Runtime Dependencies

**Manual Database Setup (Alternative to Docker):**

If you prefer not to use Docker, you'll need to set up a MySQL database manually:

1. **Install MySQL server:**
   - **Windows/macOS:** [XAMPP](https://www.apachefriends.org/download.html) (includes MySQL + phpMyAdmin)
   - **Linux:** `sudo apt install mysql-server` or equivalent

2. **Create database and user:**
   - Create a new database for the bot
   - Create a user with full permissions to that database
   - Update the `DbConnectionString` in `Settings.json` with your database details

3. **Initialize database schema:**
   - The bot will attempt to create necessary tables on first run
   - If it fails due to permissions, you may need to run it with elevated database privileges initially

**Additional Linux Requirements:**
For image processing functionality, install Microsoft Core Fonts:
```bash
sudo apt install ttf-mscorefonts-installer
```

**Connection String Format:**
```json
"DbConnectionString": "Server=localhost;Database=your_db_name;Uid=your_username;Pwd=your_password;"
```

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
1. Bot token is correctly set in `Settings.json`
2. Database connection string is correct and database is accessible
3. All required folders (SERVER, Settings) are in the right location
4. Check console output for red/yellow log messages indicating specific errors

**Q: "Unable to load the service index" or NuGet restore errors**
A: This is usually a temporary network issue with package sources. Try:
**Warning:** Clearing NuGet locals will remove all cached packages and temporary files. This may require re-downloading dependencies, which could take significant time on slower connections.
```bash
dotnet nuget locals all --clear
dotnet restore
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
- Use the logging system: `LoggingService.LogToConsole(message, ExtendedLogSeverity.Info)`
- Set breakpoints in your IDE when running the bot locally
- Check the command history in `CommandHandlingService`

**Q: My command isn't working**
A: Common issues:
- Missing `[Command]` attribute
- Incorrect parameter types
- Missing dependency injection setup
- Permission attribute blocking execution
