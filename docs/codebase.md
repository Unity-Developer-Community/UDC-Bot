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
├── .vscode/                 # Shared launch profiles, tasks, and extension recommendations
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
├── Components/              # Safe component catalog, lifecycle state, controls, and override persistence
│   ├── ComponentContracts.cs
│   ├── ComponentRegistry.cs
│   └── ComponentOverrideStore.cs
│
├── Data/                    # Data access and external API clients
│   ├── FuzzTable.cs
│   └── UnityAPI.cs
│
├── Domain/                  # Domain models and game logic
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
│   ├── UserModule.cs        # General user commands (text)
│   ├── UserSlashModule.cs   # User slash commands
│   ├── ModerationModule.cs  # Mod commands
│   ├── TipModule.cs         # Tip system
│   ├── ReminderModule.cs    # Reminders
│   ├── TicketModule.cs      # Support tickets
│   ├── EmbedModule.cs       # Embed generation
│   ├── AirportModule.cs     # Flight lookups
│   ├── Casino/              # Casino slash commands
│   ├── UnityHelp/           # Help forum, canned responses, FAQ
│   └── Weather/             # Weather commands
│
├── Services/                # Business logic and background services
│   ├── CommandHandlingService.cs  # Command routing
│   ├── DatabaseService.cs         # PostgreSQL connection/queries
│   ├── UserService.cs             # XP, levels, karma, profile cards
│   ├── LoggingService.cs          # Console/channel/file logging
│   ├── ModerationService.cs       # Audit logging, invite enforcement
│   ├── Casino/              # Token management, game sessions
│   ├── Moderation/          # Moderation sub-services
│   ├── Recruitment/         # Recruitment workflow
│   ├── Rendering/           # Bounded Magick renderers, requests, fonts, HTTP input, smoke tools
│   ├── Tips/                # Tip database management
│   └── UnityHelp/           # Help thread management
│
├── Settings/                # Configuration files
│   ├── CoreSettings.json    # Local non-secret core config (gitignored)
│   ├── FeatureSettings.json # Local non-secret feature config (gitignored)
│   ├── CoreSettings.example.json
│   ├── FeatureSettings.example.json
│   ├── Settings.json        # Legacy read-only compatibility config (gitignored)
│   ├── Rules.json           # Per-channel rules
│   ├── FAQs.json            # FAQ entries
│   ├── Options/             # Narrow domain-owned option types
│   ├── Validation/          # Core-fatal and optional-feature validation
│   └── Legacy/              # Temporary flat-settings projection
│
├── Properties/
│   └── launchSettings.json  # Shared project launch profile
│
├── Skin/                    # Profile card skin rendering system
│   ├── ISkinModule.cs       # Skin module interface
│   ├── SkinData.cs          # Skin configuration model
│   └── *SkinModule.cs       # Individual skin element renderers
│
├── Utils/                   # Utility classes
│
└── SERVER/                  # Runtime-generated data (gitignored)
    ├── log.txt              # Runtime logs
    └── ...
```

## Conventions

### Naming

- **Modules**: `*Module.cs` — Discord command handlers
- **Slash Modules**: `*SlashModule.cs` or `*InteractiveModule.cs` — Interaction-based modules
- **Services**: `*Service.cs` — Business logic, registered as singletons
- **Managed services**: implement `IManagedBotService`; runtime mutation stays disabled until cancellation and restart tests pass
- **Extensions**: `*Extensions.cs` or `*Repository.cs` — Extension methods and DB query helpers
- **Attributes**: `*Attribute.cs` — Custom precondition attributes

### Where to Add New Code

| What | Where |
|------|-------|
| New text command | `Modules/` — add to existing module or create `*Module.cs` |
| New slash command | `Modules/` — add to existing module or create `*SlashModule.cs` |
| New business logic | `Services/` — create `*Service.cs`, register in `Program.cs` |
| New DB queries | `Extensions/` — add to `*Repository.cs` |
| New game type | `Domain/Casino/Games/` — implement `ICasinoGame` |
| New precondition | `Attributes/` — extend `PreconditionAttribute` |
| New skin element | `Skin/` — implement `ISkinModule` |
| Static assets | `Assets/` — fonts, images, skins (baked into Docker image) |
| Runtime data | `SERVER/` — auto-generated, gitignored |

### Testing

- Tests go in `DiscordBot.Tests/`
- Test projects follow the `*.Tests` naming convention
- `Rendering/Fixtures/profile-card-v7-baseline.png` freezes the pre-upgrade reference; tests
  compare structural invariants and normalized RMSE rather than encoded PNG bytes.
- Run `dotnet test --configuration Release`. Use the executable's `--render-smoke` and
  `--render-stress` modes for final-runtime native/font diagnostics without Discord or a database.
