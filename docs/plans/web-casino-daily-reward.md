# #416 Web casino: claim the daily reward

Status: #416.1 and #416.2 done; finished, waiting for the user's review.

## Goal

A member claims the daily token reward from the web casino's header, with the same amount and cooldown as `/casino tokens daily`. When the reward is not available yet, the header shows how long until the next claim. Claiming in Discord and on the web at the same moment pays once, and a web claim shows in the token history like a Discord one.

## Acceptance criteria

- AC1 WHEN a member whose last claim is older than `Casino.DailyRewardIntervalSeconds` calls the hub's `ClaimDailyReward`, THE SYSTEM SHALL add `Casino.DailyRewardTokens` to the balance, record one `DailyReward` transaction, and return `ok` with the tokens awarded, the new balance and the seconds until the next claim (the full interval). Check: `CasinoWeb.Tests` hub test over `FakeCasinoRepo`.
- AC2 WHEN the last claim is more recent than the interval (made on the web or in Discord), THE SYSTEM SHALL change nothing, record nothing, and return `notYet` with the seconds left, rounded up. Check: hub test with `LastDailyReward` set in the fake repo.
- AC3 WHEN two claims for the same member run at the same moment, from Discord and the web or twice from the web, THE SYSTEM SHALL pay once. Check: the existing `CasinoServiceTests.ConcurrentDailyClaims_PayOnce` against PostgreSQL covers it, because the hub calls the same `CasinoService.TryClaimDailyReward`.
- AC4 WHEN the casino is closed, or the member sent more than 5 game actions in the last second, THE SYSTEM SHALL refuse the claim like any game action (`casino-closed`, `slow-down`). Check: hub tests.
- AC5 WHEN a member loads the page while the casino is open, `/api/me` SHALL include the reward amount and the seconds until the next claim (0 when available); while closed it SHALL include no daily information. Check: `CasinoWeb.Tests` test of `/api/me`.
- AC6 WHEN the reward is available, THE header SHALL show an enabled button `Claim 100 tokens` (the configured amount); WHEN it is not, a disabled button `Next daily in 5h 12m` that counts down (`12m`, then `45s` under a minute) and becomes the claim button at 0, without a reload. Check: Vitest header test with fake timers.
- AC7 WHEN a claim succeeds, THE header SHALL add the awarded tokens to the shown balance, switch the button to the countdown, and show a toast `Daily reward: +100 tokens` that leaves after about 4 s; an open Blackjack table SHALL raise its bet limit by the same amount. Check: Vitest header and `blackjackGame` tests.
- AC8 WHEN the claim answers `notYet`, THE header SHALL switch to the countdown and toast `Already claimed. Next daily in 5h 12m`; WHEN it is refused or fails (slow down, closed, connection lost, server error), it SHALL keep the button enabled and toast a short reason; the balance is unchanged in both cases. Check: Vitest header tests.
- AC9 The Discord `/casino tokens daily` command is unchanged. Check: `dotnet test`; no file under `DiscordBot/` changes.

## Design

- **Server reuses the bot's claim.** `CasinoHub.ClaimDailyReward()` (`[GameAction]`, so the filter applies the rate limit, the membership recheck and the casino switch) calls `CasinoService.TryClaimDailyReward(userId)` as is: its single `ClaimDailyReward` statement is what makes simultaneous claims pay once (`docs/decisions.md` D4), and it records the `DailyReward` transaction and logs the claim. No bot change.
- **Wire shapes** (`HubModels.cs`, mirrored in `protocol.ts`): `DailyInfo(long Tokens, long SecondsUntilNext)`; `DailyClaimDto(string Status, long Awarded, long Balance, DailyInfo Daily)` with status `ok` or `notYet`. `MeResponse` gains `DailyInfo? Daily`, null while the casino is closed, from `CasinoService.GetNextDailyRewardTime`. `HubState` is unchanged: the header is drawn from `/api/me` before the hub connects.
- **Seconds, not a timestamp.** The server sends the seconds left (`max(0, ceil(next − now))`); the client turns them into a deadline on its own clock when the answer arrives, so a wrong clock on the player's machine cannot show a wrong countdown.
- **Balance shown after a claim:** the header adds the awarded tokens to the balance it shows rather than taking the returned balance. The returned balance already includes the result of a Blackjack hand or a spin whose animation may still be running, and the games hold the balance back until their animation ends so it does not give the result away. The next game result brings the shown balance back in line with the server anyway.
- **Header** (`views/header.ts`): `renderHeader(me, onLogout, onClaim?)` gets the daily button between the balance and Log out when `me.daily` is set and a claim callback is given; the countdown ticks every second from an injectable clock. The button text carries the state (not color alone), with the existing `.button` / `.button.secondary` styles and theme variables, so the dark theme follows. The toast is a small `role="status"` element in the header that removes itself; there is no toast component yet, and this one stays local to the header until a second user needs it.
- **App** (`app.ts`): passes `api.claimDailyReward()` (new `CasinoApi` method) as the claim callback, and after a successful claim tells the open screen through an optional `addTokens(n)` on the screen object. Only `BlackjackGame` implements it (its bet slider is capped by the balance it last saw); slots has no client-side cap. `addTokens` raises the game's own balance without reporting it back to the header.
- **No claim button without a connection** (`deps.connection` absent, as in tests of other screens) and none while closed.
- **Not in this plan:** a reminder when the reward becomes available; the rest of `/casino tokens` (gift, history), which is #417.

## Checklist

### #416.1 Claim through the hub

- [x] `DailyInfo`, `DailyClaimDto` in `CasinoWeb/Hubs/HubModels.cs`; `CasinoHub.ClaimDailyReward` as in Design; `MeResponse.Daily` in `Endpoints/AccountEndpoints.cs`.
- [x] `FakeCasinoRepo.ClaimDailyReward` behaves like the SQL statement (pays and stamps `LastDailyReward = now` only when the last claim is at or before `eligibleBefore`, otherwise null).
- [x] Hub tests for AC1, AC2, AC4; `/api/me` test for AC5 (open and closed).
- [x] `CasinoWeb/README.md`: add `ClaimDailyReward` to the hub methods list.
- [x] `dotnet build` with no new warnings, `dotnet test` green.

Visible after: nothing: the server can pay the reward, but no button calls it yet.
Needs you: nothing.

### #416.2 Daily button in the header

- [x] `protocol.ts` (`DailyInfo`, `DailyClaim`), `api.ts` (`Me.daily`), `CasinoApi.claimDailyReward`.
- [x] `views/header.ts`: button, countdown, toast (AC6–AC8); styles in `style.css` with theme variables only.
- [x] `app.ts`: claim callback and `addTokens` on the open screen; `BlackjackGame.addTokens` and the Blackjack screen exposing it.
- [x] Vitest tests: header (both states, countdown reaching 0 with fake timers, success, `notYet`, refusal), app (button only when open and connected), `blackjackGame.addTokens`.
- [x] Docs: `docs/casino.md` (Balance changes table: `ClaimDailyReward` also used by the web casino's header; Web Casino intro mentions the daily reward), `docs/features.md` (Web Casino row).
- [x] In `CasinoWeb/ClientApp`: `npm run lint`, `npm test`, `npm run build` with no new problems; `dotnet test` green.

Visible after: the web casino's header shows `Claim 100 tokens`; clicking it adds 100 tokens and turns it into a countdown to the next claim.
Needs you: run the casino locally (`CasinoWeb/README.md`), log in, check the button's look in light and dark mode, claim once and see the balance rise and the countdown appear (at the end). The agent cannot do this check: the page needs a real Discord login.

## Finishing

No decision passes the `docs/decisions.md` test (the reuse of the bot's claim is visible in the code and covered by D4). Delete this plan.
