# #409 Casino: atomic token updates

## Goal

Two token changes for the same player at the same moment never overwrite each other, and a player cannot spend tokens they do not have by firing slots, gifts and daily claims at once.

## Acceptance criteria

- AC1: WHEN N token changes (credits and debits) for one player run concurrently, THE SYSTEM SHALL end with the starting balance plus the sum of all changes (clamped at 0 only when a debit would go below it). Check: integration test against PostgreSQL.
- AC2: WHEN two gifts or slots bets each need the player's whole balance at the same moment, THE SYSTEM SHALL accept exactly one and refuse the other as insufficient tokens. Check: integration test (gift and debit) and `SlotsServiceTests` (slots bet).
- AC3: WHEN two `/daily` claims for one player run concurrently, THE SYSTEM SHALL pay the reward once and report the other as not yet available. Check: integration test.
- AC4: WHEN a gift is made, THE SYSTEM SHALL move the tokens from sender to receiver in one database statement, so no state exists where the sender is debited and the receiver is not. Check: integration test (insufficient sender leaves both balances unchanged).
- AC5: IF a slots spin fails after its bet was reserved, THEN THE SYSTEM SHALL give the bet back without a transaction row. Check: `SlotsServiceTests`.
- AC6: Existing behavior stays: one transaction row per spin with the net amount, one row per gift side, table-game payouts clamp at 0, admin set/add unchanged. Check: existing tests plus the new ones.

## Design

- No schema change, so no migration and no new upgrade path. Only the statements in `ICasinoRepo` change (`Extensions/CasinoRepository.cs`).
- New repository statements, each a single `UPDATE ... RETURNING`, so PostgreSQL's row lock serializes concurrent changes:
  - `AdjustTokens(userId, delta, updatedAt)`: `tokens = GREATEST(tokens + @delta, 0)`, returns the new balance. Replaces the read-then-write in `CasinoService.UpdateUserTokens`; keeps the clamp that table-game payouts rely on.
  - `TryDebitTokens(userId, amount, updatedAt)`: `tokens = tokens - @amount ... WHERE tokens >= @amount`, returns the new balance or no row when insufficient.
  - `TransferTokens(from, to, amount, updatedAt)`: one statement with a data-modifying CTE that debits the sender (conditional) and credits the receiver only if the debit happened; returns the receiver's balance or no row.
  - `ClaimDailyReward(userId, reward, now, eligibleBefore)`: adds the reward and sets `lastdailyreward = now` `WHERE lastdailyreward <= @eligibleBefore`, returns the new balance or no row.
- `CasinoService`: `UpdateUserTokens`, `TransferTokens` and `TryClaimDailyReward` call these instead of reading the user first (the user is still created on first contact by `GetOrCreateCasinoUser`). `UpdateTokensAndDailyReward` and the `UpdateTokens` read-modify-write path are removed; `UpdateTokens` stays only for the admin "set" command, which is an absolute write by intent.
- Slots (`ISlotsLedger`, `SlotsService`, `CasinoSlotsLedger`): a paid spin reserves its bet up front with `TryDebitTokens` (refused as `InsufficientTokens` when it fails), then settles by crediting the payout and recording one `Game` transaction with the net amount. A failure between reservation and settlement refunds the bet. The per-user in-flight lock stays: it still protects the hold-offer state and the double-click case cheaply. Free respins reserve nothing.
- Table games keep their net settlement at the end of a game (`GameService.EndGame`) through the atomic `AdjustTokens`, clamped at 0. Their bets are still not reserved while a game runs, so overdrawing across games remains possible: that is #347, not part of this plan.
- Integration tests use `Testcontainers.PostgreSql` (test-only package in `DiscordBot.Tests`, approved with this plan; newest version the release-age gate allows) to start a throwaway PostgreSQL 16 in Docker, create the casino tables with `DatabaseService`'s statements and run the real repository. They need Docker, as the local database already does; CI's Ubuntu runners have it.
- Transaction rows stay separate inserts after the balance change (as today). A failed insert leaves the balance right and the history short; it is already logged.

## Edge cases and failure modes

- A gift to a player without a casino row: the receiver is created before the statement (as today), so the CTE's credit always finds a row.
- A clamped debit (payout larger than the balance) records the full delta in the history, as today.
- A gift now also refuses a zero or negative amount and a gift to oneself (the old code let a self-gift mint tokens); `/gift` already blocked both.
- Two gifts in opposite directions lock both rows in user ID order inside the statement, so they cannot deadlock.
- A receiver deleted by an admin reset in the middle of a gift would make the sender lose the amount; accepted as too rare to guard.
- Deploying changes how every casino path writes balances: take a production backup first (`docs/deployment.md`). Nothing persisted changes shape, so a rollback is redeploying the previous image.

## Checklist

Status: #409.1 done and reviewed; next is #409.2.

- [x] #409.1 Atomic repository statements and `CasinoService` paths (`UpdateUserTokens`, `TransferTokens`, `TryClaimDailyReward`), with the Testcontainers fixture and integration tests for AC1–AC4 (each written first and seen failing against the old read-then-write code where practical). Verify: `dotnet build`, `dotnet test`.
  - Visible after: nothing: balances behave the same for a single action; only simultaneous actions stop losing tokens.
  - Needs you: nothing
- [ ] #409.2 Slots bet reservation, refund on failure, `SlotsServiceTests` for AC2 (slots part) and AC5, existing slots tests still pass. Verify: `dotnet test`.
  - Visible after: nothing: a slots bet beyond the balance is refused the same way, now also under simultaneous commands.
  - Needs you: nothing
- [ ] #409.3 Docs: add the decision to `docs/decisions.md` (balances change only through single relative statements, never read-then-write) and update `docs/casino.md` if it describes token handling; delete this plan in the same commit, naming its path.
  - Visible after: nothing: documentation only.
  - Needs you: take a production backup before pushing to `master` (at the end).
