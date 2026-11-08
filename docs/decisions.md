# Decisions

Next ID: D9

## D1 Karma resets run in the bot, not in the database

`KarmaResetService` wakes hourly and resets weekly karma on Mondays, monthly karma on the 1st and yearly karma on January 1st. It records the last reset of each period in its own `karma_reset_meta` table and catches up missed resets at startup, so downtime never skips one.

Why: PostgreSQL has no built-in scheduler. `pg_cron` needs superuser rights and an extension that the containerized PostgreSQL does not ship.

Rejected: `pg_cron`.

## D2 Unquoted identifiers and signed integers in the schema

The DDL in `DatabaseService` never quotes identifiers, and properties mapped to columns use `int`/`long`, never `uint`/`ulong`.

Why: PostgreSQL lowercases unquoted names, and Insight.Database matches PascalCase properties to them case-insensitively; quoted names become case-sensitive and break that mapping. Npgsql rejects unsigned types (`DbType.UInt32 isn't supported`).

Rejected: quoted PascalCase column names; unsigned types carried over from the original MySQL schema.

## D3 Casino transaction types are stored as strings

The `transactiontype` column holds a readable string (`Game`, `Gift`, …) and `description` is plain `text`. `TokenTransaction.Kind` parses the string into the `TransactionKind` enum, defaulting to `Admin`; the enum is not named `TransactionType` because that name is the mapped string property.

Why: rows migrated from MySQL hold these strings, and some descriptions are plain text rather than JSON.

Rejected: an integer enum column; a `jsonb` description column.

## D4 Casino balances change only through single relative SQL statements

Every change to `casino_users.tokens` is one `UPDATE ... SET tokens = tokens + @delta ... RETURNING tokens` (or a conditional debit), never a read of the balance followed by a write of a total. Slots reserves its bet with a conditional debit before the spin.

Why: two token changes for one player at the same moment (a slots win and a table-game loss, two gifts, two `/daily` claims) each read the same starting balance, and the second write erased the first (#409). The row lock of a single statement serializes them with no application lock to keep in step across services.

Rejected: one per-user lock shared by every token-changing path (fragile: each new path must remember it, and it does not span bot instances); `SlotsService` keeps its per-user in-flight guard only for the hold-offer state and double clicks.

## D5 The web casino announces big wins through a webhook, not through the bot

`SlotsWinAnnouncer` posts a jackpot or rare slots win played on the web to the casino channel through a Discord webhook, with a static picture of the reels. The web casino still never holds the bot token, and big wins are the only thing it posts in Discord.

Why: a webhook needs one secret scoped to one channel and no running bot, so announcements do not depend on the bot being up.

Rejected: a table of web wins that the bot polls and posts (a schema change, and announcements depend on the bot running).

## D6 Unity releases come from the Release API and post on a cadence

`FeedService` reads releases from `services.api.unity.com/unity/editor/release/v1/releases` (one request per stream: LTS, Supported, Beta, Alpha) instead of the RSS feeds. LTS and Supported post at most once a day per LTS line, Beta weekly on Friday and Alpha weekly on Thursday, each digest merging the releases since the last post (day boundaries and the 12:00 UTC window are UTC). Only LTS and blog posts ping a role.

Why: the RSS items give every beta the same title (`Unity 6000.7.0 Beta`) and no `bN`, so betas were dropped as duplicates, and their description no longer holds release notes; the API gives the exact version, stream, date and notes URL. Beta and Alpha ship several times a week with notes of 300 to 500 KB, so a weekly digest keeps the channel readable without dropping a release.

Rejected: the RSS feeds with the notes link scraped from them (no exact version); one post per release for every stream (noise); pinging every stream (the roles would be muted). The API is public but not formally documented, so a failure logs a warning and the next check retries.

## D7 The startup report collects LoggingService's channel lines; services have no lifecycle interface

`StartupReport` takes every plain line `LoggingService` would post to bot-announcement between Ready and the end of startup (the startup report itself goes to `Channels.Startup`, bot-announcement when unset) and shows them in one edited card. Startup is done when the Ready handler has resolved its services and the steps it waits for (`Commands`, `Database`, `Slots art`) have ended, or after 60 s. Each step is registered with `BeginStep` before its `Task.Run` starts, so a step can't be missed.

Why: Discord.Net has no service-lifecycle events (only `Ready`, `Connected` and `Log`), and many services start implicitly: `CommandHandlingService` builds the modules, whose constructors resolve the services they inject. A Prepare/Start interface would touch every service and make that hidden order explicit, with the risk of missing gateway events if subscriptions move later.

Rejected: a Prepare/Start interface on every service; moving to `Microsoft.Extensions.Hosting` now. Hosted services (`IHostedService`/`BackgroundService`) are the idiomatic follow-up, starting with the loop services (Birthday, Reminder, KarmaReset, Welcome, Update, IntroductionWatcher), and would replace the manual cancellation handling in `Program.cs`.

## D8 The bot's GC conserves memory and runs without background collection

`DiscordBot.csproj` sets `System.GC.ConserveMemory` to 5 and `ConcurrentGarbageCollection` to false, so they go into the build's `runtimeconfig.json` for local runs, Docker and Kubernetes alike.

Why: the bot keeps about 35 MB of live objects, but after a spike the GC kept the memory committed. In a container with the production limits (768 MiB, half a CPU), the doc-index download spike left the bot at 532 MiB; with these settings it went back to 366 MiB, because the GC compacts and gives memory back when the heap is fragmented. With that download fixed, they still save about 15 MiB and cap what image renders and later downloads leave behind. At the bot's load, the extra pause time doesn't matter.

Rejected: Server GC (more memory, and the pod has half a CPU); `MALLOC_ARENA_MAX` (its gain could not be told apart from GC timing); `InvariantGlobalization` (at most 8 MB, and it changes culture-sensitive behaviour).
