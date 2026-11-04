# Code Quality Audit

**Scope:** Full codebase review: refactoring opportunities, code duplication, patterns, bugs. Fixed findings are removed from this file.

## Open questions

- **Q1 Re-check the remaining findings.** Findings that name `UserService`, `UserModule` or the moderation code point to classes that no longer exist; that code now lives in `XpService`, `KarmaService`, `ProfileCardService`, `WelcomeService` and the `Profiles` modules. Findings marked *(verified)* were checked against the current code; the others are not.
- **Q2 Track the open work in GitHub Issues.** The project's backlog is GitHub Issues; the open actions below become issues once confirmed.

---

## Findings by Category

### 1. God Classes & SRP Violations

| Class | File | Responsibilities | Severity |
|-------|------|-----------------|----------|
| `UpdateService` | `Services/UpdateService.cs` | Bot data, user muting lifecycle, FAQ loading, RSS feeds, Wikipedia downloading (5 concerns) | High |
| `CasinoSlashModule` | `Modules/Fun/Casino/CasinoSlashModule.cs` | 500+ lines; token commands, game commands, admin commands, statistics, nested `TokenCommands` class | High |
| `ICasinoRepo` | `Extensions/CasinoRepository.cs` | 37+ SQL method signatures in one interface *(verified)* | Medium |

**Recommended split:**

- `ICasinoRepo` → `ICasinoUserRepo`, `ITokenTransactionRepo`,
  `ICasinoAdminRepo`

---

### 2. Code Duplication

#### 2b. Embed construction boilerplate

Files: `ModerationService.cs`, `RecruitService.cs`, `CannedResponseService.cs`,
and ~20 other locations.

```csharp
var builder = new EmbedBuilder()
    .WithColor(color)
    .WithTimestamp(...)
    .FooterInChannel(...);
```

**Fix:** Create an `EmbedFactory` helper with preset methods per use-case. *(verified: `new EmbedBuilder()` appears 74 times)*

#### 2c. Service name constant

Every service file declares `private const string ServiceName = "...";`
identically. Could be extracted to a base class or generated via `nameof`.

#### 2f. Cooldown calculation pattern

File: `UserServiceExtensions.cs` — `Days()`, `Hours()`, `Minutes()`,
`Seconds()`, `Milliseconds()` all follow the exact same structure with
`cooldowns.HasUser()` check.

#### 2g. Weather command overloads

File: `WeatherModule.cs` — each weather command (Temp, Weather, Pollution, Time)
duplicated as `(IUser)` and `(params string[])` overloads with near-identical
bodies.

---

### 3. Potential Bugs

#### 3a. Thread-safety issues (Critical)

| Location | Issue |
|----------|-------|
| `UserService.cs` — `_xpCooldown` dictionary | Modified from multiple async tasks without synchronization. |
| `ReminderService.cs` — `_reminders` list | Modified while iterating in `CheckReminders`. |
| `GameSession.cs` — `GameData` dictionary | `AddPlayer` / `RemovePlayer` without locks. |
| `RecruitService.cs` — `_botSanityCheck` dictionary | Used as a lock mechanism but is not thread-safe. |

#### 3b. Resource leaks (High)

| Location | Issue |
|----------|-------|
| `DatabaseService.cs` — `Query` property | Returns a new connection on each access; may never be disposed by caller. |
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

#### 3d. Logic bugs

| Location | Issue | Severity |
|----------|-------|----------|
| `StringExtensions.cs` — `MessageSplitToSize` | With no newline in a page, `LastIndexOf` + 1 is 0 and `page.Remove(-1)` throws. It also ignores its `maxLength` parameter. *(verified)* | Medium |
| `AirportModule.cs` — `FlyTo` day-of-week calc | `(int)DayOfWeek - 2` counts days since Tuesday, not until: Monday gives 6 instead of 1. *(verified)* | Medium |
| `Blackjack` — `DoubleDown` method | No check that player has sufficient tokens before doubling bet; `CasinoService.UpdateUserTokens` clamps the result to 0, so a player can double beyond their balance and lose nothing extra. *(verified)* | Medium |

#### 3e. Session / memory leaks (Medium)

| Location | Issue |
|----------|-------|
| `GameSession.cs` | `ExpiryTime` commented out — sessions can live forever with no cleanup. *(verified)* |
| `Program.cs` | All services are `Singleton` — never disposed; database connections held forever. |

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

#### 6c. Inconsistent command patterns

- Mixed text commands (`UserModule`) vs slash commands (`UserSlashModule`) for
  similar functionality.
- Inconsistent use of `Priority` attribute across modules.
- Inconsistent alias patterns.
- Different `InteractionModuleBase` generic parameterization.
- Event handler naming varies: `MessageReceived` vs `OnMessageReceived` vs
  `GatewayOnMessageReceived`.

#### 6e. Singleton-only DI

`Program.cs` registers every service as `Singleton`. No consideration for
`Scoped` or `Transient` lifetimes. Services holding database connections or
disposable resources are never cleaned up.

---

### 7. Skin System Issues

| Issue | Severity | Details |
|-------|----------|---------|
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
| `EmbedModule.cs` — `EmbedCommand` | `attachment.Url` is passed to `BuildEmbedFromUrl` without `IsValidHost()`. The command is `[RequireAdmin]` and the URL is a Discord CDN attachment, so the risk is low; pastebin/hastebin redirects remain a concern for the URL overload. *(verified)* | Low |
| `CasinoRepository.cs` — 37+ SQL methods | SQL injection surface is large. All queries are likely parameterized via Insight.Database, but this should be verified explicitly. | Low (verify) |

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

## Open actions

- Create an `EmbedFactory` to reduce embed construction duplication (2b).
- Decide on service lifetimes: every service is a singleton today (6e); either keep that as the rule or move interaction-scoped services to `Scoped`.
- Remove dead and commented-out code (8). *(verified: still present in `GameSession`, `BlackjackDiscordGameSession`, `AirportModule`, `AirportService`, `MikuService`, `UpdateService`, `UnityHelpService`, `StringExtensions`)*
- Standardize naming: event handlers, async methods, service name constants (11).
- Split `ICasinoRepo` into focused interfaces (1).
- Expire and clean up idle casino game sessions (3e).
- Refactor the skin module hierarchy: `BaseTextSkinModule` exists for text modules; coordinates are still hardcoded (7).
- Fix the verified bugs in 3d: `MessageSplitToSize`, `FlyTo` day-of-week, Blackjack double down.
