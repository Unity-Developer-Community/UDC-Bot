# UDC-Bot

Discord bot for the Unity Developer Community server: C# 12 on .NET 8, [Discord.Net](https://discordnet.dev/), PostgreSQL. Open source with outside contributors: follow the existing code style.

## Commands

```bash
dotnet restore
dotnet build
dotnet test                          # xUnit, DiscordBot.Tests/ (the casino database tests start PostgreSQL in Docker)
docker compose up db adminer        # local PostgreSQL 16 on :5432, Adminer on :8080
cd DiscordBot && dotnet run          # run the bot (needs Settings/Settings.json, see below)
```

- The bot reads `Settings/`, `SERVER/` and `Assets/` relative to its working directory, so run it from `DiscordBot/` (or set the IDE's working directory there).
- CI (`.github/workflows/ci.yml`) runs restore, build and test in Release on PRs to `master` and `dev`.
- `EnforceCodeStyleInBuild` is on: `.editorconfig` analyzer warnings show up in `dotnet build`.

## Layout

| Path | Holds |
| --- | --- |
| `DiscordBot/Program.cs` | Entry point, settings loading and validation, DI registration |
| `DiscordBot/Modules/<domain>/` | Discord command handlers (text and slash) |
| `DiscordBot/Services/<domain>/` | Business logic, background tasks, event handlers |
| `DiscordBot/Domain/` | Pure logic and models: casino games (`Casino/`), dice notation (`Dice/`) |
| `DiscordBot/Utils/`, `DiscordBot/Data/` | Shared helpers (`IWebClient`, serialization, math) and static data loaders (`FuzzTable`, `UnityAPI`) |
| `DiscordBot/Extensions/` | Extension methods and the Insight.Database repository interfaces (`*Repository.cs`) |
| `DiscordBot/Attributes/` | Custom preconditions (`RequireModerator`, `RequireAdmin`, `BotCommandChannel`, …) |
| `DiscordBot/Skin/` | Profile card renderer: one `ISkinModule` per drawn element, layout in `Assets/skins/skin.json` |
| `DiscordBot/Settings/` | `Settings.json` (gitignored, copy `Settings.example.json`), `Rules.json`, `UserSettings.json`, `FAQs.json`; C# models in `Deserialized/` |
| `DiscordBot/Assets/` | Read-only fonts, images, skins and slot art, baked into the Docker image |
| `DiscordBot/SERVER/` | Runtime data (logs, generated images, caches), gitignored, created on demand |
| `DiscordBot.Tests/` | xUnit tests, mirroring the `DiscordBot/` folders |
| `k8s/dev/`, `k8s/prod/` | Kubernetes manifests, synced to the cluster by Argo CD |

Domains used under `Modules/` and `Services/`: `Profiles`, `Server`, `Fun` (with `Casino/`), `Utils`, `Code`. Cross-domain pieces sit at the root of each folder (`AdminSlashModule`, `BadgeSlashModule`, `CommandHandlingService`, `DatabaseService`, `LoggingService`, …). `docs/features.md` maps each feature to its modules and services.

## Conventions

- **Modules** (`*Module.cs` for text commands on `ModuleBase`, `*SlashModule.cs` / `*InteractiveModule.cs` for `InteractionModuleBase`) stay thin: parse input, call a service, reply. Business logic goes in a service.
- **Modules receive services by public property injection** (`public XpService XpService { get; set; }`); services use constructor injection.
- **Every service is a singleton** registered in `Program.ConfigureServices()`. A new service is not available until it is added there, and singletons are created lazily: a service that only reacts to events or runs a loop must also be resolved in the `Ready` handler in `Program.cs`, or it never starts.
- **Slash commands are registered per guild** (`GuildId` from settings) at startup, not globally.
- **Database access** goes through the Insight.Database repository interfaces in `Extensions/` (`UserDBRepository.cs`, `CasinoRepository.cs`, …; parameterized SQL in `[Sql]` attributes), exposed by `DatabaseService`.
- **Schema:** `DatabaseService` creates missing tables at startup; there is no migration framework, so a change to an existing table needs its own upgrade path for the production data. Identifiers stay unquoted and mapped properties signed (`docs/decisions.md` D2).
- **A text-command module that should appear in `!help`** carries `[Group("UserModule"), Alias("")]`: `ServerModule` builds the help from that group.
- **Settings** are validated at startup (`Validate()` on the deserialized classes): a new required setting gets a validation rule and an entry in `Settings.example.json`.
- **Long-running work from event handlers** uses `SafeFireAndForget()` (`Extensions/TaskExtensions.cs`), which logs exceptions, never a bare `Task.Run` or `async void` without try/catch.
- `IDE0051` (unused private members) is off under `Modules/` because Discord.Net finds command handlers by reflection.

## Never

- Commit `Settings.json`, tokens or API keys. Production secrets come from 1Password through External Secrets (`k8s/*/external-secrets.yaml`).
- Hand-edit the image tag in `k8s/prod/bot.yaml`: the Deploy Prod workflow rewrites it on every push to `master`.

## Deployment

- A push to `master` (unless it only changes `k8s/`, `docs/` or `*.md`) builds `ghcr.io/unity-developer-community/udc-bot:<sha>`, commits the new tag to `k8s/prod/bot.yaml`, and Argo CD syncs it.
- Dev: the **Deploy to Dev** workflow (manual, by PR number or ref) builds `udc-bot-dev:<sha>` and commits the tag to `k8s/dev/bot.yaml` on `master`. A `/build` comment on a PR only builds and pushes the image, then comments the manual steps (set the tag in `k8s/dev/bot.yaml` on the branch, point the `udc-bot-dev` Argo CD app at it).
- Full setup, secrets and backups: `docs/deployment.md`.

## Docs map

| File | Holds |
| --- | --- |
| `README.md` | What the bot is, local setup, FAQ |
| `CONTRIBUTING.md` | Architecture overview and how to add commands, services and preconditions, for human contributors |
| `docs/features.md` | Every feature with its commands, modules and services |
| `docs/casino.md` | Casino architecture and how to add a game |
| `docs/deployment.md` | Deploying with Kubernetes or Docker Compose, secrets, backups |
| `docs/decisions.md` | Decisions whose reason is not visible in the code |
| `docs/research/` | Audits and findings not yet turned into work |
| `docs/plans/` | Plans in progress |

The backlog lives in GitHub Issues (`Unity-Developer-Community/UDC-Bot`): there is no `docs/backlog.md`, and work is cited as `#123`.
