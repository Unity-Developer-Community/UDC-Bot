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
| **Recruitment** | Four-forum configuration, policy and durable state foundation; live handling unavailable pending the coordinator | — | `RecruitService` | Feature |
| **Birthday Announcements** | Scheduled birthday notifications (configurable interval) | — | `BirthdayAnnouncementService` | Feature |
| **Currency Conversion** | Real-time currency conversion | — | `CurrencyService` | Feature |
| **Flight Data** | Airport and flight lookups | `AirportModule` | `AirportService` | Feature |
| **RSS Feeds** | Feed parsing and management | — | `FeedService` | Feature |
| **Embed Builder** | Generate embeds from messages or hastebin URLs | `EmbedModule` | — | Feature |
| **Introduction Watcher** | Monitors introduction channel (toggleable) | — | `IntroductionWatcherService` | Feature |
| **User Extended Data** | Extended user data (default city for weather, etc.) | — | `UserExtendedService` | Feature |
| **Update Checker** | Background bot update checking | — | `UpdateService` | Maintenance |

## Recruitment foundation

Recruitment now has four named forum settings in `FeatureSettings.json`:
`Recruitment:Forums:PaidRecruiting:ChannelId`, `PaidForHire:ChannelId`,
`HobbyRecruiting:ChannelId`, and `HobbyForHire:ChannelId`. Channel names and tag IDs from
the old single-forum settings are not projected into this new mapping. Legacy-enabled
recruitment without the four slots reports a migration error; other features remain available.

**Keep `Recruitment:Enabled` false in this intermediate build.** The previous handler has
been retired. The component reports that its event coordinator is not yet implemented if
started. Observe capture, public messages, tags, guideline publication, and enforcement
will arrive in later chunks. The stored `Mode` and enforcement flags do not activate them.

The tested policy permits one recruiting listing across paid/hobby recruiting and one
for-hire listing across paid/hobby for-hire. Each group has its own 30-day creation/deletion
cooldown. Recent posts in a different forum produce a placement reminder; activity in the
other group does not consume the current group's slot. Missing rates are advisory.
Unanswered accepted listings close after 30 days; unacknowledged attempts are scheduled
for deletion after a successfully delivered 30-minute challenge, with outage/review gates.
Locked/archived listings remain publicly accessible and are not a way to hide rejected posts.

Messages in the four configured forums and their child threads no longer earn XP,
even while recruitment moderation is disabled. Existing XP and karma are retained.
Incomplete or malformed forum mappings are ignored by the XP classifier and reported by
recruitment validation when enabled; they do not stop UserService.

The state foundation uses `{ServerRootPath}/recruitment/recruitment-state.json`, schema v1,
UTC timestamps and decimal-string IDs. It acquires an exclusive writer lock when loaded.
First use requires explicit enrollment; corrupt, incompatible or wrong-guild state is
preserved and prevents writes. Successful updates retain the preceding valid snapshot at
`.json.bak`; explicit backup recovery preserves the replaced primary as `.json.replaced-*`.
There is no prior recruitment JSON schema to migrate automatically, and this intermediate
build does not load or create recruitment state during normal startup. Recovery commands,
retention cleanup, and Discord reconciliation remain future work.

Settings apply on process restart. `GuidelinesDirectory` is a relative subdirectory of
`AssetsRootPath`; template file checks/publication belong to the forthcoming content chunk.
Both dev and prod examples keep all enforcement gates disabled. Development forum IDs and
the staff-feed channel must be filled in before future activation.

## Slash Commands vs Text Commands

The bot supports both paradigms:

- **Text Commands** — Prefix-based (default `!`), implemented in `*Module.cs` classes
- **Slash Commands** — Discord's native slash commands, implemented in `*SlashModule.cs` and `*InteractiveModule.cs` classes

For a full command reference, see the `!Help` command or `/help` slash command in Discord.
