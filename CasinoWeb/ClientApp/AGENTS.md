# AGENTS.md

The browser client of the web casino (`../README.md`): Vite + strict TypeScript, scaffolded with create-corniflex. The repository's `AGENTS.md` covers the rest of the project.

## Commands

- Dev server: `npm run dev` on `http://localhost:5080`. It proxies `/api`, `/hub`, `/login` and `/signin-discord` to Kestrel on `:5081` (`dotnet run` from `CasinoWeb/`), so the browser, the OAuth redirect URL and the casino's public origin are all `:5080`.
- Build: `npm run build` (`tsc -b`, then `vite build` into `../wwwroot`, gitignored, served by Kestrel in production).
- Lint: `npm run lint` / `npm run lint:fix`. Test: `npm test`.

Run lint, test and build before considering work done.

## Layout

- `src/main.ts`: entry point. `src/app.ts`: picks the screen from the URL and the session and renders it.
- `src/api.ts`: `/api/me` and `/api/logout`. `src/routes.ts`: pure screen selection and the login URL.
- `src/connection.ts`: the SignalR connection as a status (`connecting`, `connected`, `reconnecting`, `offline`) for the header, `invoke` for hub calls, and `onSessionLost` when `/api/me` says the login is gone (the library hides the 401 of a failed negotiation). `app.ts` starts it and sends the player to `/login` on a lost session; without one the games are not playable.
- `src/protocol.ts`: the hub's wire types (mirror of `CasinoWeb/Hubs/HubModels.cs` and `TableHubModels.cs`) and the refusal codes. `src/casinoApi.ts`: the typed hub calls and the `TablesChanged` / `TableUpdated` pushes; a refused call becomes `CallRefusedError`.
- `src/games/slots/`: `slotsGame.ts` the controller (intents to the server, no game rules, tested with a fake stage and api), `slotsScene.ts` the Phaser machine (loaded lazily), `slotsPage.ts` the DOM around it, `reels.ts` the reel geometry, `art.ts` the pictures from `DiscordBot/Assets/slots`.
- `src/games/blackjack/`: the same split: `blackjackGame.ts` the controller, `blackjackScene.ts` the Phaser table (cards drawn in code, loaded lazily), `blackjackPage.ts` the DOM around it, `table.ts` the order cards are dealt and turned over. The server never sends the dealer's face-down card before the hand ends.
- `src/games/cards/`: what the card games share: `cardText.ts` card names and suit symbols for the text that repeats the table, `cardDrawing.ts` the Phaser card faces and theme tokens for the scenes.
- `src/games/poker/`: the same split for the multiplayer tables: `pokerGame.ts` the controller (the tables list, the seated table, the views the server pushes, no rules: what the player may do comes with each view; tested with a fake api and stage), `pokerScene.ts` the Phaser table (seats around an oval, drawn again on every view; loaded lazily), `pokerPage.ts` the DOM around it (actions, raise amount, seats, cards and result in words, turn countdown ticking locally between views), `layout.ts` the seat geometry, `fixtures.ts` views for the tests. The server sends each member only their own hole cards; the views carry no balance, so the controller asks `GetState` again at the end of a hand. `src/views/tables.ts` lists the open tables and opens one. A reload goes back to the seated table (`app.ts` opens it once connected).
- `src/views/`: DOM rendering (`pages`, `header`, `lobby`, `tables`). `src/dom.ts`: element builder using `textContent` only, so nothing from the server is parsed as HTML.
- `/access-denied` and `/login-failed` are redirect targets of the server, which answers them with `index.html` and the failure status; the client draws the message.

## Conventions

- Colors come from the tokens in `src/style.css`; never hard-code one. State is never shown by color alone (the connection status has an icon and a word).
- Import from `@/…`; tests sit next to the source as `<name>.test.ts`.
- 2-space indent, single quotes, semicolons (ESLint, `@antfu/eslint-config`).
- The app name comes from `brand.json`.
