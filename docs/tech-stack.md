# Tech Stack

## Runtime & Language

| Component | Version |
|-----------|---------|
| Target Framework | .NET 8.0 |
| Language | C# 12 |
| Nullable Reference Types | Enabled |

## Core Dependencies

Versions are defined in `DiscordBot/DiscordBot.csproj` (single source of truth).

| Package | Purpose |
|---------|---------|
| Discord.Net | Discord API client (commands, interactions, gateway) |
| Newtonsoft.Json | JSON serialization/deserialization |
| Microsoft.Extensions.DependencyInjection | DI container |
| Insight.Database | Micro ORM for database access |
| Insight.Database.Providers.PostgreSQL | PostgreSQL provider (Npgsql) for Insight |
| Magick.NET-Q8-x64 | Image processing (profile cards, skins) |
| HtmlAgilityPack | HTML parsing |
| System.ServiceModel.Syndication | RSS feed parsing |
| Pathoschild.NaturalTimeParser | Natural language time parsing (reminders) |

## Infrastructure

| Component | Details |
|-----------|---------|
| Database | PostgreSQL (containerized or standalone) |
| Container Runtime | Docker with multi-stage builds |
| Production Deployment | **Kubernetes** (`k8s/prod/`) — primary deployment method |
| Dev Server | **Kubernetes** (`k8s/dev/`) |
| Local Development | **Docker Compose** — database + optional bot for local dev/testing only |
| Secret Management | Kubernetes Secrets + External Secrets (HashiCorp Vault) |
| Package Manager | NuGet (with custom Discord.Net nightly feed via `NuGet.config`) |

## Development Tools

| Tool | Purpose |
|------|---------|
| Visual Studio / VS Code / Rider | IDE |
| Docker Compose | Local database + bot for dev/testing only |
| Adminer | Database administration UI (port 8080) |
