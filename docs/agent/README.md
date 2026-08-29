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
| `DiscordBot/Settings/CoreSettings.json` | Non-secret core config (gitignored; copy the example) |
| `DiscordBot/Settings/FeatureSettings.json` | Non-secret feature config (gitignored; copy the example) |
| `DiscordBot/Settings/Options/` | Domain-owned configuration types |
| `DiscordBot/Components/` | Component metadata, health, lifecycle, and override registry |
| `DiscordBot/Assets/` | Static assets (fonts, images, skins) — baked into Docker image |
| `DiscordBot/SERVER/` | Runtime-generated data (gitignored) |
| `DiscordBot/Domain/Casino/` | Casino game abstractions and implementations |
| `k8s/dev/`, `k8s/prod/` | Kubernetes manifests |

## Key Invariants

- All services are registered as **singletons** in `Program.cs`.
- `CoreSettings.json` and `FeatureSettings.json` are **never committed** — copy their example files.
- Secrets use documented `UDCBOT_` environment variables; do not add them to JSON or ConfigMaps.
- Legacy `Settings.json`/`UserSettings.json` are read-only compatibility inputs and must not gain new fields.
- `SERVER/` is runtime data and **gitignored**.
- `Assets/` is read-only static content loaded via `AssetsRootPath` (default `./Assets`).
- Profile card skins load from `${AssetsRootPath}/skins/skin.json`.
- Docker image bakes `Assets/` into the image at build time; `Settings/` and `SERVER/` are mounted as volumes.
- Text commands use `CommandService`; slash commands use `InteractionService`.
- Slash commands are registered per-guild using `GuildId` from settings.

## Config Files

| File | Format | Purpose |
|------|--------|---------|
| `CoreSettings.json` | JSON | Non-secret guild, storage, command, logging, and authorization settings |
| `FeatureSettings.json` | JSON | Non-secret domain feature settings and defaults |
| `CoreSettings.example.json` | JSON | Template for core settings |
| `FeatureSettings.example.json` | JSON | Template for feature settings |
| `Settings.json` | JSON | Temporary read-only legacy compatibility input |
| `Rules.json` | JSON | Per-channel rule definitions |
| `UserSettings.json` | JSON | Temporary legacy user-activity compatibility input |
| `FAQs.json` | JSON | FAQ entries for canned responses |

## Database

PostgreSQL 16 — connection string in `UDCBOT_Database__ConnectionString`. Tables are auto-created
on first run. Docker Compose service name: `db` (port 5432).
