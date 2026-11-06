# #412 Web casino: multiplayer tables, starting with poker

Status: #412.1 to #412.5 committed; finished, waiting for the user's review.

## Goal

Members play Texas Hold'em against each other at shared tables in the web casino, live, with the same token balance as the Discord casino. The table system built for it (seats, turns, timers, disconnects, hidden cards, bets taken as they are made) is what later multiplayer games reuse.

## Acceptance criteria

- AC1 WHEN a member opens Poker in the lobby, THE SYSTEM SHALL list the open tables (stakes, seats taken out of 6, hand running or waiting) and update the list live as tables open, fill and close. Check: hub test with two clients; manual.
- AC2 WHEN a member creates a table with one of the stakes presets, THE SYSTEM SHALL open it with them seated; WHEN its last player leaves, it closes. A member sits at one table at a time; a full table, a second seat, a balance under 10 big blinds and a 21st open table are refused. Check: unit tests.
- AC3 WHEN at least 2 seated players can cover 10 big blinds and no hand runs, THE SYSTEM SHALL start a hand 5 s after the previous one ended (so its result can be read), move the dealer button one seat and post the blinds (heads-up: the button posts the small blind). Check: engine and table unit tests with a fake clock.
- AC4 WHEN the player to act sends fold, check, call, bet, raise or all-in, THE SYSTEM SHALL apply no-limit Hold'em rules (a bet at least the big blind, a raise at least the previous raise, an all-in for less does not reopen the betting) and refuse anything else with the allowed options. A player commits at most 100 big blinds, or their balance when the hand starts if lower, in one hand. Check: engine unit tests.
- AC5 WHEN a blind, bet, call or raise is made, THE SYSTEM SHALL take it from the balance at once; WHEN the balance no longer covers it (spent in Discord meanwhile), it SHALL refuse the action, lower the player's stake to what is left and offer the new options. Check: unit tests over `FakeCasinoRepo`.
- AC6 WHEN a hand reaches showdown, THE SYSTEM SHALL rank each remaining player's best five of seven cards, award the main pot and every side pot, split ties evenly and give an odd token to the first winner left of the button; WHEN everyone else folds, the last player wins without showing cards. Check: engine unit tests.
- AC7 WHEN a hand ends, THE SYSTEM SHALL credit each human player their winnings and record one `Game` transaction with `game=Texas Hold'em` of their net result, for every player who put tokens in, so `/casino statistics` and the token history show it as its own game, apart from Discord's five-card draw `Poker`. Check: unit tests.
- AC8 WHILE a hand runs, THE SYSTEM SHALL send each player only their own hole cards; other players' cards are sent only at showdown and only for hands still in; folded hands and the deck never leave the server. Check: hub test with two seated clients and a watcher, asserting the received payloads.
- AC9 WHEN it is a player's turn, THE SYSTEM SHALL show everyone whose turn it is and the seconds left; WHEN 30 s pass, it checks for them if they can, otherwise folds; after 2 missed turns in a row the player is stood up at the end of the hand. Check: unit tests with a fake clock.
- AC10 WHEN a seated player's last connection drops, THE SYSTEM SHALL keep their seat for 60 s (their turns still time out); a reconnect within that time shows the table with their own cards; otherwise they fold and leave. Check: unit tests; hub test closing and reopening a client.
- AC11 WHEN the server stops gracefully, THE SYSTEM SHALL give back every token committed to an unfinished hand and close the tables; every debit, settlement and give-back is logged with user, table, hand and amount. Check: unit test with a simulated stop.
- AC12 WHEN `Casino__AllowAiSeats` is true (the dev environment and local Development only), a seated player can add and remove AI seats that check or call and never touch a balance; WHEN it is false, the hub refuses them. Check: unit and hub tests; manual on dev.
- AC13 Table actions go through the existing hub filter (5 actions/s, membership recheck, casino switch); WHEN the casino is closed, no new hand starts and only leaving is allowed. Check: hub tests.
- AC14 Slots, solo blackjack, the bot and their tests are unchanged: `dotnet test`, client lint/test/build pass. Check: commands.

## Design

- **Rules engine:** `CasinoWeb/Games/Poker/`, pure, no tokens or time: `HoldemHand` takes the seats with their stake caps, the button, the blinds and a shuffled deck; exposes the street, board, current seat, allowed actions (with min and max raise), pots, and `Act` / `Forfeit` (a leave folds out of turn) / `CapStake` (AC5). It lives in `CasinoWeb`, not `DiscordBot/Domain`, because the Discord poker is five-card draw and these tables are web-only; it reuses `Card` and `PokerHelper.EvaluateHand` / `CompareHands` for the best of 21 five-card combinations (fix `PokerHelper` in the bot only if the tests show a ranking bug). The deck is shuffled with `RandomNumberGenerator` (players compete against each other, so the order must not be predictable), behind a seam the tests use to deal fixed cards.
- **Table system:** `CasinoWeb/Tables/`, generic over a thin `ITableGame` (start a hand, act, time out, forfeit, view for a user, results); poker is its only implementation (`PokerTableGame`), no speculative hooks.
  - `TableService` (singleton): registry, lobby summaries, one seat per member, at most 20 open tables, AI seats behind `CasinoHostOptions.AllowAiSeats`.
  - `Table`: seats (6), watchers, presence, turn deadline, consecutive timeouts; every change runs through one `SemaphoreSlim` gate per table, as `WebBlackjackService.Seat` does; pushes leave in order.
  - `TableLedger`: `TryReserveTokens` per blind or bet, `UpdateUserTokens(winnings, Game, {game: Texas Hold'em}, recordedAmount: net)` at the end (its own game name, never `Poker`: Discord's five-card draw may be removed, and its statistics must not mix with these), `ReleaseTokens` of the committed amount on stop; each logged as in `WebBlackjackService` (prefix `WebPoker`). No history row for a debit, as for blackjack.
  - `TablesSweeper` (hosted service, 1 s tick, `TimeProvider`): turn deadlines, grace expiry, next-hand start, AI moves; its `StopAsync` runs the release (AC11), as `BlackjackSweeper` does.
  - Constants in code: stakes presets 1/2, 5/10, 25/50; 6 seats; 10 big blinds to sit; 100 big blinds per hand; 30 s turn; 60 s grace; 2 missed turns; 5 s between hands; 20 tables.
- **Presence:** a `PresenceTracker` counts connections per user from `CasinoHub.OnConnectedAsync` / `OnDisconnectedAsync`; zero marks the seat away, a new connection marks it back. SignalR's reconnect makes a new connection, so presence is per user, not per connection.
- **Hub:** `TablesList()`, `TableCreate(stakes)`, `TableWatch(tableId)`, `TableUnwatch()`, `TableSit(tableId, seat)`, `TableLeave()`, `TableAddAi()`, `TableRemoveAi()`, `PokerAct(action, amount)`; all `[GameAction]` except `TableLeave` and the watch calls, which stay allowed when the casino closes. `GetState` gains the stakes presets, whether AI seats are allowed and the table the member sits at, so a reload goes back to it. Pushes through `IHubContext<CasinoHub>`: `TablesChanged(summaries)` to all connections, `TableUpdated(view)` to each watcher and seated user with that user's own view (`Clients.User`), so hidden cards are filtered per recipient on the server. Watching shows public information only.
- **AI seats:** fixed 100-big-blind stake each hand, no ledger, named `AI 1`…; they check when free, otherwise call. Tokens a human wins from them are created out of nothing, which is why the setting is false by default, true in `appsettings.Development.json` and in `k8s/dev/casino.yaml` only, and logged as a warning at start when on.
- **Client:** `src/games/poker/` with the existing split: `pokerGame.ts` controller (tested with a fake stage and api), `pokerScene.ts` Phaser table (seats around an oval, board, pot, face-down cards for others), `pokerPage.ts` DOM (action buttons, raise amount with min/max, seat list in text, turn countdown as a number and a bar, result lines). `src/views/tables.ts` lists tables and creates one. Card text and drawing move from `games/blackjack/` to a shared `src/games/cards/`, importers updated. Whose turn is shown by a "to act" label and the countdown, never by color alone.
- **Look:** plain and functional, like slots and blackjack (the prototype-first rule of #410); no Claude Design canvas for this item.
- **Failure modes:** a crash loses the tokens committed to the running hand, with a `WebPoker: took …` log line and no `settled` / `gave back` line to credit them from; a failed settlement is logged with the amount owed, as for blackjack. One replica (`replicas: 1`, `Recreate`) holds all tables, so no cross-process state is needed. Every casino deploy (each push to `master` touching `CasinoWeb/**` or `DiscordBot/**`) therefore closes every table and gives back the running hands: restarting without losing games in progress, tables included, is #418, not this plan.
- **Not in this plan:** other table games, multiplayer blackjack and Rock Paper Scissors (they reuse the table system later), Discord's five-card draw on the web (#417), a `Texas Hold'em` entry in the per-game leaderboard filter, which lists only the Discord games, as for Slots (#417 covers leaderboards), announcing big pots in Discord, chat (never), games and tables surviving a restart (#418), the 2D room (#413).

## Checklist

### #412.1 Hold'em engine

- [x] `CasinoWeb/Games/Poker/`: `HoldemHand`, best-of-seven ranking, pots and side pots, as in Design.
- [x] `CasinoWeb.Tests/Poker/`: blinds and button (heads-up included), min-raise and all-in for less, betting round ends, side pots with three all-ins, split pot with an odd token, best-of-seven kickers and the wheel, fold-out win, `CapStake`, `Forfeit` out of turn.

Decided during the run: the engine deals with no burn cards and does not add several short all-ins up into a full raise (they never reopen the betting), both documented on `HoldemHand`; the dealt order is what the tests rig. Why: burn cards add nothing when the deck never leaves the server, and the cumulative rule is rare and costs state. Alternative: burn one card before each street, or track the sum of short raises in `PutIn`. To change: `HoldemHand.NextStreet` / `PutIn`.
Decided during the run: the namespace is `CasinoWeb.Games.Poker` and the types are prefixed `Holdem`. A `DiscordBot.Domain.Poker` class exists, so code in `CasinoWeb.Games.*` that ever needs Discord's five-card draw (#417) must qualify it. Alternative: namespace `CasinoWeb.Games.Holdem`. To change: rename the namespace in `CasinoWeb/Games/Poker/` and `CasinoWeb.Tests/Poker/`.

Visible after: nothing: rules only, nothing calls them yet.
Needs you: nothing.

### #412.2 Tables, money and timers

- [x] `CasinoWeb/Tables/`: `ITableGame`, `PokerTableGame`, `Table`, `TableService`, `TableLedger`, `PresenceTracker`, `TablesSweeper`, AI seats; register in `HubSetup.AddCasinoHub`; `AllowAiSeats` in `CasinoHostOptions` and `appsettings.Development.json`.
- [x] Unit tests with `FakeTimeProvider` and `FakeCasinoRepo` for AC2, AC3, AC5, AC7, AC9, AC10, AC11, AC12.

Decided during the run: the table system pushes through an `ITableNotifier` (lobby list, and one view per seated user or watcher, `null` meaning "closed or no longer at it"), with a `NullTableNotifier` registered for now; #412.3 replaces it with the `IHubContext` one. Why: the hub item needs a seam to push through and the services must not know SignalR. Alternative: the services take `IHubContext<CasinoHub>` directly. To change: `ITableNotifier` in `CasinoWeb/Tables/ITableGame.cs` and its registration in `HubSetup.AddCasinoTables`.
Decided during the run: a table with no human seated closes, even with AI seats left (a hand still running is played down by the AI seats first, so the leaver's tokens settle); a player away (all connections dropped) is not dealt into new hands; a seat of the running hand stays reserved after its player left, until the hand ends; a member watches or sits at one table at a time; callers pass the display name when they sit (stored on the seat), table ids are sequential ints; an AI seat moves one action per 1 s tick. Why: smallest rules that keep hidden cards and money consistent. Alternative: AI moves at once in the same call, or ids as Guids. To change: `Table.cs` (`AfterChangeAsync`, `StartHandAsync`, `IsFree`, `TickAsync`).

Visible after: nothing: no hub method calls it yet.
Needs you: nothing.

### #412.3 Hub

- [x] Hub methods, `GetState` additions, pushes and DTOs (`HubModels.cs`, cards as `CardDto`), presence hooks in `CasinoHub`.
- [x] Hub tests with real SignalR clients for AC1, AC8, AC10, AC12, AC13.
- [x] `CasinoWeb/README.md`: hub methods, the `Casino__AllowAiSeats` setting, the `WebPoker` log lines.

Decided during the run: the poker DTOs are in `CasinoWeb/Hubs/TableHubModels.cs` (the push `HubTableNotifier.cs` beside it) instead of `HubModels.cs`, which already holds slots and blackjack; `GetState` also carries the seated table while the casino is closed, so a reload can still offer to leave it; wire words for statuses, refusals, streets and actions are the C# enum names with a lower-case first letter (`TableWire`); a seated member's display name is stored on the seat, capped at 32 characters. `TableService.CreateAsync` / `SitAsync` now end the member's watching (the plan says a member follows one table at a time), with a test. Why: smallest shape that keeps hidden cards and the one-table rule consistent. Alternative: everything in `HubModels.cs`, explicit wire switches per enum. To change: the files named.

Visible after: nothing in the browser yet; the tables exist on the server.
Needs you: nothing.

### #412.4 Poker in the browser

- [x] Lobby entry, tables list (create with a stakes preset, watch, sit), poker table page and scene, add/remove AI seat when allowed, reload resumes the seated table; `src/protocol.ts` and `src/casinoApi.ts` mirror the hub.
- [x] Vitest for the controller and the views; lint, `tsc`, build green; headless browser check of the tables list and a table with a stubbed hub.
- [x] `CasinoWeb/ClientApp/AGENTS.md` layout.

Decided during the run: the poker screen is one screen (`renderPokerScreen`, one lobby entry) that switches between the tables list and the table, driven by one controller; the table views carry no balance, so the header balance is refreshed with `GetState` when a hand reaches showdown and on joining or leaving (it shows the pre-hand balance while a hand runs). A reload resumes the seated table: `app.ts` opens the poker screen once connected when `GetState` says the member sits at one and they have not opened another game. The countdown (30 s bar) is drawn against a client constant `TURN_SECONDS` mirroring the server's timer. Why: smallest shape that meets the plan with no new server fields. Alternative: add the balance to `TableViewDto`, or an `Open` push; or ask the server for the turn length. To change: `PokerGame.refreshBalance`, `resumeTable` in `app.ts`, `TURN_SECONDS` in `pokerGame.ts`.

Visible after: poker playable locally at a table with AI seats.
Needs you: restart `dotnet run` in `CasinoWeb/`, create a table, add an AI seat, play a few hands including an all-in and a fold, leave the table idle for a missed turn (at the end).

### #412.5 Dev environment and docs

- [x] `k8s/dev/casino.yaml`: `Casino__AllowAiSeats=true`.
- [x] `docs/casino.md` (Web Casino: Tables and Poker; correct the Discord poker description, which is five-card draw, not Hold'em), `docs/features.md`.

Visible after: once deployed to dev, tables can be tried with several accounts.
Needs you: run Deploy to Dev, then play a hand with two Discord accounts in two browsers and check the other's cards stay hidden until showdown and `/casino statistics` shows the hands as Texas Hold'em (at the end).
