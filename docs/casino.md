# Casino Module Architecture

## Overview

The Casino module is a comprehensive gaming system for the Discord bot that enables users to play various casino-style games (Blackjack, Poker, Rock Paper Scissors) within Discord channels. The architecture follows a layered approach with clear separation of concerns between user interface, business logic, game logic, and data persistence.

## Architecture Components

### 1. User Interface Layer

#### CasinoSlashModule (`Modules/Casino/CasinoSlashModule.cs`)

- **Primary Interface**: Handles all Discord slash commands and component interactions

- **Key Features**:
  - Token management commands (`/casino tokens balance`, `/casino tokens gift`, `/casino tokens daily`)
  - Token leaderboards (`/casino tokens leaderboard`)

- **Responsibilities**:
  - Input validation and permission checking
  - Discord interaction handling (slash commands, buttons, modals)
  - Response formatting and error handling
  - Delegating business logic to services

#### CasinoSlashModule.Games (`Modules/Casino/CasinoSlashModule.Games.cs`)

- **Game Interface**: Handles game-specific interactions and component callbacks

- **Key Features**:
  - Game creation and management (`/casino game`, `/casino rules`)
  - Game Statistics and leaderboards (`/casino statistics`, `/casino leaderboard`)
  - Game session management (join, leave, ready, play again)
  - Player actions (hit, stand, bet, etc.)
  - AI player management
  - Real-time game state updates

### 2. Service Layer

#### CasinoService (`Services/Casino/CasinoService.cs`)

- **User Management**: Core service for token and user data management

- **Key Features**:
  - User creation and token balance management
  - Transaction recording and audit trails
  - Daily reward system
  - Game statistics and leaderboard generation
  - Admin token management

- **Responsibilities**:
  - Database interactions for user data
  - Token transfer and validation
  - Statistical data aggregation

#### GameService (`Services/Casino/GameService.cs`)

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
- **BlackjackDiscordGameSession**: Discord UI for blackjack games

##### Poker (`Domain/Casino/Games/Cards/Poker/`)

- **PokerPlayerAction**: Call, Raise, Fold, Check actions
- **PokerPlayerData**: Hole cards, betting state
- **Poker**: Texas Hold'em implementation
- **PokerDiscordGameSession**: Discord UI with private hand support

##### Rock Paper Scissors (`Domain/Casino/Games/RockPaperScissors/`)

- **RPSPlayerAction**: Rock, Paper, Scissors choices
- **RockPaperScissors**: Simple simultaneous choice game
- **RockPaperScissorsDiscordGameSession**: Discord UI for RPS

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

   *(Example: `Domain/Casino/Games/Blackjack.cs`)*
   - Define player action enum with `ButtonMetadata` attributes
   - Create player data class implementing `ICasinoGamePlayerData`
   - Implement game class extending `ACasinoGame<TPlayerData, TPlayerAction>`

2. **Create Discord Integration**:

   *(Example: `Domain/Casino/Discord/BlackjackDiscordGameSession.cs`)*
   - Implement Discord session class extending `DiscordGameSession<TGame>`
   - Override embed and component generation methods

3. **Register Game**:
   - Add to `CasinoGame` enum in `CasinoSlashModule.cs`
   - Update factory methods in `GameService`

### Configuration

- Channel restrictions via `CasinoService.IsChannelAllowed()`
- Starting token amounts in `BotSettings.CasinoStartingTokens`
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
