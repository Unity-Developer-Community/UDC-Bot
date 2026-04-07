# Code Quality Audit Report

**Date:** 2026-04-03  
**Scope:** Full codebase review — refactoring opportunities, code duplication, patterns, bugs  
**Excludes:** New features, test implementation (gaps noted only)

---

## Executive Summary

The UDC-Bot codebase has a solid feature set but suffers from several structural
issues common in organically grown projects. The most impactful problems are:

1. **God classes** — `UserService`, `BotSettings`, and `UserModule` carry too many
   responsibilities.
2. **Zero test coverage** — the `DiscordBot.Tests/` project is empty.
3. **Thread-safety bugs** — multiple shared collections accessed without
   synchronization.
4. **Resource leaks** — `HttpClient` created per-request instead of reused;
   database connections potentially undisposed.
5. **Pervasive code duplication** — embed building, error handling, and HTTP
   patterns repeated dozens of times.

---

## Findings by Category

### 1. God Classes & SRP Violations

| Class | File | Responsibilities | Severity |
|-------|------|-----------------|----------|
| `UserService` | `Services/UserService.cs` | XP, karma, muting, code formatting warnings, everyone-mention scold, profile card generation, welcome messages, avatar ops, data persistence, level calculation (~11 concerns) | Critical |
| `BotSettings` | `Settings/Deserialized/Settings.cs` | 60+ properties across channels, roles, API keys, casino, tips, recruitment, weather in one flat class | High |
| `UserModule` | `Modules/UserModule.cs` | 1 000+ lines; text commands, web scraping, role management, search, profile display all in one module | High |
| `UpdateService` | `Services/UpdateService.cs` | Bot data, user muting lifecycle, FAQ loading, RSS feeds, Wikipedia downloading (5 concerns) | High |
| `CasinoSlashModule` | `Modules/Casino/CasinoSlashModule.cs` | 500+ lines; token commands, game commands, admin commands, statistics, nested `TokenCommands` class | High |
| `WebUtil` | `Utils/WebUtil.cs` | HTTP fetching, HTML parsing, JSON deserialization, error handling, logging (5 concerns) | Medium |
| `ICasinoRepo` | `Extensions/CasinoRepository.cs` | 37+ SQL method signatures in one interface | Medium |

**Recommended splits:**

- `UserService` → `XpService`, `KarmaService`, `ProfileCardService`,
  `WelcomeService`, `CodeFormattingService`
- `BotSettings` → `ChannelSettings`, `RoleSettings`, `CasinoSettings`,
  `TipSettings`, `RecruitmentSettings`, `ApiKeySettings`
- `ICasinoRepo` → `ICasinoUserRepo`, `ITokenTransactionRepo`,
  `ICasinoAdminRepo`

---

### 2. Code Duplication

#### 2a. `HttpClient` creation (repeated everywhere)

Files: `AirportService.cs`, `UserService.cs`, `TipService.cs`, `WebUtil.cs`

```csharp
// Pattern found in 4+ places
using (var http = new HttpClient()) { ... }
HttpClient client = new();  // sometimes without using
```

**Fix:** Register a shared `IHttpClientFactory` in DI and inject it.

#### 2b. Embed construction boilerplate

Files: `ModerationService.cs`, `RecruitService.cs`, `CannedResponseService.cs`,
and ~20 other locations.

```csharp
var builder = new EmbedBuilder()
    .WithColor(color)
    .WithTimestamp(...)
    .FooterInChannel(...);
```

**Fix:** Create an `EmbedFactory` helper with preset methods per use-case.

#### 2c. Service name constant

Every service file declares `private const string ServiceName = "...";`
identically. Could be extracted to a base class or generated via `nameof`.

#### 2d. Fire-and-forget `Task.Run` with pragma suppression

Files: `RecruitService.cs`, `UnityHelpService.cs`

```csharp
#pragma warning disable CS4014
Task.Run(() => ...);
#pragma warning restore CS4014
```

**Fix:** Create a `SafeFireAndForget()` extension that logs exceptions.

#### ~~2e. `ContainsInviteLink()` — three identical overloads~~ (removed — dead code, no callers)

File: `MessageExtensions.cs` — same regex for `IUserMessage`, `string`, and
`IMessage`. Should be a single implementation on `string` with the others
delegating.

#### 2f. Cooldown calculation pattern

File: `UserServiceExtensions.cs` — `Days()`, `Hours()`, `Minutes()`,
`Seconds()`, `Milliseconds()` all follow the exact same structure with
`cooldowns.HasUser()` check.

#### 2g. Weather command overloads

File: `WeatherModule.cs` — each weather command (Temp, Weather, Pollution, Time)
duplicated as `(IUser)` and `(params string[])` overloads with near-identical
bodies.

#### 2h. Mute logic overloads

File: `ModerationModule.cs` — three `MuteUser` overloads with near-identical
role-add / logging / cooldown / DM logic.

---

### 3. Potential Bugs

#### 3a. Thread-safety issues (Critical)

| Location | Issue |
|----------|-------|
| `Program.cs` — `_isInitialized` flag | Not thread-safe; `Ready` event could fire twice before flag is set. Use `Interlocked.CompareExchange`. |
| `GameService.cs` — `List<IDiscordGameSession>` | Plain `List<T>` mutated from multiple event handlers. Use `ConcurrentDictionary` or lock. |
| `UserService.cs` — `_xpCooldown` dictionary | Modified from multiple async tasks without synchronization. |
| `ReminderService.cs` — `_reminders` list | Modified while iterating in `CheckReminders`. |
| `GameSession.cs` — `GameData` dictionary | `AddPlayer` / `RemovePlayer` without locks. |
| `RecruitService.cs` — `_botSanityCheck` dictionary | Used as a lock mechanism but is not thread-safe. |

#### 3b. Resource leaks (High)

| Location | Issue |
|----------|-------|
| `WebUtil.cs` — `new HttpClient()` per call | Starves sockets under load. Use `IHttpClientFactory` or a static instance. |
| `AirportService.cs` — `HttpClient client = new()` | Created without `using`, never disposed. |
| `DatabaseService.cs` — `Query` property | Returns new `MySqlConnection` each access; may never be disposed by caller. |
| `UserService.cs` — `GenerateProfileCard()` | MagickImage objects not consistently disposed. |
| `FuzzTable.cs` — `File.ReadLines()` | Not wrapped in `using`; handle may leak on exception. |

#### 3c. Null-reference risks (Medium)

| Location | Issue |
|----------|-------|
| `ModerationService` — `MessageDeleted` handler | `_botAnnouncementChannel.Id` — channel could be null. |
| `FeedService` — `HandleFeed` method | `item.Links[0]` — no bounds check. |
| `UserExtensions` — `HasRoleGroup` overload | `user as SocketGuildUser` result used without null check. |
| `ContextExtension` — `IsOnlyReplyingToAuthor` | `context.Message.ReferencedMessage.Author.Id` — no null check. |
| `SkinModuleJsonConverter` — `ReadJson` | `Type.GetType(t)` can return null; `jo["Type"]` can be null. |
| `CustomTextSkinModule` — `GetDrawables` | `prop.GetValue(data, null)` cast to `dynamic` — value could be null. |
| `RoleAttributes` — `CheckPermissionsAsync` | Direct cast `(SocketGuildUser)` crashes in DM context. |

#### 3d. Logic bugs

| Location | Issue | Severity |
|----------|-------|----------|
| `EmbedModule.cs` — `SendEmbedToChannel` reaction-polling loop | `i++` inside a `for(int i=0; i<10; i++)` loop — counter incremented twice per iteration, halving the confirmation window from 20s to 10s. Additionally, the loop continues polling after confirmation is received (no `break` on `confirmedEmbed = true`). | High |
| `WeatherModule.cs` — `WeatherEmbed` sunrise/sunset block | `res.sys.sunrise > 0` checked twice; second should be `res.sys.sunset`. Copy-paste bug. Low impact: output uses correct `sunset` variable, but sunset line is suppressed when `sunrise == 0 && sunset > 0`. | Low |
| `StringExtensions.cs` — `MessageSplitToSize` | If no newlines exist, `LastIndexOf("\n")` returns -1/0, risking infinite loop or empty string. | Medium |
| `AirportModule.cs` — `FlyTo` day-of-week calc | Day-of-week calculation may have off-by-one when Sunday (`DayOfWeek = 0`) is involved. | Medium |
| `Blackjack` — `DoubleDown` method | No check that player has sufficient tokens before doubling bet. Could create negative balances. | Medium |

#### 3e. `async void` event handlers (Medium-High)

Several event subscriptions use `async void` delegate signatures (e.g.,
`_client.MessageReceived += Thanks` in `UserService`). `async void` methods are
fire-and-forget: unhandled exceptions inside them crash the process instead of
being caught. All async event handlers should be wrapped in try-catch or use a
safe-fire-and-forget pattern.

#### 3f. Session / memory leaks (Medium)

| Location | Issue |
|----------|-------|
| `GameSession.cs` | `ExpiryTime` commented out — sessions can live forever with no cleanup. |
| `Program.cs` | All services are `Singleton` — never disposed; database connections held forever. |
| `Program.cs` | `await Task.Delay(-1)` — no graceful shutdown; no `CancellationToken`. |

---

### 4. Long Methods (> 50 lines)

| File | Method | ~Lines | Issue |
|------|--------|--------|-------|
| `UserService.cs` | Constructor | 150 | Initialization, regex compilation, event hookup all mixed |
| `UserService.cs` | `GenerateProfileCard()` | 150 | DB queries, image manipulation, HTTP, file I/O in one method |
| `UserService.cs` | `Thanks()` | 100 | Regex matching, DB calls, cooldown checks combined |
| `UserModule.cs` | `SearchResults` | 120+ | Web scraping, HTML parsing, URL manipulation, embed building |
| `FeedService.cs` | `GetReleaseNotes()` | 100 | Complex HTML parsing with nested loops |
| `AirportModule.cs` | `FlyTo` | 95 | API calls, coordinate lookups, embed building |
| `EmbedModule.cs` | `SendEmbedToChannel` | 90+ | Reaction polling, message creation, confirmation |
| `UpdateService.cs` | `DownloadDocDatabase()` | 80 | Web scraping, parsing, file I/O |
| `CasinoSlashModule.cs` | `DisplayTransactionHistory` | 80+ | Query, pagination, admin checks, embed formatting |
| `PokerHelper.cs` | Hand evaluation | 200 | Complex hand ranking with edge cases |

---

### 5. Hardcoded Values That Should Be In Config

| File | Value | Purpose |
|------|-------|---------|
| `UserService.cs` | `39` minutes | Miku cooldown |
| `UserService.cs` | `800` | Code block warning length threshold |
| `UnityHelpService.cs` | `10` min, `14` hr, `20` hr, `3` days | Thread close/idle timers |
| `RecruitService.cs` | `120` chars, `60` sec | Min message length, delete delay |
| `ReminderService.cs` | `10` | Max reminders per user |
| `AirportService.cs` | API URLs | Test vs production URLs |
| `TipService.cs` | `"tips.json"` | Filename |
| `StringExtensions.cs` | `1990` | Discord max message length (should use constant) |
| `UserServiceExtensions.cs` | `9999` days | "Permanent" duration |
| `WeatherModule.cs` | `22.5, 67.5, 112.5...` | Wind direction angles |
| `CasinoSlashModule.Games.cs` | Game name mapping | Hardcoded switch expression |
| `Skin modules` | Pixel coordinates | Layout-specific X/Y positions |
| `BotSettings` | `300`, `21600`, `86400` seconds | Various delay timers |

---

### 6. Architecture & Design Issues

#### 6a. Business logic in command handlers

Several modules contain significant business logic that should live in services:

- `AirportModule.FlyTo` — flight calculation and coordinate fetching
- `UserModule.SearchResults` — web scraping and HTML parsing
- `WeatherModule.TemperatureEmbed` — formatting and calculation
- `CasinoSlashModule.DisplayTransactionHistory` — complex query and pagination
- `UserSlashModule.Duel` — AI action loops, timeout handling, component builders
- `TipModule.Tip` — file path handling, attachment creation, DB persistence

#### 6b. Static mutable state in modules

`UserSlashModule._activeDuels` is a `ConcurrentDictionary` held as static state
in a module. This should be in a service for proper lifecycle management and
recovery.

#### 6c. Inconsistent command patterns

- Mixed text commands (`UserModule`) vs slash commands (`UserSlashModule`) for
  similar functionality.
- Inconsistent use of `Priority` attribute across modules.
- Inconsistent alias patterns.
- Different `InteractionModuleBase` generic parameterization.
- Event handler naming varies: `MessageReceived` vs `OnMessageReceived` vs
  `GatewayOnMessageReceived`.

#### 6d. No configuration validation

`BotSettings` has no `Validate()` method. Critical fields like `Token`,
`GuildId`, `DbConnectionString` could be empty/null/zero without detection until
a runtime crash.

#### 6e. Singleton-only DI

`Program.cs` registers every service as `Singleton`. No consideration for
`Scoped` or `Transient` lifetimes. Services holding database connections or
disposable resources are never cleaned up.

#### 6f. No graceful shutdown

`await Task.Delay(-1)` blocks forever. No `CancellationToken`, no shutdown
signal handling, no resource cleanup on exit.

---

### 7. Skin System Issues

| Issue | Severity | Details |
|-------|----------|---------|
| Duplicate `RectangleD` struct | Low | Defined in both `Skin/RectangleD.cs` and `Domain/RectangleD.cs` |
| Reflection in `SkinModuleJsonConverter` | Medium | `Type.GetType()` on every deserialization; no caching; no null check |
| Magic threshold in avatar color sampling | Medium | `650` RGB sum threshold unexplained |
| Inconsistent coordinate types | Low | Some modules use `int`, others use `double` |
| Hardcoded pixel positions | Medium | Skin modules have layout-specific coordinates baked in |
| `CustomTextSkinModule` null risk | Medium | `prop.GetValue()` result cast to `dynamic`, `.ToString()` called without null check |
| Text rendering setup duplication | Medium | All text skin modules repeat the same `StrokeColor`/`FillColor`/`FontPointSize` initialization |

---

### 8. Dead Code & Commented-Out Code

| File | What | Notes |
|------|------|-------|
| `UserService.cs` | `MikuCheck` event subscription | Commented out |
| `UserService.cs` | `_mikuCooldownTime` initialization | Commented out |
| `UpdateService.cs` | `UpdateUserRanks` task | Commented out |
| `AirportModule.cs` | Flight details (seats, bags, fees) | Commented out |
| `UserModule.cs` | Entire `CompileCode` method | Commented out with note "Not really a required feature" |
| `FeedService.cs` | TODO about other entities | Stale |
| `TipService.cs` | TODO about image attachment | Stale |
| `GameSession.cs` | `ExpiryTime`, `UserId` properties | Commented out |
| `DiscordGameSession.cs` | "Reload Embed" and "Custom" bet buttons | Commented out |

---

### 9. Missing Error Handling

| File | Method | Issue |
|------|--------|-------|
| `AirportService.cs` | `GetFlightTickets()` | Returns null without logging |
| `DatabaseService.cs` | Constructor | Bare catch block; continues silently |
| `ReminderService.cs` | `LoadReminders()` | No handling for corrupted file |
| `TipService.cs` | `CommitTipDatabase()` | No try-catch for file write failures |
| `UnityHelpService.cs` | Message fetching | Missing null checks on `GetMessageAsync` results |
| `TicketModule.cs` | `Complaint` | No validation that `Settings.ComplaintCategoryId` is valid |
| `CasinoSlashModule.Games.cs` | `DoAction` | `Enum.Parse` with no try-catch |
| `Program.cs` | `DeserializeSettings()` | No error handling; exception propagates uncaught |
| `Program.cs` | `Ready` handler | No try-catch around service initialization |

---

### 10. Security Concerns

| Location | Issue | Severity |
|----------|-------|----------|
| `EmbedModule.cs` — `BuildEmbedFromUrl` | SSRF risk: `IsValidHost()` allow-list exists but `attachment.Url` from Discord CDN bypasses it. Pastebin/hastebin URLs could also contain redirects. User-supplied URLs are downloaded server-side. | Medium |
| `EmbedModule.cs` | Uses deprecated `WebClient` (obsolete since .NET 6) — should switch to `HttpClient` via `IHttpClientFactory` | Low |
| `CasinoRepository.cs` — 37+ SQL methods | SQL injection surface is large. All queries are likely parameterized via Insight.Database, but this should be verified explicitly. | Low (verify) |
| `RoleAttributes.cs` | ~~Direct cast `(SocketGuildUser)` crashes in DM context — precondition bypass could allow unauthorized command execution if the exception is caught upstream~~ ✅ Fixed | ~~Medium~~ |

---

### 11. Naming Inconsistencies

| Pattern | Examples | Issue |
|---------|----------|-------|
| Service name strings | Some include "Service" suffix, some don't | Inconsistent |
| Method naming | `GetOrAddUser` vs `GetOrCreateCasinoUser` | No consistent convention |
| Async method naming | `Thanks()` (async void) vs `Thanks` (async Task) | Some void, some Task |
| Private field prefix | `_settings` in some classes, `Settings` in others | Inconsistent underscore |
| Event handlers | `MessageReceived`, `OnMessageReceived`, `GatewayOnMessageReceived` | Three different conventions |
| Data model casing | `Kategory` vs `Category`, `Keyimage` vs `KeyImage`, `Pubdate` vs `PubDate` in `UnityAPI.cs` | Inconsistent |
| `HasAnyPingableMention` | Exists in both `MessageExtensions` and `ContextExtension` with different behavior | Confusing |

---

### 12. Test Coverage

**Current state: 0%** — `DiscordBot.Tests/` exists as an empty project stub with
no `.cs` files and no test framework configured.

**Highest-priority areas for testing:**

1. Attribute preconditions (role checks, channel checks)
2. Extension methods (string splitting, cooldown logic, message extensions)
3. Casino game logic (hand evaluation, bet validation, session management)
4. Service business logic (karma calculation, XP, reminders)
5. Skin module rendering pipeline

---

## Prioritized Action Plan

### Immediate (Bugs)

1. ~~Fix `_isInitialized` race condition in `Program.cs` — use
   `Interlocked.CompareExchange`~~ ✅
2. ~~Replace `List<IDiscordGameSession>` with `ConcurrentDictionary` in
   `GameService.cs`~~ ✅
3. ~~Fix double `i++` in `EmbedModule.cs` reaction-polling loop (and add
   `break` after confirmation)~~ ✅
4. ~~Fix sunrise/sunset copy-paste bug in `WeatherModule.cs`~~ ✅
5. ~~Add `using` to all `HttpClient` instances or switch to `IHttpClientFactory`;
   replace deprecated `WebClient` usage~~ ✅
6. ~~Add null checks in `RoleAttributes.cs` for DM context safety~~ ✅
7. ~~Wrap all `async void` event handlers in try-catch~~ ✅

### Short-term (Architecture)

1. ~~Split `UserService` into focused services~~ ✅
2. Split `BotSettings` into domain-specific config classes
3. Add `BotSettings.Validate()` post-deserialization
4. ~~Extract business logic from command handlers into services~~ ✅
5. ~~Register `IHttpClientFactory` in DI; remove manual `HttpClient` creation~~ ✅
6. ~~Add graceful shutdown support with `CancellationToken`~~ ✅
7. ~~Move static module state (`_activeDuels`) to services~~ ✅

### Medium-term (Quality)

1. Create `EmbedFactory` to reduce embed construction duplication
2. Create `SafeFireAndForget()` extension to replace `#pragma` + `Task.Run`
3. ~~Consolidate `ContainsInviteLink()` overloads~~ ✅ removed (dead code)
4. Add configuration validation for all settings
5. Audit service lifetimes — consider `Scoped` for interaction-scoped services
6. Remove all dead/commented-out code
7. Standardize naming conventions (event handlers, async methods, service
    constants)

### Long-term (Sustainability)

1. Set up test project with xUnit and write tests for critical paths
2. Split `ICasinoRepo` into focused interfaces
3. Extract `IWebClient` / `IHtmlParser` from `WebUtil` for testability
4. Implement session expiry and cleanup for casino game sessions
5. Refactor skin module hierarchy — intermediate base classes, coordinate config
6. Consolidate duplicate `RectangleD` struct
7. Replace `string[][]` database in `UpdateService` with typed structures

---

## Findings Summary

| Category | Critical | High | Medium | Low | Total |
|----------|----------|------|--------|-----|-------|
| Thread safety | 3 | 3 | — | — | 6 |
| Resource leaks | — | 5 | — | — | 5 |
| Null-reference risks | — | 2 | 5 | — | 7 |
| Logic bugs | — | 1 | 3 | 1 | 5 |
| God classes / SRP | — | 4 | 3 | — | 7 |
| Code duplication | — | 2 | 6 | — | 8 |
| Long methods | — | 4 | 6 | — | 10 |
| Hardcoded values | — | — | 7 | 6 | 13 |
| Missing error handling | — | 2 | 5 | 2 | 9 |
| Architecture / design | — | 3 | 4 | — | 7 |
| Dead code | — | — | 4 | 5 | 9 |
| Naming inconsistencies | — | — | 3 | 4 | 7 |
| Security | — | — | 3 | 1 | 4 |
| Test coverage | 1 | — | — | — | 1 |
| Skin system | — | — | 4 | 3 | 7 |
| Async void handlers | — | 1 | — | — | 1 |
| **Total** | **4** | **27** | **53** | **22** | **106** |
