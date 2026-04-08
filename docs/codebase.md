---
post_title: "Codebase Map"
author1: "UDC-Bot Contributors"
post_slug: "codebase"
microsoft_alias: "N/A"
featured_image: ""
categories: []
tags: ["codebase", "architecture", "conventions"]
ai_note: "Generated with AI assistance"
summary: "Directory structure, naming conventions, and guidance for adding new code."
post_date: "2026-04-03"
---

## Top-Level Structure

```text
UDC-Bot/
├── DiscordBot/              # Main application source
├── DiscordBot.Tests/        # Unit tests
├── docs/                    # Project documentation
├── k8s/                     # Kubernetes manifests (production + dev server)
├── Settings/                # Root-level settings (deprecated, use DiscordBot/Settings/)
├── Dockerfile               # Multi-stage Docker build
├── docker-compose.yml       # Local development only (database + bot)
├── DiscordBot.sln           # Solution file
└── NuGet.config             # Custom NuGet feed (Discord.Net nightly)
```

## Application Directory (`DiscordBot/`)

```text
DiscordBot/
├── Program.cs               # Entry point, DI registration, bot startup
├── Constants.cs             # Shared constants (MaxLengthChannelMessage = 2000)
├── GlobalUsings.cs          # Global using directives
├── AssemblyDefinition.cs    # Assembly metadata
│
├── Assets/                  # Static assets (baked into Docker image, read-only)
│   ├── fonts/               # Fonts for profile card rendering
│   ├── images/              # Default images
│   └── skins/               # Profile card skin definitions (skin.json)
│
├── Attributes/              # Custom Discord.Net precondition attributes
│   ├── BotCommandChannelAttribute.cs
│   ├── HideFromHelpAttribute.cs
│   ├── IgnoreBotsAttribute.cs
│   └── RoleAttributes.cs    # RequireModerator, RequireAdmin, etc.
│
├── Data/                    # Data access and external API clients
│   ├── FuzzTable.cs
│   └── UnityAPI.cs
│
├── Domain/                  # Domain models and game logic
│   ├── ProfileData.cs
│   ├── RectangleD.cs
│   └── Casino/              # Casino game abstractions and implementations
│       └── Games/           # Blackjack, Poker, RPS game logic
│
├── Extensions/              # Extension methods and repository helpers
│   ├── CasinoRepository.cs  # Casino DB queries
│   ├── UserDBRepository.cs  # User DB queries
│   ├── ChannelExtensions.cs
│   ├── StringExtensions.cs
│   └── ...
│
├── Modules/                 # Discord command handlers (text + slash)
│   ├── Profiles/            # User profile, rank & birthday commands
│   │   ├── ProfileModule.cs
│   │   ├── RankModule.cs
│   │   └── BirthdayModule.cs
│   ├── Server/              # Server management, moderation, embeds, quotes, reminders
│   │   ├── ServerModule.cs / ServerSlashModule.cs
│   │   ├── TicketModule.cs
│   │   ├── RulesModule.cs
│   │   ├── EmbedModule.cs
│   │   ├── QuoteModule.cs
│   │   └── ReminderModule.cs
│   ├── Fun/                 # Entertainment & games
│   │   ├── FunModule.cs
│   │   ├── DuelSlashModule.cs
│   │   └── Casino/          # Casino slash commands
│   ├── Utils/               # Search, conversion, flights, weather
│   │   ├── SearchModule.cs
│   │   ├── ConvertModule.cs
│   │   ├── AirportModule.cs
│   │   └── Weather/         # Weather commands
│   └── Code/                # Coding tips, Unity help
│       ├── CodeTipModule.cs
│       ├── TipModule.cs
│       └── Unity/UnityHelp/ # Help forum, canned responses, FAQ
│
├── Services/                # Business logic and background services
│   ├── CommandHandlingService.cs  # Command routing (core)
│   ├── DatabaseService.cs         # PostgreSQL connection/queries (core)
│   ├── LoggingService.cs          # Console/channel/file logging (core)
│   ├── UpdateService.cs           # Update checking (core)
│   ├── Profiles/            # Profile cards, XP, karma, birthdays
│   ├── Server/              # Welcome, audit log, embed parsing, reminders
│   ├── Fun/                 # Duels, Miku, Casino/
│   ├── Utils/               # Search, airport, currency, Weather/
│   └── Code/                # Code checking, Tips/, Unity/ (docs, feeds, UnityHelp/)
│
├── Settings/                # Configuration files
│   ├── Settings.json        # Main config (gitignored)
│   ├── Settings.example.json # Template config
│   ├── Rules.json           # Per-channel rules
│   ├── UserSettings.json    # XP/karma/thanks tuning
│   ├── FAQs.json            # FAQ entries
│   └── Deserialized/        # C# classes for deserialized settings
│
├── Skin/                    # Profile card skin rendering system
│   ├── ISkinModule.cs       # Skin module interface
│   ├── SkinData.cs          # Skin configuration model
│   └── *SkinModule.cs       # Individual skin element renderers
│
├── Utils/                   # Utility classes
│
└── SERVER/                  # Runtime-generated data (gitignored)
    ├── images/profiles/     # Generated profile card images
    ├── log.txt              # Runtime logs
    └── ...
```

## Conventions

### Naming

- **Modules**: `*Module.cs` — Discord command handlers
- **Slash Modules**: `*SlashModule.cs` or `*InteractiveModule.cs` — Interaction-based modules
- **Services**: `*Service.cs` — Business logic, registered as singletons
- **Extensions**: `*Extensions.cs` or `*Repository.cs` — Extension methods and DB query helpers
- **Attributes**: `*Attribute.cs` — Custom precondition attributes

### Where to Add New Code

| What | Where |
|------|-------|
| New text command | `Modules/<domain>/` — add to existing module or create `*Module.cs` |
| New slash command | `Modules/<domain>/` — add to existing module or create `*SlashModule.cs` |
| New business logic | `Services/<domain>/` — create `*Service.cs`, register in `Program.cs` |
| New DB queries | `Extensions/` — add to `*Repository.cs` |
| New game type | `Domain/Casino/Games/` — implement `ICasinoGame` |
| New precondition | `Attributes/` — extend `PreconditionAttribute` |
| New skin element | `Skin/` — implement `ISkinModule` |
| Static assets | `Assets/` — fonts, images, skins (baked into Docker image) |
| Runtime data | `SERVER/` — auto-generated, gitignored |

### Module/Service Domain Groups

| Domain | Modules | Services |
|--------|---------|----------|
| **Profiles** | ProfileModule, RankModule, BirthdayModule | ProfileCardService, XpService, KarmaService, KarmaResetService, UserExtendedService, BirthdayAnnouncementService |
| **Server** | ServerModule, ServerSlashModule, TicketModule, RulesModule, EmbedModule, QuoteModule, ReminderModule | ServerService, WelcomeService, AuditLogService, EveryoneScoldService, EmbedParsingService, ReminderService, RecruitService |
| **Fun** | FunModule, DuelSlashModule, Casino/ | DuelService, MikuService, Casino/ |
| **Utils** | SearchModule, ConvertModule, AirportModule, Weather/ | SearchService, AirportService, CurrencyService, Weather/ |
| **Code** | CodeTipModule, TipModule, Unity/UnityHelp/ | CodeCheckService, Tips/, Unity/ (feeds, docs, UnityHelp/) |

### Testing

- Tests go in `DiscordBot.Tests/`
- Test projects follow the `*.Tests` naming convention
