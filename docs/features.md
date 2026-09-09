# Features

## Feature List

| Feature | Description | Module(s) | Service(s) | Criticality |
|---------|-------------|-----------|------------|-------------|
| **User Profiles** | XP/level system, karma tracking, profile cards with customizable skins | `ProfileModule`, `RankModule` | `XpService`, `KarmaService`, `ProfileCardService`, `UserExtendedService` | Core |
| **Command Handling** | Text command + slash command routing, history tracking, prefix config | — | `CommandHandlingService` | Core |
| **Logging** | Multi-destination logging (console, file, Discord channel) with severity levels | — | `LoggingService` | Core |
| **Database** | PostgreSQL connection pooling, user/casino repositories | — | `DatabaseService` | Core |
| **Audit Logging** | Background logging of moderator-relevant server events | — | `AuditLogService` | Core |
| **Unity Help** | Help forum thread management, auto-archive, canned responses, resources | `UnityHelpModule`, `UnityHelpInteractiveModule`, `GeneralHelpModule`, `CannedResponseModule`, `CannedInteractiveModule` | `UnityHelpService`, `CannedResponseService` | Core |
| **Welcome** | New-member welcome messages | — | `WelcomeService` | Core |
| **Code Assistance** | Code-block formatting checks/reminders and code tips | `CodeTipModule` | `CodeCheckService` | Feature |
| **Tips** | Searchable tip database with image support, keyword lookups | `TipModule` | `TipService` | Feature |
| **Casino** | Token economy, Blackjack, Poker, Rock Paper Scissors, daily rewards, leaderboards | `CasinoSlashModule` | `CasinoService`, `GameService`, `TransactionFormatter` | Feature |
| **Duels** | Player-vs-player duels with configurable stakes | `DuelSlashModule` | `DuelService` | Feature |
| **Fun** | Slap, coin flip, dice rolls (including D&D format) | `FunModule` | — | Feature |
| **Search** | Documentation, manual, and wiki lookups | `SearchModule` | `SearchService` | Feature |
| **Weather** | Temperature, conditions, air quality, local time via OpenWeatherMap | `WeatherModule` | `WeatherService` | Feature |
| **Conversion** | Currency, temperature, and unit conversion | `ConvertModule` | `CurrencyService` | Feature |
| **Flight Data** | Airport and flight lookups | `AirportModule` | `AirportService` | Feature |
| **Reminders** | Persistent scheduled reminders with natural time parsing | `ReminderModule` | `ReminderService` | Feature |
| **Quotes** | Quote a message by ID into the current channel | `QuoteModule` | — | Feature |
| **Rules** | Server/global rules and channel listings | `RulesModule` | — | Feature |
| **Server Utilities** | Help, ping, member count, and server info | `ServerModule`, `ServerSlashModule` | `ServerService` | Feature |
| **Tickets** | Private complaint/support ticket channels | `TicketModule` | — | Feature |
| **Embed Builder** | Generate embeds from messages or hastebin URLs | `EmbedModule` | `EmbedParsingService` | Feature |
| **Birthday Announcements** | Scheduled birthday notifications (configurable interval) | `BirthdayModule` | `BirthdayAnnouncementService` | Feature |
| **Recruitment** | Configurable recruitment workflow (toggleable) | — | `RecruitService` | Feature |
| **Release Feeds** | RSS feed parsing and Unity release-notes tracking | — | `FeedService`, `ReleaseNotesParser` | Feature |
| **@everyone Scold** | Warns users who use `@everyone`/`@here` without permission | — | `EveryoneScoldService` | Feature |
| **Miku** | Playful auto-reply when Hatsune Miku is mentioned | — | `MikuService` | Feature |
| **Karma Reset** | Scheduled periodic karma resets | — | `KarmaResetService` | Maintenance |
| **Update Checker** | Background bot update checking | — | `UpdateService` | Maintenance |

## Slash Commands vs Text Commands

The bot supports both paradigms:

- **Text Commands** — Prefix-based (default `!`), implemented in `*Module.cs` classes
- **Slash Commands** — Discord's native slash commands, implemented in `*SlashModule.cs` and `*InteractiveModule.cs` classes

For a full command reference, see the `!Help` command or `/help` slash command in Discord.
