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
| **Recruitment** | Four-forum Observe inventory and Advisory practice with managed Guidelines and owner controls | `/recruitment preview`, `/recruitment publish` | `RecruitService` | Feature |
| **Birthday Announcements** | Scheduled birthday notifications (configurable interval) | — | `BirthdayAnnouncementService` | Feature |
| **Currency Conversion** | Real-time currency conversion | — | `CurrencyService` | Feature |
| **Flight Data** | Airport and flight lookups | `AirportModule` | `AirportService` | Feature |
| **RSS Feeds** | Feed parsing and management | — | `FeedService` | Feature |
| **Embed Builder** | Generate embeds from messages or hastebin URLs | `EmbedModule` | — | Feature |
| **Introduction Watcher** | Monitors introduction channel (toggleable) | — | `IntroductionWatcherService` | Feature |
| **User Extended Data** | Extended user data (default city for weather, etc.) | — | `UserExtendedService` | Feature |
| **Update Checker** | Background bot update checking | — | `UpdateService` | Maintenance |

## Recruitment Observe

Recruitment now has four named forum settings in `FeatureSettings.json`:
`Recruitment:Forums:PaidRecruiting:ChannelId`, `PaidForHire:ChannelId`,
`HobbyRecruiting:ChannelId`, and `HobbyForHire:ChannelId`. Channel names and tag IDs from
the old single-forum settings are not projected into this new mapping. Legacy-enabled
recruitment without the four slots reports a migration error; other features remain available.

The managed coordinator supports **enabled `Mode: Observe`** with valid forum and staff-feed
settings. It captures thread creation/changes/deletion, starter edits, replies and forum
changes; inventories active and archived posts; and maintains one staff-feed entry per
observed attempt. Entries show known facts, eligibility findings, placement reminders,
previous-post links and evidence gaps. Observe does not publish public messages, create tags,
change Guidelines, accept listings, or delete/lock/archive posts. Advisory now adds the public
practice workflow described in [Recruitment Advisory](recruitment.md). Enforce startup is
still rejected. Checked-in deployments remain disabled.

The tested policy foundation permits one recruiting listing across paid/hobby recruiting
and one for-hire listing across paid/hobby for-hire. Automatic enforcement remains unavailable. Each group has its own 30-day creation/deletion
cooldown. Recent posts in a different forum produce a placement reminder; activity in the
other group does not consume the current group's slot. Missing rates are advisory.
Unanswered accepted listings close after 30 days; unacknowledged attempts are scheduled
for deletion after a successfully delivered 30-minute challenge, with outage/review gates.
Locked/archived listings remain publicly accessible and are not a way to hide rejected posts.

Messages in the four configured forums and their child threads no longer earn XP,
even while recruitment moderation is disabled. Existing XP and karma are retained.
Incomplete or malformed forum mappings are ignored by the XP classifier and reported by
recruitment validation when enabled; they do not stop UserService.

The state store uses `{ServerRootPath}/recruitment/recruitment-state.json`, schema v3,
UTC timestamps and decimal-string IDs. It acquires an exclusive writer lock when loaded.
The first enabled Observe start enrolls a missing, backup-free state file. Existing posts
are imported as unverified; their past acknowledgement/acceptance is never invented.
Corrupt, incompatible or wrong-guild state is preserved and prevents writes. Successful
updates retain the preceding valid snapshot at
`.json.bak`; explicit backup recovery preserves the replaced primary as `.json.replaced-*`.
The v1 foundation schema upgrades with a preserved backup and review/coverage flags.
The v2 Observe schema upgrades with empty publication/control metadata while preserving
its evidence and acceptance history.
Recovery commands and retention cleanup remain future work; do not delete state to recover.

One worker drains a bounded 256-event queue and checks due work every 30 seconds. Active
inventory refreshes every five minutes; archive scans use saved 100-thread pages, resume
after restart, catch up the archive head after gaps, and repeat every six hours. Each tick
checks at most eight posts (100 replies each) and eight feed entries. Confirmed replies
remain evidence after deletion. Missing threads, offline gaps, and historical messages
whose original staff roles cannot be established remain uncertain; natural archive alone
does not close a listing. Health exposes incomplete inventories, evidence gaps, pending
feed delivery and queue overflow counts.

Feed sends first persist an intent. An interrupted send is recovered by paginated lookup
of a bot-owned stable marker before retrying; saved entries are refreshed in place. Discord
delivery and local persistence are separate operations, so delivery is reconciled rather
than claimed to be transactional. Do not edit/remove the marker at the end of feed entries.
The configured feed has its own Discord retention: local future metadata cleanup will not
remove staff-feed messages. Post bodies are not stored in recruitment state.

Settings apply on process restart. `GuidelinesDirectory` is a relative subdirectory of
`AssetsRootPath`; Advisory validates four Markdown templates and publishes native forum topics.
Both dev and prod examples keep all enforcement gates disabled. Development forum IDs and
the staff-feed channel must be filled in before Observe activation. Stop unsubscribes and
drains owned work before releasing the writer. Component toggle/restart controls remain
deferred; stop the process or disable recruitment in settings and restart it.

### Maintaining the Observe coordinator

Start with `RecruitmentObservationCoordinator.TickAsync`: it inventories forums, checks a
bounded batch of posts, updates due staff-feed entries, then summarizes health. `HandleAsync`
records gateway observations between ticks. `RecruitService` owns the single worker, so
these operations run sequentially. State-store callbacks change metadata only; keep Discord
and database calls outside them to avoid holding the writer lock during network requests.

Three distinctions matter when changing this code:

- **Observed response versus proven absence.** A qualifying reply remains evidence even if
  later deleted. Catch-up cannot recover replies deleted during an outage, so completing a
  history scan must not clear an existing uncertainty flag.
- **Imported post versus newly observed post.** Enrollment determines which posts predate
  the feature; the current process start determines which posts may have missed events.
  Neither establishes prior acknowledgement or acceptance. Discord's archive/lock flags
  also do not establish a bot policy transition.
- **Pending send versus failed send.** Discord may accept a feed message before the bot loses
  its response or fails to save the ID. `PublishFeedAsync` refreshes a known entry, calls
  `RecoverPendingFeedAsync` for an uncertain send, and only then records a new send intent.
  An incomplete marker search must finish before another message can be sent.

`RecruitmentObservationMessage.Build` lists the feed's sections in display order. Its
`Describe…` helpers handle wording and conditional details; they do not change policy or
state. The final recovery marker must survive truncation because delivery recovery uses it
to identify the entry. Recovery and lifetime regressions are covered in
`DiscordBot.Tests/Recruitment/RecruitmentObservationTests.cs`; adapter tests use the pinned
Discord.Net transport without connecting to Discord.

## Slash Commands vs Text Commands

The bot supports both paradigms:

- **Text Commands** — Prefix-based (default `!`), implemented in `*Module.cs` classes
- **Slash Commands** — Discord's native slash commands, implemented in `*SlashModule.cs` and `*InteractiveModule.cs` classes

For a full command reference, see the `!Help` command or `/help` slash command in Discord.
