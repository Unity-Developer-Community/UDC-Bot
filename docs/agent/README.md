---
last-updated: "2026-04-03"
applicable: ["**"]
owner: "UDC-Bot Contributors"
---

## Purpose

Operational reference for automated agents working on the UDC-Bot codebase.

## Project Type

C# .NET 8.0 Discord bot using Discord.Net 3.17.4.

## Build & Run

```bash
dotnet restore
dotnet build DiscordBot/DiscordBot.csproj
dotnet run --project DiscordBot/DiscordBot.csproj
```

Docker Compose (local dev only, database + bot):

```bash
docker-compose up db
```

Full local stack:

```bash
docker-compose up --build
```

Production and dev server use **Kubernetes** (`k8s/prod/` and `k8s/dev/`).

## Important Paths

| Path | Purpose |
|------|---------|
| `DiscordBot/Program.cs` | Entry point, DI registration |
| `DiscordBot/Services/` | All business logic services |
| `DiscordBot/Modules/` | Discord command handlers |
| `DiscordBot/Settings/Settings.json` | Main config (gitignored, use `Settings.example.json` as template) |
| `DiscordBot/Settings/Deserialized/` | C# models for settings |
| `DiscordBot/Assets/` | Static assets (fonts, images, skins) — baked into Docker image |
| `DiscordBot/SERVER/` | Runtime-generated data (gitignored) |
| `DiscordBot/Domain/Casino/` | Casino game abstractions and implementations |
| `k8s/dev/`, `k8s/prod/` | Kubernetes manifests |

## Key Invariants

- All services are registered as **singletons** in `Program.cs`.
- `Settings.json` is **never committed** — use `Settings.example.json` as template.
- `SERVER/` is runtime data and **gitignored**.
- `Assets/` is read-only static content loaded via `AssetsRootPath` (default `./Assets`).
- Profile card skins load from `${AssetsRootPath}/skins/skin.json`.
- Docker image bakes `Assets/` into the image at build time; `Settings/` and `SERVER/` are mounted as volumes.
- Text commands use `CommandService`; slash commands use `InteractionService`.
- Slash commands are registered per-guild using `GuildId` from settings.

## Config Files

| File | Format | Purpose |
|------|--------|---------|
| `Settings.json` | JSON | Bot token, DB connection, channel/role IDs, feature toggles |
| `Settings.example.json` | JSON | Template for `Settings.json` (committed to repo) |
| `Rules.json` | JSON | Per-channel rule definitions |
| `UserSettings.json` | JSON | XP/karma/thanks tuning parameters |
| `FAQs.json` | JSON | FAQ entries for canned responses |

## Database

MySQL — connection string in `Settings.json`. Tables are auto-created on first run.
Docker Compose service name: `db` (port 3306).
