---
post_title: "Features"
author1: "UDC-Bot Contributors"
post_slug: "features"
microsoft_alias: "N/A"
featured_image: ""
categories: []
tags: ["features"]
ai_note: "Generated with AI assistance"
summary: "Complete feature list for UDC-Bot with descriptions and ownership."
post_date: "2026-04-03"
---

## Feature List

| Feature | Description | Module(s) | Service(s) | Criticality |
|---------|-------------|-----------|------------|-------------|
| **User Profiles** | XP/level system, karma tracking, profile cards with customizable skins | `UserModule`, `UserSlashModule` | `UserService` | Core |
| **Moderation** | Mute, kick, ban, slowmode, message clear, single/bulk deletion and bounded thread/forum-post audit logging, invite enforcement | `ModerationModule` | `ModerationService` | Core |
| **Command Handling** | Text command + slash command routing, history tracking, prefix config | — | `CommandHandlingService` | Core |
| **Logging** | Multi-destination logging (console, file, Discord channel) with severity levels | — | `LoggingService` | Core |
| **Database** | PostgreSQL connection pooling, user/casino repositories | — | `DatabaseService` | Core |
| **Casino** | Token economy, Blackjack, Poker, Rock Paper Scissors, daily rewards, leaderboards | `CasinoSlashModule` | `CasinoService`, `GameService` | Feature |
| **Weather** | Temperature, conditions, air quality, local time via OpenWeatherMap | `WeatherModule` | `WeatherService` | Feature |
| **Reminders** | Persistent scheduled reminders with natural time parsing | `ReminderModule` | `ReminderService` | Feature |
| **Tips** | Searchable tip database with image support, keyword lookups | `TipModule` | `TipService` | Feature |
| **Tickets** | Private complaint/support ticket channels | `TicketModule` | — | Feature |
| **Unity Help** | Help forum thread management, auto-archive, canned responses, FAQ, resources | `UnityHelpModule`, `CannedResponseModule`, `GeneralHelpModule`, `UnityHelpInteractiveModule`, `CannedInteractiveModule` | `UnityHelpService`, `CannedResponseService` | Core |
| **Recruitment** | Four-forum observation, practice and gated enforcement with owner/moderator controls | `/recruitment` | `RecruitmentService` | Feature |
| **Birthday Announcements** | Scheduled birthday notifications (configurable interval) | — | `BirthdayAnnouncementService` | Feature |
| **Currency Conversion** | Real-time currency conversion | — | `CurrencyService` | Feature |
| **Flight Data** | Airport and flight lookups | `AirportModule` | `AirportService` | Feature |
| **RSS Feeds** | Feed parsing and management | — | `FeedService` | Feature |
| **Embed Builder** | Generate embeds from messages or hastebin URLs | `EmbedModule` | — | Feature |
| **Introduction Watcher** | Monitors introduction channel (toggleable) | — | `IntroductionWatcherService` | Feature |
| **User Extended Data** | Extended user data (default city for weather, etc.) | — | `UserExtendedService` | Feature |
| **Update Checker** | Background bot update checking | — | `UpdateService` | Maintenance |

## Recruitment

Four configured forums support paid/hobby Recruiting and ForHire. Observe reports to staff;
Advisory adds Guidelines and practice owner controls; Enforce adds acceptance and independently
gated lifecycle actions. Each author can maintain one listing per group with a separate
30-day repost wait. Cross-group posting and missing payment details produce advice.

Messages in configured recruitment forums/threads earn no XP, even while the feature is
disabled; existing totals are retained. Recruitment supports component toggle/restart.
Checked-in deployments remain disabled.

See the [recruitment guide](recruitment.md) for setup, owner/moderator controls, recovery,
retention, validation and the source navigation map. Deployment permissions are in the
[deployment guide](deployment.md).

## Slash Commands vs Text Commands

The bot supports both paradigms:

- **Text Commands** — Prefix-based (default `!`), implemented in `*Module.cs` classes
- **Slash Commands** — Discord's native slash commands, implemented in `*SlashModule.cs` and `*InteractiveModule.cs` classes

For a full command reference, see the `!Help` command or `/help` slash command in Discord.
