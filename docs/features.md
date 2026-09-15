# Features

## Feature List

| Feature | Description | Module(s) | Service(s) | Criticality |
| --------- | ------------- | ----------- | ------------ | ------------- |
| **User Profiles** | XP/level system, karma tracking, profile cards with customizable skins (`/profile [user]` or the **View Profile** user context-menu command), join dates (`/join-date [user]`), karma explainer (`/karma`), and level/karma leaderboards (`/top`, `/top-karma [interval]`) | `ProfileSlashModule`, `RankSlashModule` | `ProfileCardService`, `XpService`, `KarmaService`, `UserExtendedService` | Core |
| **Command Handling** | Text command + slash command routing, history tracking, prefix config | — | `CommandHandlingService` | Core |
| **Logging** | Multi-destination logging (console, file, Discord channel) with severity levels | — | `LoggingService` | Core |
| **Database** | PostgreSQL connection pooling, user/casino repositories | — | `DatabaseService` | Core |
| **Audit Logging** | Background logging of moderator-relevant server events | — | `AuditLogService` | Core |
| **Unity Help** | Help forum thread management, auto-archive, canned responses, resources | `UnityHelpModule`, `UnityHelpInteractiveModule`, `GeneralHelpModule`, `CannedResponseModule`, `CannedInteractiveModule` | `UnityHelpService`, `CannedResponseService` | Core |
| **Welcome** | New-member welcome messages | — | `WelcomeService` | Core |
| **Code Assistance** | Code-block formatting checks/reminders and code tips | `CodeTipModule` | `CodeCheckService` | Feature |
| **Tips** | Searchable tip database with image support, keyword lookups | `TipModule` | `TipService` | Feature |
| **Casino** | Token economy, token gifting (`/casino tokens gift` or the **Gift Tokens** user context-menu command, which prompts for the amount), Blackjack, Poker, Rock Paper Scissors, daily rewards, leaderboards, plus admin moderation commands under `/admin casino` (`tokens-history`, `tokens-set`, `tokens-add`, `reset`) with user context-menu counterparts (**View Casino History**, and **Set Tokens** / **Add Tokens**, which prompt for the amount) | `CasinoSlashModule`, `AdminSlashModule` | `CasinoService`, `GameService`, `TransactionFormatter` | Feature |
| **Badges** | Badge catalog, per-user badge viewing, leaderboard, and admin assignment/removal workflows including user context commands (`View Badges`, plus `Assign Badge` and `Remove Badge`, which both pick one or more of the target's badges from a multi-select menu) | `BadgeSlashModule`, `AdminSlashModule` | `BadgeService` | Feature |
| **Duels** | Player-vs-player duels with configurable stakes, available as `/duel` or the **Duel** user context-menu command (non-mute only) | `DuelSlashModule` | `DuelService` | Feature |
| **Fun** | Slap (`/slap` with up to 5 targets), coin flip (`/coinflip`), and dice rolls (`/roll`, including additive D&D notation such as `2d6+4` and `1d20+1d4-1`) | `FunSlashModule` | — | Feature |
| **Search** | Documentation, manual, and wiki lookups | `SearchModule` | `SearchService` | Feature |
| **Weather** | Temperature, conditions, air quality, local time via OpenWeatherMap | `WeatherModule` | `WeatherService` | Feature |
| **Conversion** | Temperature (`/ftoc`, `/ctof`) and currency conversion (`/curr`, with code autocomplete) | `ConvertSlashModule` | `CurrencyService` | Feature |
| **Flight Data** | Airport and flight lookups | `AirportModule` | `AirportService` | Feature |
| **Reminders** | Persistent scheduled reminders with natural time parsing | `ReminderModule` | `ReminderService` | Feature |
| **Quotes** | Quote a message by ID into the current channel | `QuoteModule` | — | Feature |
| **Rules** | Server/global rules and channel listings | `RulesModule` | — | Feature |
| **Server Utilities** | Help, ping, member count (`/members`), and server info | `ServerModule`, `ServerSlashModule` | `ServerService` | Feature |
| **Tickets** | Private complaint/support ticket channels | `TicketModule` | — | Feature |
| **Embed Builder** | Generate embeds from messages or hastebin URLs | `EmbedModule` | `EmbedParsingService` | Feature |
| **Birthday Announcements** | Scheduled birthday notifications (configurable interval), plus birthday command management (`/bday show [count] [user]` lists upcoming birthdays or a specific member's birthday, `/bday set`, `/bday del`, the **View Birthday**, **Set Birthday**, and **Remove Birthday** user context-menu commands — removal asks for confirmation via buttons, admin-only `/admin bday set-user`, `/admin bday del-user`, `/admin bday list`) | `BirthdayModule`, `BirthdaySlashModule`, `AdminSlashModule` | `BirthdayAnnouncementService` | Feature |
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
