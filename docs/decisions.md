# Decisions

Next ID: D4

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
