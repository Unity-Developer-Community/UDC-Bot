# CasinoWeb

The web front end of the UDC casino: a separate ASP.NET Core 8 process that signs members in with Discord and plays the casino games against the same PostgreSQL tables as the bot. It reuses the bot's services (`DiscordBot` project reference) and never holds the bot token.

## Local setup

1. **Settings file.** `CasinoWeb` reads the bot's `DiscordBot/Settings/Settings.json` (see `appsettings.Development.json`) and uses only `GuildId`, `DbConnectionString` and `Casino`. Start the database with `docker compose up db` as for the bot.
2. **Discord application.** In the [Discord developer portal](https://discord.com/developers/applications), open the bot's application, then OAuth2:
   - add the redirect URL `http://localhost:5080/signin-discord`;
   - copy the client ID and generate or reveal the client secret.
3. **Secrets**, from `CasinoWeb/`:

   ```bash
   dotnet user-secrets set Discord:ClientId <client id>
   dotnet user-secrets set Discord:ClientSecret <client secret>
   ```

4. **Run** two processes, then open `http://localhost:5080`:
   - the server, from `CasinoWeb/`: `dotnet run` (listens on `http://localhost:5081`);
   - the client, from `CasinoWeb/ClientApp/`: `npm install` once, then `npm run dev` (listens on `http://localhost:5080` and proxies the API to the server). The Discord redirect URL, the cookie and the Origin check all use `:5080`, so browse there, not on `:5081`.
   - `GET /login` goes through Discord; a guild member lands back on `/` with a cookie and sees the lobby, anyone else lands on `/access-denied`.
   - `GET /api/me` answers with the logged-in member's name, avatar and balance (`401` without a login).

`npm run build` in `ClientApp/` writes the client to `CasinoWeb/wwwroot/` (gitignored), which the server serves; this is how it runs in production.

## Settings

| Key (environment variable) | Meaning |
| --- | --- |
| `Casino__PublicOrigin` | The address players use, such as `https://casino.example.com` (no path). OAuth redirect URLs, cookies and the Origin check derive from it. |
| `Casino__BotSettingsPath` | The bot's `Settings.json`. Default `Settings/Settings.json`. |
| `Casino__KeysPath` | Folder for the Data Protection keys, so a restart keeps everyone logged in. Empty uses the framework default. The keys are not encrypted at rest: the folder must be private to the process. |
| `Discord__ClientId`, `Discord__ClientSecret` | The bot's Discord application. |

## Tests

`dotnet test CasinoWeb.Tests` runs without Discord or PostgreSQL: Discord and the casino table are replaced by fakes. The tests need the ASP.NET Core 8 runtime (`winget install Microsoft.DotNet.AspNetCore.8`): on a newer runtime only, JSON responses fail inside the test server.
