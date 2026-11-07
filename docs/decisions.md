# Decisions

Next ID: D6

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
