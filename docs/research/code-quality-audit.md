# Code Quality Audit

**Scope:** Full codebase review: refactoring opportunities, code duplication, patterns, bugs. Every finding below was checked against the current code and is marked *(verified)*. Fixed findings are removed from this file.

## Open questions

- **Q1 Which open actions become GitHub issues?** The project's backlog is GitHub Issues (`Unity-Developer-Community/UDC-Bot`). Recommendation: file F10, F11, F12 and F13 (confirmed bugs) first, then F7 and F8; keep the rest here until someone picks them up.

---

## Findings

### Structure

| ID | Where | Finding | Severity |
|----|-------|---------|----------|
| F1 | `Services/UpdateService.cs` | 307 lines, five concerns: persisted bot/user/feed JSON, FAQ loading, RSS feed checks, Unity doc database download, Wikipedia download. *(verified)* | Medium |
| F2 | `Modules/Fun/Casino/CasinoSlashModule.cs` and `CasinoSlashModule.Games.cs` | One `partial` class of about 1,165 lines (512 + 653): token commands (nested `TokenCommands` class), game commands, statistics and leaderboards. *(verified)* | High |

---

### Code duplication

- **F3 Embed construction boilerplate.** `new EmbedBuilder()` appears 71 times in 22 files (most in `AdminSlashModule` 12, `RecruitService` 8, `CasinoSlashModule` 6). Color, timestamp and footer setup is repeated per use-case; only `EmbedBuilderExtension` shares part of it (`FooterInChannel`, used in `AuditLogService`). Fix: an `EmbedFactory` helper with preset methods. *(verified)*
- **F4 `ServiceName` constant.** 15 services declare `private const string ServiceName = "..."` by hand, and two values are wrong: `RecruitService` uses `"RecruitmentService"` and `WeatherService` uses `"FeedService"` (`Services/Utils/Weather/WeatherService.cs:9`), so its log lines name the wrong service. Fix: derive from `nameof` or a base class. *(verified)*
- **F5 Cooldown helpers.** `Extensions/UserServiceExtensions.cs`: `Days()`, `Hours()`, `Minutes()`, `Seconds()`, `Milliseconds()` repeat the same `HasUser` check and `Subtract(DateTime.Now)` shape. *(verified)*
- **F6 Weather overloads.** `Modules/Utils/Weather/WeatherModule.cs`: Temperature, CurentWeather, Pollution and Time each exist as an `(IUser)` and a `(params string[])` overload. The embed building is already shared in `*Embed` helpers; what repeats is the 10-line command body. Low value. *(verified)*

---

### Potential bugs

- **F7 Unsynchronized shared state (High).** *(verified)*
  - `Services/Server/ReminderService.cs`: `_reminders` (a `List`) is changed by command handlers (`AddReminder`, `RemoveReminders`) while the `CheckReminders` background loop reads and removes from it. The loop iterates a copy, so the risk is concurrent access, not modification during enumeration.
  - `Domain/Casino/GameSession.cs`: `Players` is changed by `AddPlayer` / `RemovePlayer` / `RemovePlayerAI` from concurrent button interactions without a lock.
- **F8 Undisposed images (High).** `Services/Profiles/ProfileCardService.cs`, `GenerateProfileCard()`: `background`, `profile.Picture`, each layer `image`, each layer canvas `l` (all `MagickImage`) and the avatar `Stream` are never disposed; only `profileCard` and `result` are. *(verified)*
- **F9 Connection per access (Low).** `Services/DatabaseService.cs`: `Query`, `CasinoQuery` and `BadgeQuery` create a new `NpgsqlConnection` on every property access and nothing disposes it. Whether this leaks depends on Insight.Database opening and closing the connection per call, which was not confirmed here. *(verified in code, impact unconfirmed)*
- **F10 `MessageSplitToSize` (Medium).** `Extensions/StringExtensions.cs:37`. *(verified by reading)*
  - It ignores its `maxLength` parameter and uses `Constants.MaxLengthChannelMessage`.
  - When a page has no newline, `LastIndexOf` + 1 is 0 and `page.Remove(-1)` throws.
  - From the third page on, `indexOfLine` is an offset inside the previous page but is used as an offset into the whole string, so text repeats.
  - Callers: `RulesModule`, `CommandHandlingService`.
- **F11 `FlyTo` day offset (Medium).** `Modules/Utils/AirportModule.cs:79`: `(int)DayOfWeek - 2` counts days since Tuesday, not until: Monday gives 6 instead of 1. The result is passed as `daysFromNow` to `AirportService.GetFlightInfo`. *(verified)*
- **F12 Blackjack double down (Medium).** `Domain/Casino/Games/Cards/Blackjack/Blackjack.cs:159`: `DoubleDown` doubles `player.Bet` without checking the player's balance, and `CasinoService.UpdateUserTokens` clamps the balance at 0 (`Services/Fun/Casino/CasinoService.cs:89`), so a player can double beyond their tokens and lose nothing extra. The balance is only checked when the bet is set (`GameService.SetBet`). *(verified)*
- **F13 Game sessions never expire (Medium).** `Domain/Casino/GameSession.cs:42`: `ExpiryTime` is commented out. `GameService._activeSessions` entries are removed only for abandoned games and rules pages (`CasinoSlashModule.Games.cs:104`, `:202`), so finished or idle sessions stay in memory. *(verified)*

---

### F14 Long methods (> 50 lines)

All rows verified.

| File | Method | ~Lines | Issue |
|------|--------|--------|-------|
| `Services/Profiles/ProfileCardService.cs` | `GenerateProfileCard()` | 120 | DB queries, image manipulation, HTTP, file I/O in one method |
| `Services/Profiles/KarmaService.cs` | `Thanks()` | 65 | Regex matching, DB calls, cooldown checks combined |
| `Modules/Utils/AirportModule.cs` | `FlyTo` | 85 | API calls, coordinate lookups, embed building |
| `Modules/Server/EmbedModule.cs` | `SendEmbedToChannel` | 70 | Reaction polling, message creation, confirmation |
| `Modules/Fun/Casino/CasinoSlashModule.cs` | `DisplayTransactionHistory` | 145 | Query, pagination, admin checks, embed formatting |
| `Domain/Casino/Games/Cards/Poker/PokerHelper.cs` | `EvaluateHand` | 100 | Hand ranking with edge cases |

---

### F15 Hardcoded values that could be configuration

All rows verified.

| File | Value | Purpose |
|------|-------|---------|
| `Services/Fun/MikuService.cs:21` | `39` minutes | Miku cooldown |
| `Services/Code/CodeCheckService.cs:19` | `800` | Code block warning length |
| `Services/Code/Unity/UnityHelp/UnityHelpService.cs` | `10` min, `14` h, `20` h, `3` days, `5` min (named constants) | Thread close, idle and delete timers |
| `Services/Server/RecruitService.cs` | `120` chars, `60` s (named constants) | Minimum message length, delete delay |
| `Services/Server/ReminderService.cs` | `10` (named constant) | Max reminders per user |
| `Services/Utils/AirportService.cs:19`, `:200` | `https://test.api.amadeus.com/...` | Test endpoints hardcoded, no production switch |
| `Extensions/StringExtensions.cs:16` | `1990` | `MessageSplit` default, while `Constants.MaxLengthChannelMessage` is 2000 |
| `Extensions/UserServiceExtensions.cs:82` | `9999` days | "Permanent" cooldown |
| `Modules/Utils/Weather/WeatherModule.cs:331` | `22.5, 67.5, 112.5...` | Wind direction angles |
| `Modules/Fun/Casino/CasinoSlashModule.Games.cs:573` | Game name mapping | Hardcoded switch expression |
| `Skin/KarmaPointsSkinModule.cs`, `KarmaRankSkinModule.cs`, `LevelSkinModule.cs` | `StartX` / `StartY` | Default pixel positions in constructors; `Assets/skins/skin.json` can override them |

---

### F16 Inconsistent command patterns

All rows verified.

- Text and slash modules exist side by side for the same commands: `ConvertModule` / `ConvertSlashModule` (currency) and `ServerModule` / `ServerSlashModule` (help, ping, members).
- `Priority` is used in 8 of the text modules and absent from the others.
- `InteractionModuleBase` is used non-generic in 6 modules (`AdminSlashModule`, `BirthdaySlashModule`, `DuelSlashModule`, `ServerSlashModule`, `CannedInteractiveModule`, `UnityHelpInteractiveModule`) and as `InteractionModuleBase<SocketInteractionContext>` in 11.

---

### Skin system

| ID | Where | Finding | Severity |
|----|-------|---------|----------|
| F17 | `Skin/SkinModuleJsonConverter.cs`, `Services/Profiles/ProfileCardService.cs` | `Type.GetType()` runs for every module on every deserialization, and `GetSkinData()` re-reads and re-deserializes `skin.json` for each profile card (no cache). An unknown module `Type` makes `Type.GetType` return null; the failure is logged and rethrown, but with an unclear message. *(verified)* | Medium |
| F18 | `Skin/RectangleSampleAvatarColorSkinModule.cs:40` | Unexplained `650` RGB-sum threshold. *(verified)* | Medium |
| F19 | `Skin/` | Coordinates are `double` everywhere except `RectangleSampleAvatarColorSkinModule` (`int`). *(verified)* | Low |
| F20 | `Skin/CustomTextSkinModule.cs`, text skin modules | `BaseTextSkinModule` holds the shared text setup, but `CustomTextSkinModule.GetDrawables` copies the base method body instead of calling it, and several constructors repeat the same `StrokeColor` / `FillColor` / `FontPointSize` defaults. *(verified)* | Medium |

---

### F21 Dead code and commented-out code

All rows verified.

| File | What |
|------|------|
| `Services/Fun/MikuService.cs:29` | `MikuCheck` event subscription commented out, so the service never runs |
| `Services/UpdateService.cs:79` | `UpdateUserRanks` task commented out |
| `Modules/Utils/AirportModule.cs:105-117` | Flight details (seats, bags, fees) commented out |
| `Services/Utils/AirportService.cs:45`, `:86-87` | Commented-out model properties |
| `Domain/Casino/GameSession.cs:42-43` | `ExpiryTime`, `UserId` properties commented out |
| `Domain/Casino/DiscordGameSession.cs:171`, `:216` | "Custom" bet button and "Reload Embed" button commented out |
| `Domain/Casino/Discord/BlackjackDiscordGameSession.cs:52` | Dealer blackjack text commented out |
| `Services/Code/Unity/UnityHelp/UnityHelpService.cs:176`, `:191`, `:265` | Commented-out statements |
| `Extensions/StringExtensions.cs:98` | Commented-out `return` |
| `Services/Code/Tips/TipService.cs:206` | TODO about attaching images |

---

### Missing error handling

| ID | File | Method | Issue |
|----|------|--------|-------|
| F22 | `Services/Utils/AirportService.cs` | `GetFlightInfo()` | Returns null on a failed HTTP response without logging. *(verified)* |
| F23 | `Services/DatabaseService.cs` | Constructor background task | Bare `catch` treats any failure (network, permissions, a failed `ALTER TABLE`) as "table does not exist" and goes on to `CREATE TABLE`, without logging the original exception. Same pattern for the casino tables. *(verified)* |
| F24 | `Services/Code/Tips/TipService.cs` | `CommitTipDatabase()` | No try-catch around the file write. *(verified)* |
| F25 | `Program.cs` | `Ready` handler | No try-catch around service resolution; `_isInitialized` is already set, so a failure leaves the bot partly initialized and never retries. *(verified)* |

---

### Security

- **F26 `EmbedCommand` attachment URL (Low).** `Modules/Server/EmbedModule.cs:25`: `attachment.Url` is passed to `BuildEmbedFromUrl` without `IsValidHost()`. The command is `[RequireAdmin]` and the URL is a Discord CDN attachment, so the risk is low. *(verified)* The URL overload checks the host before download, but `EmbedParsingService.BuildEmbedFromUrl` uses a default `HttpClient`, which follows redirects; whether the allow-listed hosts redirect elsewhere was not checked.

---

### Naming inconsistencies

- **F27 Event handler names.** Handlers are named by action (`Thanks`, `UpdateXp`, `CodeCheck`), `GatewayOn*` (`RecruitService`, `UnityHelpService`) or `On*` (`UnityHelpService`, the inner method behind each `GatewayOn*` wrapper). `UnityHelpService` and `RecruitService` also subscribe without `EventGuard.Guarded`. *(verified)*
- **F28 Get-or-create methods.** `DatabaseService.GetOrAddUser` and `CasinoService.GetOrCreateCasinoUser` do the same kind of thing under two verbs. Low. *(verified)*
- **F29 Data model casing.** `Data/UnityAPI.cs`: `Kategory` next to `Category`, `Keyimage`, `Pubdate`. Properties map to the API's JSON, so a rename needs `[JsonProperty]`. Low. *(verified)*

---

## Open actions

All confirmed against the current code.

- Fix the logic bugs: `MessageSplitToSize` (F10), `FlyTo` day offset (F11), Blackjack double down (F12).
- Expire and clean up idle and finished casino game sessions (F13).
- Dispose the images and stream in `GenerateProfileCard` (F8).
- Guard `_reminders` and `GameSession.Players` against concurrent access (F7).
- Correct the `ServiceName` values (`WeatherService`, `RecruitService`) and derive the constant instead of retyping it (F4).
- Log the original exception in the `DatabaseService` bare catch blocks (F23).
- Create an `EmbedFactory` to reduce embed construction duplication (F3).
- Remove dead and commented-out code (F21).
- Standardize event handler names (F27).
- Cache the parsed skin and the module type lookup (F17); reuse `BaseTextSkinModule` in `CustomTextSkinModule` (F20).
- Split `CasinoSlashModule` (F2) and `UpdateService` (F1).
