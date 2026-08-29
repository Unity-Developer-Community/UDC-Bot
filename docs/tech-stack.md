---
post_title: "Tech Stack"
author1: "UDC-Bot Contributors"
post_slug: "tech-stack"
microsoft_alias: "N/A"
featured_image: ""
categories: []
tags: ["tech-stack", "dependencies"]
ai_note: "Generated with AI assistance"
summary: "Primary languages, runtimes, and dependencies for UDC-Bot."
post_date: "2026-04-03"
---

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
| MySql.Data | MySQL database driver |
| Insight.Database | Micro ORM for database access |
| Magick.NET-Q8-x64 14.16.0 | Q8 image processing (profile cards and skins), Linux/Windows/macOS x64 host support |
| HtmlAgilityPack | HTML parsing |
| System.ServiceModel.Syndication | RSS feed parsing |
| Pathoschild.NaturalTimeParser | Natural language time parsing (reminders) |

## Infrastructure

| Component | Details |
|-----------|---------|
| Database | MySQL (containerized or standalone) |
| Container Runtime | Docker with multi-stage, framework-dependent `linux-x64` publish |
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
| PhpMyAdmin | Database administration UI (port 8080) |
| MSTest 4.3.3 | Renderer regression, bounds, failure, and concurrency tests |

## Profile Rendering Runtime

- The deployment image is Linux AMD64; Docker rejects non-AMD64 target builds because the
  selected Magick package contains x64 native libraries.
- Profile text resolves explicit files from `DiscordBot/Assets/fonts` rather than host font
  names. The Docker build runs a render before installing Core Fonts to enforce this boundary.
- Avatar downloads are HTTPS-only and bounded to 2 MiB and five seconds. Decode dimensions,
  final dimensions, PNG bytes, native memory, disk cache, threads, and execution time are also
  bounded for the 512 MiB production container.
- Profile cards are stripped 8-bit PNGs returned in memory; no username-derived render files
  are persisted.
