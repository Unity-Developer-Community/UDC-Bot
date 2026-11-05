# Casino Module Architecture

## Overview

The Casino module is a comprehensive gaming system for the Discord bot that enables users to play various casino-style games (Blackjack, Poker, Rock Paper Scissors) within Discord channels. The architecture follows a layered approach with clear separation of concerns between user interface, business logic, game logic, and data persistence.

## Architecture Components

### 1. User Interface Layer

#### CasinoSlashModule (`Modules/Fun/Casino/CasinoSlashModule.cs`)

- **Primary Interface**: Handles all Discord slash commands and component interactions

- **Key Features**:
  - Token management commands (`/casino tokens balance`, `/casino tokens gift`, `/casino tokens daily`)
  - Token leaderboards (`/casino tokens leaderboard`)

- **Responsibilities**:
  - Input validation and permission checking
  - Discord interaction handling (slash commands, buttons, modals)
  - Response formatting and error handling
  - Delegating business logic to services

#### CasinoSlashModule.Games (`Modules/Fun/Casino/CasinoSlashModule.Games.cs`)

- **Game Interface**: Handles game-specific interactions and component callbacks

- **Key Features**:
  - Game creation and management (`/casino game`, `/casino rules`)
  - Game Statistics and leaderboards (`/casino statistics`, `/casino leaderboard`)
  - Game session management (join, leave, ready, play again)
  - Player actions (hit, stand, bet, etc.)
  - AI player management
  - Real-time game state updates

### 2. Service Layer

#### CasinoService (`Services/Fun/Casino/CasinoService.cs`)

- **User Management**: Core service for token and user data management

- **Key Features**:
  - User creation and token balance management: every balance change is one atomic SQL statement (see "Balance changes" below)
  - Transaction recording and audit trails
  - Daily reward system
  - Game statistics and leaderboard generation
  - Admin token management

- **Responsibilities**:
  - Database interactions for user data
  - Token transfer and validation
  - Statistical data aggregation

#### GameService (`Services/Fun/Casino/GameService.cs`)

- **Session Management**: Manages active game sessions and player interactions

- **Key Features**:
  - Game session lifecycle (create, join, end, remove)
  - Game type factory pattern for different games
  - Player action validation and processing
  - Payout calculation and distribution

- **Responsibilities**:
  - Creating game instances and Discord wrappers
  - Managing active session collection
  - Coordinating between game logic and user service

### 3. Domain Layer

#### Game Abstraction

##### ICasinoGame Interface (`Domain/Casino/Game.cs`)

- **Contract**: Defines the interface all casino games must implement

- **Key Methods**:
  - `StartGame()`, `EndGame()`, `DoPlayerAction()`
  - `HasNextDealerAction()`, `DoNextDealerAction()`
  - `HasNextAIAction()`, `DoNextAIAction()`
  - `GetPlayerGameResult()`, `CalculatePayout()`

##### ACasinoGame&lt;TPlayerData, TPlayerAction&gt; (`Domain/Casino/Game.cs`)

- **Base Class**: Abstract implementation providing common game functionality

- **Generic Parameters**:
  - `TPlayerData`: Game-specific player state (cards, actions, etc.)
  - `TPlayerAction`: Enum defining possible player actions

- **Features**:
  - State management (NotStarted, InProgress, Finished, Abandoned)
  - Player data management with strongly-typed game data
  - Template method pattern for game flow

#### Session Management

##### IGameSession Interface (`Domain/Casino/GameSession.cs`)

- **Session Contract**: Defines game session management interface

- **Key Methods**:
  - Player management (add, remove, ready state)
  - Action processing and state queries
  - AI and dealer action handling

##### GameSession&lt;TGame&gt; (`Domain/Casino/GameSession.cs`)

- **Session Implementation**: Generic game session with strongly-typed game reference

- **Features**:
  - Player collection management
  - Game state validation and auto-start logic
  - Maximum seats and player count enforcement

##### IDiscordGameSession Interface (`Domain/Casino/DiscordGameSession.cs`)

- **Discord Integration**: Extends game sessions with Discord-specific functionality

- **Key Methods**:
  - `GenerateEmbedAndButtons()`: Creates Discord UI components
  - `GenerateRules()`: Provides game rule explanations
  - `ShowHand()`: Returns private hand information

##### DiscordGameSession&lt;TGame&gt; (`Domain/Casino/DiscordGameSession.cs`)

- **Discord Wrapper**: Abstract base for Discord-integrated game sessions

- **Features**:
  - Discord context management (guild, client, user)
  - Embed and component generation
  - Player name resolution and formatting
  - Results display and payout information

#### Specific Game Implementations

##### Blackjack (`Domain/Casino/Games/Cards/Blackjack/`)

- **BlackjackPlayerAction**: Hit, Stand, DoubleDown actions
- **BlackjackPlayerData**: Player cards and action history
- **Blackjack**: Core game logic with dealer AI and payout calculation
- **BlackjackDiscordGameSession** (`Domain/Casino/Discord/`): Discord UI for blackjack games

##### Poker (`Domain/Casino/Games/Cards/Poker/`)

- **PokerPlayerAction**: Call, Raise, Fold, Check actions
- **PokerPlayerData**: Hole cards, betting state
- **Poker**: Texas Hold'em implementation
- **PokerDiscordGameSession** (`Domain/Casino/Discord/`): Discord UI with private hand support

##### Rock Paper Scissors (`Domain/Casino/Games/RockPaperScissors/`)

- **RPSPlayerAction**: Rock, Paper, Scissors choices
- **RockPaperScissors**: Simple simultaneous choice game
- **RockPaperScissorsDiscordGameSession** (`Domain/Casino/Discord/`): Discord UI for RPS

### 4. Data Layer

#### Domain Models

- **CasinoUser**: User profile with token balance and statistics
- **GamePlayer**: Base player representation
- **DiscordGamePlayer**: Discord-specific player with user ID and AI support
- **Card, Deck**: Card game utilities

#### Database Integration

- **DatabaseService**: Handles all database operations
- **Transaction logging**: Audit trail for all token movements
- **Statistics tracking**: Game results and player performance data

## Slots

A solo three-reel slot machine with one payline and a jackpot shared by the whole server. Players run `/casino slots spin bet:<1-100>` and get a private reply with an animated GIF of the reels stopping, then buttons to spin again. `/casino slots info` shows the paytable, the rules and the current jackpot.

### Why it is not a table game

Slots has no join, ready or seats, so it does not go through `GameSession` / `CasinoGame` / `GameService`. It has its own engine, service and commands. Its settled spins are `Game` transactions with `game = Slots`, so they appear in `/casino statistics`, the token history and the global leaderboard. The leaderboard's per-game filter lists only the `CasinoGame` enum, so Slots is not in it.

### Pieces

| Piece | Path | Holds |
| --- | --- | --- |
| Engine | `Domain/Casino/Slots/` | Pure rules, no Discord: symbols, the three reel strips, the paytable, `Spin` / `Respin` over an `ISlotRandom` (cryptographically secure in production, seeded in tests), the settlement math (`SlotSettlementMath`) and the exact-return analysis (`SlotReturnAnalysis`) |
| Service | `Services/Fun/Casino/SlotsService.cs` | Validates the bet, guards against a second spin of the same player, spins, settles the player and the jackpot, renders the GIF, keeps the pending hold offers in memory. Balances and the jackpot go through `ISlotsLedger` (`CasinoSlotsLedger` in production) so the service is tested without a database |
| Renderer | `Services/Fun/Casino/SlotsRenderer.cs` | The GIF (Magick.NET) and the static picture, one render at a time. Resolved and warmed in the `Ready` handler so no player waits for the first render |
| Messages | `Services/Fun/Casino/SlotsMessageBuilder.cs` | The private result, the public announcement, the buttons and the info page |
| Commands | `Modules/Fun/Casino/CasinoSlashModule.Slots.cs` | `/casino slots spin` and `info`, and the `slots_spin`, `slots_hold` and `slots_respin` buttons |
| Art | `Assets/slots/` | Fluent Emoji 3D PNGs (MIT, notice beside them); the red 7 is drawn from `Assets/fonts/ConsolasBold.ttf`. The `Dockerfile` copies this folder explicitly |

### Rules

- **Paytable:** three of a kind (🍋 3×, 🍇 5×, 🔔 10×, ⭐ 20×, 🍒 25×, 7️⃣ 100×, 💎 the jackpot) or exactly two of a symbol anywhere on the line (🍇, 🔔, ⭐ and 🍒 2×, 7️⃣ 3×; lemon and diamond pairs pay nothing, so two diamonds stay a near miss that can offer a hold). The design favours frequent small wins so a player's tokens last: no line only gives the bet back, a rarer three of a kind always pays more, and in a simulation of players starting with 20 tokens at 1 per spin, half last more than about 100 spins.
- **Return to players:** the paytable, the strips and the 5% jackpot contribution are tuned together and guarded by `SlotMachineTests`, which enumerates every reel combination (no simulation): the return is between 92% and 96% (about 94.4%), wins land on 1 spin in 3 to 4 (about 1 in 3.65), and three diamonds on about 1 spin in 3,000-10,000 (about 1 in 8,400, holds included). The figure assumes the jackpot at its long-run size; as with any shared progressive jackpot, a player who only plays when the pot is far above it has an edge.
- **Jackpot:** 5% of every paid bet, small bets included, is added to the pot (kept in hundredths of a token so a 1-token bet adds exactly 0.05). Three diamonds pay `bet / 100` of the pot, rounded down to whole tokens, so a 100-token bet takes it all and the rest stays in the pot. The pot is topped up to 1,000 tokens when a win leaves it lower.
- **Hold and respin:** about 1 losing spin in 4 offers a hold. The player toggles a hold per reel, then takes one free respin of the other reels at the original bet. A respin feeds nothing, never offers another hold, and an offer can be used once (`SlotsService` removes it when spent, because the engine itself would accept a second respin). It expires after 10 minutes, at the player's next spin, or at a bot restart. Button ids carry the offer id, so a button of an older message is refused.
- **Public posts:** a jackpot and any win on a line that lands less than once in 200 first spins (`SlotMachine.PublicWinOneIn`; currently three bells and better) are also posted in the channel with the GIF, naming the player without pinging them. Each paytable line's chance is computed exactly from the reel strips, so retuning the reels keeps the rule right. Expect about one post per 190 spins.
- **Failures:** the spin is settled before the GIF is rendered, so a rendering failure only costs the animation: the reply is the text result. The bet is reserved first and the jackpot is claimed before the player is credited; when crediting fails after a claim, the amount and the player are logged as an error so an admin can credit it with `/admin casino tokens-add`. Any other failed crediting of a win is logged the same way, with the payout and bet. A spin or respin is refused and logged, before anything changes and without spending a hold offer, when the `casino_jackpot` row is missing or below its floor; a jackpot claim that returns nothing is treated as the same fault.

### Balance changes

A balance is never read and written back as a total: two changes for the same player at the same moment would overwrite each other. `ICasinoRepo` changes it with single `UPDATE ... RETURNING` statements, which PostgreSQL runs one after the other for the same row:

| Statement | Used by | Behavior |
| --- | --- | --- |
| `AdjustTokens` | `CasinoService.UpdateUserTokens`: table-game payouts, slots payouts, admin add | Adds a signed amount, stops at 0 |
| `TryDebitTokens` | `CasinoService.TryReserveTokens`: the slots bet | Subtracts only when the balance covers it |
| `TransferTokens` | `/casino tokens gift` | Debits the sender and credits the receiver in one statement, locking both rows in user ID order so opposite gifts cannot deadlock |
| `ClaimDailyReward` | `/casino tokens daily` | Pays only when the last claim is old enough, so a double claim pays once |

The slots bet is taken with `TryDebitTokens` before the spin. The payout is credited at settlement and one `Game` transaction records the net result. A spin that fails before settlement gives the bet back without a history row; a failed settlement of a win keeps the bet taken and logs the payout owed. The admin `set` command is the one absolute write, by intent. `CasinoServiceTests` runs these against a real PostgreSQL started with Testcontainers.

### Jackpot storage

The `casino_jackpot` table (`Id integer PRIMARY KEY, Amount bigint, UpdatedAt timestamptz`) holds one row, `Id = 1`. `DatabaseService` creates it and the row with `JackpotProps.EnsureStatements` on every start, independently of the casino tables, which are only created when `casino_users` is missing. `ICasinoRepo` has three methods: `GetJackpot`, `AddJackpotContribution` and `ClaimJackpotShare`, the last one a single atomic statement that locks the row, takes the share and applies the floor.

### Known limits

- Pending bets in table games are not counted against the balance used for the bet check (#347).
- Table-game bets are not reserved while a game runs: a player can join games with tokens they then spend elsewhere, and a loss that exceeds the balance at settlement stops at 0 (#347).
- A restart between the reservation and the settlement of a slots spin loses that bet, with no history row and no log. The window is a few database round trips.

## Game Flow Architecture

### 1. Game Creation Flow

```text
User Command → CasinoSlashModule → GameService → Game Factory → GameSession Creation → Discord Response
```

### 2. Player Action Flow

```text
Discord Interaction → CasinoSlashModule → GameService → GameSession → Game Logic → State Update → Discord Update
```

### 3. Game Completion Flow

```text
Game End Condition → GameService.EndGame() → Payout Calculation → CasinoService.UpdateUserTokens() → Statistics Update
```

## Key Design Patterns

### 1. Factory Pattern

- `GameService.GetGameInstance()` creates appropriate game types
- `GameService.CreateDiscordGameSession()` creates Discord wrappers

### 2. Template Method Pattern

- `ACasinoGame<T,U>` provides common game structure
- Specific games override abstract methods for custom logic

### 3. Strategy Pattern

- Different games implement `ICasinoGame` interface
- Allows runtime game type selection

### 4. Observer Pattern

- Game state changes trigger Discord UI updates
- Automatic AI and dealer action processing

## Extensibility

### Adding a New Game

1. **Create Game Logic**:

   *(Example: `Domain/Casino/Games/Cards/Blackjack/Blackjack.cs`)*
   - Define player action enum with `ButtonMetadata` attributes
   - Create player data class implementing `ICasinoGamePlayerData`
   - Implement game class extending `ACasinoGame<TPlayerData, TPlayerAction>`

2. **Create Discord Integration**:

   *(Example: `Domain/Casino/Discord/BlackjackDiscordGameSession.cs`)*
   - Implement Discord session class extending `DiscordGameSession<TGame>`
   - Override embed and component generation methods

3. **Register Game**:
   - Add to `CasinoGame` enum in `CasinoSlashModule.Games.cs`
   - Update factory methods in `GameService`

### Configuration

- Channel restrictions via `CasinoService.IsChannelAllowed()`
- Starting token amounts in `BotSettings.Casino.StartingTokens`
- Daily reward amounts and cooldowns
- Game-specific parameters (max players, betting limits)

## Error Handling and Logging

### Error Handling Strategy

- **User Errors**: Graceful handling with ephemeral Discord responses
- **System Errors**: Comprehensive logging with stack traces
- **Transaction Errors**: Rollback mechanisms and audit trails

### Logging Integration

- All significant actions logged via `ILoggingService`
- Separate logging for user actions vs system errors
- Transaction audit trail for compliance

## Security Considerations

### Token Security

- Server-side validation of all token operations
- Audit trail for all transactions
- Prevention of negative balances and invalid transfers

### Game Integrity

- Server-side game state management
- Action validation against current game state
- Prevention of duplicate actions and invalid moves
