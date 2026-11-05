# UDC-Bot

A [Discord.NET](https://github.com/discord-net/Discord.Net) bot made for the server Unity Developer Community
Join us on [Discord](https://discord.gg/bu3bbby) !

The code is provided as-is and there will be no guaranteed support to help make it run.

## Features

- **Profiles:** XP and levels, karma, profile cards with skins, badges, birthdays, leaderboards
- **Unity help:** help-forum thread management, canned responses, FAQ, tips, documentation and manual search
- **Server:** welcome messages, audit logging, rules, tickets, reminders, quotes, embeds, recruitment
- **Fun:** casino with a token economy (Blackjack, Poker, Rock Paper Scissors, slots with a shared jackpot, see [docs/casino.md](docs/casino.md)), duels, dice rolls
- **Utilities:** weather, currency and temperature conversion, flight lookups, Unity release feeds

Commands exist as slash commands and as `!` text commands. [docs/features.md](docs/features.md) describes each feature and the code behind it; `/help` and `!help` list the commands in Discord.

## Requirements

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- [Docker](https://www.docker.com/get-started) for the local database (or a PostgreSQL 16 server)
- A Discord bot token from the [Discord Developer Portal](https://discord.com/developers/applications)
- On Linux, Microsoft Core Fonts for profile cards: `sudo apt install ttf-mscorefonts-installer`

## Running locally

1. Copy `DiscordBot/Settings/Settings.example.json` to `DiscordBot/Settings/Settings.json` and edit it (the bot lists missing or invalid settings at startup). At least set:
   - `Token`: your bot token
   - `DbConnectionString`: `Host=localhost;Port=5432;Database=udcbot;Username=udcbot;Password=123456789` for the Docker database below (with your own PostgreSQL, use a database and a user that owns it)
   - `GuildId` and the channel and role IDs of your test server
2. Start the database: `docker compose up db` (add `adminer` for a database UI on <http://localhost:8080>).
3. Run the bot from the `DiscordBot/` folder, which must be its working directory (set it in your IDE's run configuration):

   ```bash
   cd DiscordBot
   dotnet run
   ```

The bot creates its tables on first run. Runtime data (logs, generated images) goes to `DiscordBot/SERVER/`.

To run everything in Docker instead, set `Host=db` in the connection string and run `docker compose up --build`.

Tests: `dotnet test` (the casino database tests start a temporary PostgreSQL, so Docker must be running).

Production runs on Kubernetes: see the [Deployment Guide](docs/deployment.md).

## Contributing

See [CONTRIBUTING.md](CONTRIBUTING.md) for the architecture and how to add commands and services.

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
- See [LoggingService](DiscordBot/Services/LoggingService.cs) for implementation details

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
3. The bot runs with `DiscordBot/` as its working directory, so it finds `Settings/` and `Assets/`
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
- Manage Messages (to clean up command messages)
- Add Reactions
- Use Slash Commands
- Additional permissions based on enabled features

**Q: How can I contribute or report bugs?**
A:

- Check existing issues on GitHub
- For bugs: provide console logs and steps to reproduce
- For contributions: see [CONTRIBUTING.md](CONTRIBUTING.md)

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
