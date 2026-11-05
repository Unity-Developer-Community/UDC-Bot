# #411 Web casino: Discord login, slots and solo blackjack in the browser

## Goal

UDC members play the casino in a browser page, which responds immediately, with the same token balance as the Discord casino. The page is reached through a Discord login, and only members of the server get in. This plan delivers the first stage: login, slots and solo blackjack. Multiplayer tables and the walkable 2D room come later, in their own plans.

## Acceptance criteria

- AC1 WHEN someone who is not a member of the guild (`GuildId`) logs in, THE SYSTEM SHALL show an access-denied page and issue no casino cookie. Check: integration test with a faked Discord API; manual login with an account outside the server.
- AC2 WHEN a member logs in, THE SYSTEM SHALL show their Discord name, avatar and the same balance `/casino tokens balance` shows. Check: manual.
- AC3 WHEN a logged-in user leaves or is removed from the guild, THE SYSTEM SHALL refuse their next action after at most 10 minutes. Check: unit test of the membership revalidation with a fake clock.
- AC4 WHEN a request or hub connection has no valid cookie, or the WebSocket `Origin` is not the site's own, THE SYSTEM SHALL refuse it (401 / handshake rejected). Check: integration tests.
- AC5 WHEN a member spins the slots, THE SYSTEM SHALL apply exactly the Discord slots rules (bet limits, payouts, jackpot, hold offer, single-use respin) and record a `Game` transaction with `game=Slots`, so Discord statistics and leaderboards include web play. Check: hub tests over `SlotsService` with a fake ledger; manual spin, then `/casino statistics` in Discord.
- AC6 WHEN a member deals a blackjack hand, THE SYSTEM SHALL reserve the bet before the deal and refuse the deal when the balance is below the bet; a double down reserves a second bet and is refused when the balance cannot cover it. Check: unit tests.
- AC7 WHEN a blackjack hand ends, THE SYSTEM SHALL credit bet + payout (win 2× bet, tie the bet, loss 0) and record one `Game` transaction of the net amount with `game=Blackjack`. Check: unit tests.
- AC8 WHEN a hand gets no action for `Casino.GameTimeoutMinutes`, THE SYSTEM SHALL stand for the player and settle it; WHEN the server shuts down gracefully, it SHALL give back every reserved bet of an unfinished hand. Check: unit tests with a fake clock and a simulated stop.
- AC9 WHEN a user sends more than 5 game actions per second, THE SYSTEM SHALL refuse the extra ones with a "slow down" message. Check: unit test.
- AC10 WHEN `Casino.Enabled` is false, THE SYSTEM SHALL show "casino closed" after login and refuse game actions. Check: integration test.
- AC11 The bot's behavior and tests are unchanged: `dotnet test` passes, and a commit touching only `CasinoWeb/` does not rebuild or redeploy the bot. Check: `dotnet test`; reading the workflow path filters.

## Design

- **Location:** this repository. `CasinoWeb/` is an ASP.NET Core 8 project in `DiscordBot.sln` with a `ProjectReference` to `DiscordBot` (rules, services and repositories are reused as they are, no copy, no package to publish). `CasinoWeb/ClientApp/` is the TypeScript client. Argo CD already syncs `k8s/prod/`, so the casino deploys without a change to the cluster repository.
- **Separate process:** the casino never shares a process with the bot: a casino crash or deploy does not touch the bot, and the casino never holds the bot token. Balances are safe across both processes because every change is one relative SQL statement (`docs/decisions.md` D4). In-memory state (slots in-flight guard, hold offers, open blackjack hands) is per process, which is fine: an offer or hand belongs to the front end that made it.
- **Seams in the bot (the only bot changes):**
  - `ICasinoDatabase` (`ICasinoRepo? CasinoQuery { get; }`), implemented by `DatabaseService`; `CasinoService` and `CasinoSlotsLedger` depend on it instead of `DatabaseService`. The casino implements it from the connection string alone, so it never runs `DatabaseService`'s schema setup: the bot owns the schema.
  - `SlotsService` accepts a `null` renderer and then returns `Gif = null` without rendering (the browser animates; Magick GIF rendering per spin would waste CPU).
- **Settings:** the casino reads the bot's `Settings.json` (same file locally, same ConfigMap in prod) and uses only `GuildId`, `DbConnectionString` and `Casino`, so starting tokens and timeouts never drift. In prod the initContainer substitutes only `${DB_PASSWORD}`; the other placeholders stay unrendered and unread. Its own settings come from environment / user-secrets: `Discord__ClientId`, `Discord__ClientSecret`, `Casino__PublicOrigin`, `Casino__KeysPath`.
- **Auth:** OAuth2 code flow with `AspNet.Security.OAuth.Discord` (aspnet-contrib, approved runtime dependency) on the bot's Discord application, scopes `identify guilds.members.read`. At login, `GET /users/@me/guilds/{GuildId}/member` with the user's access token: 404 → access denied. The access token stays in the encrypted auth ticket; a revalidation runs on cookie validation when the last check is older than 10 minutes, and a non-member is signed out. Cookie `HttpOnly`, `Secure`, `SameSite=Lax`. Data Protection keys persist to `Casino__KeysPath` (a PVC in prod) so a deploy does not log everyone out. The bot token is never used.
- **Transport:** one SignalR hub `CasinoHub` (`[Authorize]`) from the start, since multiplayer tables need it next. Methods return results to the caller: `GetState`, `SlotsSpin(bet)`, `SlotsToggleHold(offerId, reel)`, `SlotsRespin(offerId)`, `BlackjackDeal(bet)`, `BlackjackAct(action)`. A hub filter applies the per-user rate limit (AC9) and maps service errors to typed results. The handshake checks `Origin` against `Casino__PublicOrigin`. The server makes every decision; the client sends intents and animates results.
- **Blackjack:** `WebBlackjackService` (singleton) keeps one `GameSession<Blackjack>` per user (one human + dealer, no AI). Deal: `TryReserveTokens(bet)` then start; double down: reserve the second bet first. End: `UpdateUserTokens(bet + payout, Game, {game: Blackjack}, recordedAmount: payout)`. A timer sweeps hands idle past `GameTimeoutMinutes` (stand + settle). `IHostApplicationLifetime.ApplicationStopping` releases every open reservation. Each reservation and settlement is logged with user, hand id and amount, so a crash (no graceful stop) leaves an audit trail for `/admin casino tokens-add`. The Discord table games do not reserve bets yet; that is #347, not this plan.
- **Logging:** `WebLoggingService : ILoggingService` writes to ASP.NET `ILogger` (console, collected by Loki). It does not post to the Discord log channel.
- **Client:** scaffolded with `create-corniflex` (Vite, strict TypeScript, Vitest, ESLint), plus `phaser` and `@microsoft/signalr` (approved runtime dependencies, newest versions the release-age gate allows). The header (avatar, name, balance, logout) is DOM; each game is a Phaser scene, so the 2D room becomes one more scene later. Slot symbols reuse `DiscordBot/Assets/slots/*.png` (Fluent Emoji, MIT, license file kept). Cards are drawn in code (no card art to license). The lobby, slots and blackjack screens follow the Claude Design canvas made before #411.3. Vite proxies `/api`, `/hub`, `/signin-discord` to Kestrel in dev; in prod Kestrel serves the built files from `wwwroot`.
- **Not in this plan:** daily reward, gifts, leaderboards (they stay in Discord); multiplayer tables (#412), the 2D room (#413), announcing web big wins in Discord (#414), a dev environment deployment (#415). All are sub-issues of #410.

## Checklist

### #411.1 Bot seams

- [x] Add `ICasinoDatabase`, implement it on `DatabaseService`, switch `CasinoService` and `CasinoSlotsLedger` to it; register it in `Program.ConfigureServices()`.
- [x] Make the `SlotsService` renderer nullable; a `null` renderer returns `Gif = null` without logging an error. Test it in `DiscordBot.Tests`.
- [x] `dotnet build` with no new warnings, `dotnet test` green.

Visible after: nothing: internal seams only, the bot behaves the same.
Needs you: nothing.

### #411.2 Casino server and Discord login

- [ ] `CasinoWeb/CasinoWeb.csproj` (`Microsoft.NET.Sdk.Web`, net8.0, same `LangVersion`, `Nullable`, `EnforceCodeStyleInBuild`), added to the solution, referencing `DiscordBot`. Settings loading and validation as in Design.
- [ ] Discord OAuth, membership check at login and revalidation, cookie settings, Data Protection key persistence, `GET /api/me` (id, name, avatar URL, balance, casino enabled), `POST /api/logout`, access-denied and casino-closed responses.
- [ ] `CasinoWeb.Tests` (xUnit, `WebApplicationFactory`, faked Discord HTTP handler) covering AC1, AC3, AC4 (HTTP part), AC10.
- [ ] `README.md` local setup: Discord redirect URL `http://localhost:<port>/signin-discord`, user-secrets, `dotnet run` from `CasinoWeb/`.

Visible after: nothing in the browser yet; `/api/me` answers after a Discord login.
Needs you: add the local redirect URL to the bot's Discord application and give the client secret through user-secrets (at the end); the Claude Design canvas for the lobby, slots and blackjack screens, before #411.3 starts (stops the run).

### #411.3 Client shell

- [ ] Scaffold `CasinoWeb/ClientApp` with `create-corniflex`, add `phaser` and `@microsoft/signalr`; Vite proxy; build output to `CasinoWeb/wwwroot` (gitignored).
- [ ] Login page, access-denied page, casino-closed page, lobby with header and two entries (Slots, Blackjack); SignalR connection with reconnect and a visible "reconnecting" state.
- [ ] Lint, `tsc`, Vitest green; headless browser check of the pages with a stubbed `/api/me`.

Visible after: you can log in locally and see your avatar, name and real balance.
Needs you: log in locally once with your account (at the end).

### #411.4 Slots

- [ ] `CasinoHub` with the Origin check, rate-limit filter (AC9) and the three slots methods over `SlotsService` (renderer `null`); hub tests for AC4, AC5, AC9.
- [ ] Phaser slots scene: bet picker within `SlotMachine.MinBet`–`MaxBet`, reel spin animation that stops on the server result, win display, hold toggles and free respin, jackpot pot display, balance update in the header.

Visible after: slots playable in the browser, balance shared with Discord.
Needs you: play a few spins, then check `/casino tokens balance` and `/casino statistics` in Discord match (at the end).

### #411.5 Solo blackjack

- [ ] `WebBlackjackService` as in Design, with unit tests for AC6–AC8; hub methods `BlackjackDeal`, `BlackjackAct`.
- [ ] Phaser blackjack scene: bet, deal animation, hit / stand / double down (disabled when not allowed), dealer reveal, result and payout.

Visible after: solo blackjack playable in the browser.
Needs you: play a few hands, including a double down and leaving a hand idle (at the end).

### #411.6 Packaging and deployment

- [ ] `CasinoWeb/Dockerfile` (multi-stage: node builds the client, `dotnet publish`, `aspnet:8.0` runtime, non-root, read-only root filesystem with a writable keys volume).
- [ ] CI: client lint/test/build job; `Deploy Casino` workflow building `udc-casino:<sha>` and committing the tag to `k8s/prod/casino.yaml`, triggered by `CasinoWeb/**` and `DiscordBot/**`; the bot's Deploy Prod ignores `CasinoWeb/**` (AC11). Actions pinned to SHAs.
- [ ] `k8s/prod/casino.yaml`: Deployment (initContainer rendering `Settings.json` with `${DB_PASSWORD}` only, from `bot-settings-config`), Service, Ingress (Traefik, `letsencrypt-prod`), PVC for keys; `ExternalSecret` for the OAuth client secret. Security headers (CSP, HSTS, `nosniff`) in the app.
- [ ] Docs: `AGENTS.md` (commands, layout), `docs/features.md`, `docs/deployment.md`, `docs/casino.md` (web front end section).

Visible after: nothing until pushed to `master`; then the casino is online at its domain.
Needs you: choose the domain and point its DNS at the cluster; create the 1Password item with the OAuth client secret; add the prod redirect URL in the Discord application; ask for the push (stops the run).
