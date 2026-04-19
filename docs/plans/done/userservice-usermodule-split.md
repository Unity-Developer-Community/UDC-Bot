---
post_title: "UserService & UserModule Split Plan"
author1: "Copilot"
post_slug: "userservice-usermodule-split"
microsoft_alias: ""
featured_image: ""
categories: []
tags: ["refactor", "architecture"]
ai_note: "AI-generated plan"
summary: "Detailed plan for extracting focused services from UserService and focused modules from UserModule"
post_date: "2026-04-06"
---

## Overview

Split `UserService` (god service) and `UserModule` (god module) into focused,
single-responsibility classes. Each service extraction is paired with its
corresponding module extraction where applicable.

## Services to Extract from UserService

### S1. XpService

- **State**: `_xpCooldown`, `_xpMin/MaxPerMessage`, `_xpMin/MaxCooldown`,
  `_noXpChannels`, `_rand`
- **Events**: `MessageReceived → UpdateXp`
- **Methods**: `UpdateXp()`, `LevelUp()`, `GetXpLow()`, `GetXpHigh()`
- **Dependencies**: `DatabaseService`, `ILoggingService`, `BotSettings`,
  `UserSettings`
- **Note**: `GetXpLow/High` already duplicated in `ProfileCardService` — will
  remain duplicated for now (different ownership)

### S2. KarmaService

- **State**: `_thanksCooldown`, `_canEditThanks`, `_thanksRegex`,
  `_thanksCooldownTime`, `_thanksMinJoinTime`
- **Events**: `MessageReceived → Thanks`,
  `MessageUpdated → ThanksEdited`
- **Methods**: `Thanks()`, `ThanksEdited()`
- **Dependencies**: `DatabaseService`, `ILoggingService`, `BotSettings`,
  `UserSettings`

### S3. CodeCheckService

- **State**: `CodeReminderCooldown`, `_codeBlockWarnPatterns`, `_x3CodeBlock`,
  `_x2CodeBlock`, `_codeReminderCooldownTime`, `_maxCodeBlockLengthWarning`,
  `CodeFormattingExample`, `CodeReminderFormattingExample`
- **Events**: `MessageReceived → CodeCheck`
- **Methods**: `CodeCheck()`
- **Dependencies**: `ILoggingService`, `BotSettings`, `UserSettings`,
  `UpdateService`
- **Public API**: `CodeFormattingExample` (used by `CodeTipModule`),
  `CodeReminderCooldown` (used by `CodeTipModule`)
- **Persistence**: `UpdateLoop()`, `SaveData()`, `LoadData()` move here —
  only `CodeReminderCooldown` is persisted

### S4. EveryoneScoldService

- **State**: `_everyoneScoldCooldown`
- **Events**: `MessageReceived → ScoldForAtEveryoneUsage`
- **Methods**: `ScoldForAtEveryoneUsage()`
- **Dependencies**: `BotSettings`
- **Tiny service** — could be inlined into a message filter, but extracting
  keeps UserService clean

### S5. MikuService

- **State**: `_mikuMentioned`, `_mikuCooldownTime`, `_mikuRegex`, `_mikuReply`
- **Events**: `MessageReceived → MikuCheck` (currently commented out)
- **Methods**: `MikuCheck()`
- **Dependencies**: None (standalone easter egg)
- **Note**: Currently disabled. Will extract as-is with the event subscription
  commented out

### What Stays in UserService (→ renamed to WelcomeService)

- Welcome block (`UserJoined`, `DelayedWelcomeService`, `ProcessWelcomeUser`,
  `WelcomeMessage`, `DMFormattedWelcome`, `GetWelcomeEmbed`,
  `CheckForWelcomeMessage`, `UserIsTyping`)

`UserLeft` and `UserUpdated` move to `AuditLogService` (step 1, pre-split).

After extraction, `UserService` is renamed to **WelcomeService** (step 17).

## Modules to Extract from UserModule

All new modules use `[Group("UserModule"), Alias("")]` to stay visible in
`!help`.

### M1. ProfileModule — ALREADY DONE

- Commands: `!profile` (2 overloads)
- Dependencies: `ProfileCardService`, `ILoggingService`

### M2. QuoteModule

- Commands: `!quote` (3 overloads)
- Dependencies: None beyond `Context`
- Self-contained, no service dependency

### M3. RulesModule

- Commands: `!rules` (2 overloads), `!globalrules`, `!welcome`, `!channels`,
  `!faq`
- Dependencies: `Rules`, `UserService` (DMFormattedWelcome), `UpdateService`
  (GetFaqData)
- FAQ is server info/guidance, fits with rules thematically
- Includes helper methods: `SearchFaqs`, `ListFaqs`, `GetFaqEmbed`,
  `FormatFaq`, `CalculateScore`, `ParseNumber`

### M4. RankModule

- Commands: `!top`, `!topkarma`, `!topkarmaweekly`, `!topkarmamonthly`,
  `!topkarmayearly`
- Dependencies: `DatabaseService`, `ILoggingService`
- Includes helper: `GenerateRankEmbedFromList()`

### M5. ProfileModule update

- Add `!karma` and `!joindate` to existing `ProfileModule`
- Dependencies already satisfied (`DatabaseService` to add)

### M6. CodeTipModule

- Commands: `!codetip`, `!disablecodetips`
- Dependencies: `CodeCheckService` (new, replaces `UserService` for
  `CodeFormattingExample` and `CodeReminderCooldown`)

### M7. FunModule

- Commands: `!slap`, `!coinflip`, `!roll` (2 overloads), `!d20`
- Dependencies: `BotSettings` (slap tables)
- State: `_random`, `_slapObjects`, `_slapFails`

### M8. SearchModule

- Commands: `!search` (2 overloads), `!manual`, `!doc`, `!wiki`
- Dependencies: `BotSettings` (API URLs), `ILoggingService`
- Uses `HtmlAgilityPack`, `UnityAPI`
- Includes helpers: various HTML scraping methods

### M9. BirthdayModule

- Commands: `!birthday` (2 overloads)
- Dependencies: `UserExtendedService`, `DatabaseService`, `ILoggingService`
- Includes helper: `GenerateBirthdayCard()`

### M10. ConvertModule

- Commands: `!ftoc`, `!ctof`, `!translate` (2 overloads), `!currency`
  (2 overloads), `!currencyname`
- Dependencies: `CurrencyService`
- Groups temperature, translation, and currency conversion — all
  "convert/translate" commands

### M11. WeatherModule update

- Move `!setcity` and `!removecity` from UserModule into existing
  `WeatherModule` (they're only used by weather)
- Dependencies: `WeatherService`, `UserExtendedService` (already in
  WeatherModule)

### M12. ServerModule + ServerService

- **ServerService**: `GetGatewayPing()` extracted from UserService
- Commands: `!ping`, `!members`, `!help`
- Dependencies: `ServerService`, `CommandHandlingService`
- `!help` moves here as the final extraction from UserModule

### What Stays in UserModule

Nothing — **UserModule is deleted** once all commands are extracted (step 16).

## Execution Order

Paired service+module commits where applicable.
Each commit includes DI registration in `Program.cs`.

| Step | Service | Module | Commit message |
|------|---------|--------|----------------|
| 0 | AuditLogService update | — | `refactor(services): move UserLeft and UserUpdated to AuditLogService` |
| 1 | XpService | — | `refactor(services): extract XpService from UserService` |
| 2 | KarmaService | — | `refactor(services): extract KarmaService from UserService` |
| 3 | CodeCheckService | CodeTipModule | `refactor: extract CodeCheckService and CodeTipModule` |
| 4 | EveryoneScoldService | — | `refactor(services): extract EveryoneScoldService from UserService` |
| 5 | MikuService | — | `refactor(services): extract MikuService from UserService` |
| 6 | — | QuoteModule | `refactor(modules): extract QuoteModule from UserModule` |
| 7 | — | RulesModule (+FAQ) | `refactor(modules): extract RulesModule from UserModule` |
| 8 | — | RankModule | `refactor(modules): extract RankModule from UserModule` |
| 9 | — | ProfileModule update | `refactor(modules): move karma and joindate to ProfileModule` |
| 10 | — | FunModule | `refactor(modules): extract FunModule from UserModule` |
| 11 | — | SearchModule | `refactor(modules): extract SearchModule from UserModule` |
| 12 | — | BirthdayModule | `refactor(modules): extract BirthdayModule from UserModule` |
| 13 | — | ConvertModule | `refactor(modules): extract ConvertModule from UserModule` |
| 14 | — | WeatherModule update | `refactor(modules): move city commands to WeatherModule` |
| 15 | ServerService | ServerModule (+!help) | `refactor: extract ServerService and ServerModule` |
| 16 | — | delete UserModule | `refactor: delete UserModule after full extraction` |
| 17 | rename UserService→WelcomeService | — | `refactor: rename UserService to WelcomeService` |

## Notes

- Every new module gets `[Group("UserModule"), Alias("")]` for `!help`
  compatibility
- Every new service registered as singleton in `Program.cs`
- Build verified after every step
- Peer review at the end (or after each major batch)
- Audit doc S1 checkmark added on the final cleanup commit
