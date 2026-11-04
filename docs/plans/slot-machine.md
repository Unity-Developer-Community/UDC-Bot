# Slots for the casino

Branch: `feat/slot-machine`.

Status: `slots-animation` committed; next open item is `JackpotProps`, table creation in `DatabaseService` and repository methods (Jackpot storage). The four sample GIFs still await the user's approval.

## Goal

A solo slot machine in the casino: each spin shows an animated GIF of three reels stopping on the result, and part of every bet feeds one jackpot shared by the whole server. The house keeps a small, tested share of what is bet.

## Acceptance criteria

- AC1 WHEN a player runs `/casino slots spin bet:<1–100>` in a casino channel with enough tokens, THE SYSTEM SHALL reply privately with an animated GIF of the three reels stopping one after another on the outcome, and a text line with the three symbols, the win, the new balance and the current jackpot. Check: renderer tests, manual test.
- AC2 WHEN a spin settles, THE SYSTEM SHALL change the player's balance by exactly payout − bet and record one `Game` transaction with `game = Slots` (a free respin records its payout with a bet of 0). Check: settlement unit tests.
- AC3 IF the balance is below the bet, or the bet is outside 1–100, THE SYSTEM SHALL refuse privately and change no balance and no jackpot. Check: unit test, manual test.
- AC4 IF the player clicks spin again while their previous spin is still being processed, THE SYSTEM SHALL refuse the second click privately. Check: unit test of the per-user guard.
- AC5 WHEN the exact-return test runs, THE SYSTEM SHALL compute over every reel combination, with the best possible hold choices, a total return to players between 92% and 96% (base game + 5% jackpot contribution + the house-funded jackpot floor), wins on 20–35% of spins, and the jackpot line about once in 3,000–10,000 spins. Check: `SlotMachineTests`.
- AC6 WHEN a bet is paid, THE SYSTEM SHALL add exactly 5% of it to the shared jackpot, small bets included. Check: settlement unit tests.
- AC7 WHEN three 💎 land on the line, THE SYSTEM SHALL pay bet/100 of the jackpot (a 100-token bet takes it all), keep the rest in the pot, top the pot up to 1,000 tokens if it falls below, and post a public announcement with the GIF in the channel. Check: settlement unit tests, message-builder test.
- AC8 WHEN a win pays at least 20× the bet, THE SYSTEM SHALL also post it publicly in the channel with the GIF, naming the player without pinging them. Check: message-builder test.
- AC9 WHEN a losing spin is offered a hold (about 1 losing spin in 4), THE SYSTEM SHALL show a Hold toggle per reel and a free Respin. The respin keeps the held reels, spins the others once, pays at the original bet and never offers another hold. The offer expires after 10 minutes, at the player's next spin, or at a bot restart. Check: engine tests, manual test.
- AC10 WHEN a player runs `/casino slots info`, THE SYSTEM SHALL show the paytable (generated from the engine), the bet limits, the hold and jackpot rules and the current jackpot. Check: manual test.
- AC11 IF the GIF fails to render, THE SYSTEM SHALL still settle the spin and reply with the text result. Check: review, unit test of the fallback path.
- AC12 WHEN the bot starts, THE SYSTEM SHALL create the `casino_jackpot` table with its 1,000-token row if missing and leave every existing table untouched, on every restart. Check: run the statements twice against a throwaway `postgres:16` container.
- AC13 WHEN a GIF is rendered, it SHALL weigh under 2 MB, take under 1 second on a dev machine, and its last frame SHALL be identical to the static render of the outcome. Check: renderer tests.

## Design

- **Not a table game.** Slots never goes through `GameSession`/`CasinoGame` (no join, ready or seats). It gets its own engine, service and commands. It shows in `/casino statistics` and the global leaderboard through its `Game` transactions; the leaderboard's per-game filter stays limited to the `CasinoGame` enum.
- **Engine** (`Domain/Casino/Slots/`, pure, no Discord): symbol enum (🍒 🍋 🍇 🔔 ⭐ 7 💎), three reel strips (weighted by repeating symbols), one payline (middle row), paytable (three of a kind per symbol, plus one or two 🍒 paying small), the hold-offer chance, `Spin(rng)`, `Respin(previous, held reels, rng)`, and settlement math: payout, jackpot contribution, jackpot share and floor top-up. Randomness goes through a small interface backed by `RandomNumberGenerator` in production and seeded in tests. The paytable lives in code, not settings, because the exact-return test guards it.
- **Exact return**: enumerate every stop combination. For losing ones, add hold-offer chance × the best hold's expected value, memoized by (held reels, held symbols). The floor top-up is bounded by 10 × jackpot chance per token bet. No random simulation, so the test is exact and never flaky.
- **Jackpot storage**: new table `casino_jackpot (Id integer PRIMARY KEY, Amount bigint NOT NULL, UpdatedAt timestamptz NOT NULL)`, one row `Id = 1`. `Amount` is stored in hundredths of a token, so a 1-token bet adds exactly 0.05 and is shown rounded down. It is created by its own `CREATE TABLE IF NOT EXISTS` + `INSERT … ON CONFLICT DO NOTHING` in `DatabaseService`, outside the existing casino-tables block (which only runs when `casino_users` is missing). Repository methods on `ICasinoRepo`: get, add a contribution (`UPDATE … SET Amount = Amount + @c RETURNING Amount`), claim a share in one atomic statement (lock the row, take the share, apply the floor, return the share). Identifiers unquoted, `long` properties (D2).
- **Service** (`Services/Fun/Casino/SlotsService.cs`, singleton in `Program.ConfigureServices`): per-user guard (a `ConcurrentDictionary` of in-flight user IDs), balance check, spin, jackpot contribution or claim, `CasinoService.UpdateUserTokens(payout − bet, Game, {game: Slots, jackpot: <share> when won})`, pending holds in memory keyed by user (outcome, bet, held reels, expiry), and the public-post decision. Order: claim/contribute → settle the player. If settling fails after a claim, log the user and amount as an error so an admin can credit it with `/admin casino tokens-add`.
- **Renderer** (`Services/Fun/Casino/SlotsRenderer.cs`, singleton, Magick.NET): about 400×220, a machine frame drawn with Magick, 3 visible rows per reel, the middle one the payline. Reel strips are rendered once at first use and frames are cropped from them. About 14 fps; the reels stop at about 1.2, 1.8 and 2.4 s, with motion blur while spinning; the last frame is held 20 s and the loop count set to once (Discord may loop anyway, which is accepted). A fixed palette avoids per-frame color reduction. One render at a time (`SemaphoreSlim(1)`), since prod has half a CPU (`k8s/prod/bot.yaml`). Output is a `MemoryStream`, never on disk.
- **Art**: Microsoft Fluent Emoji 3D PNGs (MIT) for 🍒 🍋 🍇 🔔 ⭐ 💎, and a red "7" drawn with a bundled font, in `DiscordBot/Assets/slots/` with the MIT notice beside them. The `Dockerfile` copies asset folders one by one, so `Assets/slots/` needs its own `COPY` line or prod has no art.
- **Commands** (`Modules/Fun/Casino/CasinoSlashModule.Slots.cs`, a nested `[Group("slots", …)]` like `TokenCommands`): `spin bet` (`[MinValue(1)] [MaxValue(100)]`, server-side check too) and `info`. Both reuse the casino channel check. A spin defers privately, then follows up with an embed whose image is `attachment://spin.gif`. Buttons: `slots_spin:<bet>` (spin again at the same bet; the bet is re-validated), and when a hold is offered, `slots_hold:<reel>` toggles (label "🔒 Held" vs "Hold", not only the color) and `slots_respin`. Buttons update the private message in place. Public posts use `AllowedMentions.None`. The text line repeats the symbols, so players who turned off GIF autoplay (who see only the first frame) still get the result.
- **Out of scope**: pending bets in table games are not counted against the balance (#347). Two balance updates for the same player at the same moment from different games can overwrite each other (`UpdateUserTokens` reads, then writes); the per-user guard only stops slots-vs-slots.

## Checklist

### Slots engine and paytable

- [x] Baseline: `dotnet build` and `dotnet test`, note existing warnings and failures
- [x] Engine, paytable, hold, settlement math in `Domain/Casino/Slots/`
- [x] `DiscordBot.Tests/Domain/Casino/SlotMachineTests.cs`: exact-return bounds (AC5), settlement (AC2, AC6, AC7 share and floor), respin keeps held reels and never re-offers a hold (AC9), seeded spins are reproducible
- Visible after: nothing: no command yet.
- Needs you: nothing.
- Baseline: 47 build warnings, 0 errors, 250 tests passing. Tuned numbers: return 93.3–93.6%, wins on 29% of spins, jackpot line about once in 4,300 spins.

### Spin animation

- [x] Download the Fluent Emoji 3D PNGs into `Assets/slots/` (check the upstream LICENSE and that each glyph is included), add the MIT notice, draw the 7
- [x] `SlotsRenderer` (animated GIF + static frame), `Dockerfile` `COPY` line for `Assets/slots/`, test project copies `Assets/slots/` to its output
- [x] `DiscordBot.Tests/Services/SlotsRendererTests.cs`: AC13; writes a sample of a loss, a 🍒 win, a 7-7-7 and a 💎 jackpot to `slots-samples/` in the test output folder
- Decided during the run: the six Fluent Emoji PNGs (cherry, lemon, grape, bell, star, diamond = Gem stone) are copied as-is at 256 px into `Assets/slots/` with the upstream MIT `LICENSE-FluentEmoji.txt` and a short `README.md`; the red 7 is drawn at render time from the already bundled `Assets/fonts/ConsolasBold.ttf` instead of shipping a PNG, so the item needs no new font. Alternative: ship a pre-drawn `seven.png`; change `DrawSeven` in `SlotsRenderer`.
- Decided during the run: the static render is a PNG of the same palette-mapped frame as the last GIF frame (not a truecolor picture), so the two are pixel-identical (AC13). The palette is built once from sample frames and applied through a lookup table (Magick's own remap cost ~30 ms a frame, a spin took ~1.1 s instead of ~0.4 s). Alternative: truecolor static PNG, which would differ slightly from the GIF; change `RenderStatic`.
- Decided during the run: no cylinder shading on the reels (a gradient multiplied onto the strips turned into banding and muddy colors with 256 colors); opaque dark bezels at the top and bottom of each window and a thin red payline stand in. Alternative: shading with dithering, bigger and shimmering GIFs; change `BuildOverlay`.
- Measured on the dev machine: warm spin render about 0.4 s, GIF about 460 KB, static render 15 ms; the first render after start takes about 2.7 s (art loading and palette). The commands item should resolve `SlotsRenderer` in the `Ready` handler and warm it with one `RenderStaticAsync` so no player waits for it.
- Visible after: four sample GIFs.
- Needs you: open the four sample GIFs and approve the look (stops the run).

### Jackpot storage

- [ ] `JackpotProps`, table creation in `DatabaseService`, repository methods on `ICasinoRepo`
- [ ] Run the creation and the three statements twice against a throwaway `docker run --rm postgres:16` (never the dev database): AC12, atomic claim, floor
- Visible after: nothing: used by the commands item.
- Needs you: nothing (the new table is approved with this plan).

### Slots commands

- [ ] `SlotsService`, `CasinoSlashModule.Slots.cs`, registration in `Program.ConfigureServices`
- [ ] Message builder tests: private result, public post for ≥ 20× and jackpot (AC8, AC7), render-failure text fallback (AC11), per-user guard (AC4)
- [ ] Docs: `docs/casino.md` (Slots section: engine, service, renderer, jackpot table, why it is not a table game), `docs/features.md` Casino row, `README.md` feature line
- Visible after: `/casino slots spin` and `/casino slots info` in Discord.
- Needs you: run the bot locally against your test server and play a few spins, a hold and a respin (at the end).
