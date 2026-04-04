# MySQL → PostgreSQL Code & Schema Changes

> Reference document for PR #375 (`feature/postgre`). Describes **what** was changed in the codebase and **why**.
>
> For the operational data migration procedure, see [data-migration-mysql-to-postgresql.md](../data-migration-mysql-to-postgresql.md).

## Table of Contents

- [1. Database Schema Differences](#1-database-schema-differences)
- [2. Application Code Changes](#2-application-code-changes)
- [3. Infrastructure Changes](#3-infrastructure-changes)
- [4. pgloader Gotchas](#4-pgloader-gotchas)
- [5. Files Summary](#5-files-summary)

---

## 1. Database Schema Differences

### 1.1. Type Mapping

PostgreSQL does not support unsigned integer types. All `uint`/`ulong` C# properties mapped to DB columns were changed to `int`/`long`.

| MySQL Type | PostgreSQL Type | C# Type | Affected Columns |
|-----------|----------------|---------|-----------------|
| `int unsigned` | `integer` | `int` | All `Id`, Karma*, KarmaGiven, Level |
| `bigint unsigned` | `bigint` | `long` | Exp, Tokens, Amount |
| `datetime` | `timestamptz` | `DateTime` | CreatedAt, UpdatedAt, LastDailyReward |
| `auto_increment` | `SERIAL` | — | All primary keys |
| `varchar(N)` | `varchar(N)` | `string` | No change |
| `text` | `text` | `string` | No change |

### 1.2. Identifier Casing

PostgreSQL lowercases all unquoted identifiers. The DDL in `DatabaseService.cs` uses unquoted names (e.g., `UserID` becomes `userid` in the PG catalog). Insight.Database maps C# PascalCase properties to PG lowercase columns automatically via case-insensitive matching.

**Never use double-quoted identifiers** in PostgreSQL DDL — it forces case-sensitivity and breaks Insight.Database's automatic mapping.

### 1.3. `token_transactions` Table

This table was redesigned to match the MySQL schema exactly, enabling direct data migration via pgloader (which maps columns by name, case-insensitively).

| Column | MySQL | PostgreSQL (original design) | PostgreSQL (current) |
|--------|-------|------------------------------|---------------------|
| Id | `int unsigned auto_increment` | `SERIAL` | `SERIAL` |
| UserID | `varchar(32) NOT NULL` | `varchar(32) NOT NULL` | `varchar(32) NOT NULL` |
| TargetUserID | `varchar(32) NULL` | *(missing)* | `varchar(32) DEFAULT NULL` |
| Amount | `bigint NOT NULL` | `bigint NOT NULL` | `bigint NOT NULL` |
| TransactionType | `varchar(50) NOT NULL` | `integer NOT NULL` (named `Type`) | `varchar(50) NOT NULL` |
| Description | `text NULL` | `jsonb DEFAULT NULL` (named `DetailsJson`) | `text DEFAULT NULL` |
| CreatedAt | `datetime NOT NULL` | `timestamptz NOT NULL` | `timestamptz NOT NULL` |

**Why the change:** The original PG design used an integer enum for `Type` and `jsonb` for `DetailsJson`. These column names and types did not match MySQL, so pgloader couldn't map them (it maps by column name). Matching the MySQL schema allows direct, zero-transformation migration.

**Trade-offs:**
- Lost: `jsonb` native JSON queries and indexing on the Description column
- Gained: Direct migration compatibility, simpler schema, human-readable `TransactionType` values

### 1.4. `karma_reset_meta` — New Table (PG only)

MySQL has a built-in EVENT scheduler that resets weekly/monthly/yearly karma columns. **PostgreSQL has no EVENT scheduler.**

The standard PG alternative is the `pg_cron` extension, but it requires superuser access and isn't always available in managed/containerized deployments.

**Solution:** A new C# background service (`KarmaResetService`) polls hourly and tracks last-reset timestamps in a `karma_reset_meta` table. This table is created automatically on startup — it has no MySQL counterpart and is not part of the data migration.

### 1.5. `users.Birthday` Column

MySQL has a `Birthday` column that was dropped in the PostgreSQL schema. The pgloader migration handles this with temporary column add/drop (see [data-migration-mysql-to-postgresql.md](../data-migration-mysql-to-postgresql.md)).

---

## 2. Application Code Changes

### 2.1. NuGet Packages

| Package | Old | New |
|---------|-----|-----|
| `MySql.Data` | (removed) | — |
| `Insight.Database` | 8.0.5 | 8.0.6 |
| `Insight.Database.Providers.PostgreSQL` | (new) | 8.0.6 |

### 2.2. Database Layer (`DatabaseService.cs`)

- `MySqlConnection` → `NpgsqlConnection`
- Registered `PostgreSQLInsightDbProvider` on startup
- Full DDL rewrite to PostgreSQL syntax (`SERIAL`, `timestamptz`, no double-quotes, no unsigned types)

### 2.3. `uint` → `int` / `ulong` → `long`

Npgsql throws `DbType.UInt32 isn't supported by PostgreSQL or Npgsql` for unsigned C# types. All 17+ files with DB-mapped unsigned properties were changed:

- `Domain/ProfileData.cs` — karma/level fields
- `Domain/Casino/CasinoUser.cs` — Id
- `Domain/Casino/Game.cs`, `GamePlayer.cs`, `GameSession.cs`
- `Domain/Casino/Games/Cards/Blackjack/Blackjack.cs`
- `Domain/Casino/Games/Cards/Poker/Poker.cs`
- `Domain/Casino/Games/RockPaperScissors/RockPaperScissors.cs`
- `Extensions/CasinoRepository.cs`, `UserDBRepository.cs`
- `Modules/Casino/CasinoSlashModule.cs`, `CasinoSlashModule.Games.cs`
- `Modules/UserModule.cs`
- `Services/Casino/CasinoService.cs`, `GameService.cs`
- `Services/UserService.cs`
- `Settings/Deserialized/Settings.cs`

### 2.4. SQL Syntax Differences

| MySQL | PostgreSQL | Files |
|-------|-----------|-------|
| `RAND()` | `RANDOM()` | `UserDBRepository.cs` |
| `INSERT...SELECT LAST_INSERT_ID()` | `INSERT...RETURNING *` | `UserDBRepository.cs`, `CasinoRepository.cs` |
| `SHOW COLUMNS FROM...` | `information_schema.columns` query | `DBConnectionExtension.cs` |
| `::jsonb` cast | Removed (column is now `text`) | `CasinoRepository.cs` |

### 2.5. `TransactionType` Enum → `TransactionKind`

Renamed to avoid a naming collision with the new `TransactionType` string DB column property on `TokenTransaction`.

| Before | After |
|--------|-------|
| `enum TransactionType { ... }` | `enum TransactionKind { ... }` |
| `transaction.Type` (enum property) | `transaction.Kind` (computed from `TransactionType` string) |
| `TransactionType.Game` | `TransactionKind.Game` |

**How the mapping works:**

1. DB column `transactiontype` (varchar) maps to `TokenTransaction.TransactionType` (string property)
2. `TokenTransaction.Kind` is a computed property:
   - Getter: parses `TransactionType` string to `TransactionKind` enum (case-insensitive, defaults to `Admin`)
   - Setter: converts enum to string via `.ToString()` and assigns to `TransactionType`
3. `CasinoProps.TransactionType` constant = `"TransactionType"` (the column name)
4. `GetTransactionsOfType()` now takes a `string` parameter instead of the enum, called with `nameof(TransactionKind.Game)`

### 2.6. `DetailsJson` → `Description`

The `DetailsJson` property (mapped to `jsonb`) was replaced by `Description` (mapped to `text`). The `Details` dictionary is still used internally — it's JSON-serialized to plain text.

**Backward compatibility for migrated MySQL data:** The `Description` setter handles both formats:
- JSON strings (new data from the bot): deserialized to `Dictionary<string, string>`
- Plain text (old MySQL data): caught via `JsonException`, stored as `{ "text": "<value>" }`

### 2.7. `TargetUserID` Column

Added to match MySQL schema. Currently **unused** by application code — the target user for gift transfers is stored in `Details["from"]`/`Details["to"]`. The column exists for migration compatibility and potential future use.

### 2.8. `KarmaResetService` (new)

**File:** `Services/KarmaResetService.cs` — registered in `Program.cs` as singleton.

Replaces MySQL EVENT scheduler:
- Background loop checks hourly
- Resets `KarmaWeekly` on Mondays, `KarmaMonthly` on 1st, `KarmaYearly` on Jan 1st
- Persists timestamps in `karma_reset_meta` table (auto-created)
- Catches up missed resets on startup (e.g., bot was down)

### 2.9. Other Changes

- **`DBConnectionExtension.cs`**: Changed `using MySql.Data.MySqlClient` → `using Npgsql`. `ColumnExists` now queries `information_schema.columns` with parameterized SQL and `LOWER()` for case-insensitive matching.
- **`ModerationModule.cs`**: Removed orphaned `BouncyCastle` import.

---

## 3. Infrastructure Changes

### 3.1. Docker Compose

| Service | Before | After |
|---------|--------|-------|
| Database | `mysql:8.0` | `postgres:16` |
| Admin UI | phpMyAdmin | `adminer:4` (Dracula theme) |
| Port | 3306 | 5432 |

Environment variables: `MYSQL_*` → `POSTGRES_*`. Volume: `mysql_data` → `postgres_data`.

### 3.2. Kubernetes Manifests (dev + prod)

| File | Change |
|------|--------|
| `postgresql.yaml` | **New.** StatefulSet + Service + PVC. |
| `postgresql-backup.yaml` | **New.** CronJob using `pg_dump` + S3 upload. |
| `mysql-backup.yaml` | **Deleted.** |
| `phpmyadmin.yaml` → `adminer.yaml` | **Renamed and rewritten.** Adminer uses ~13 Mi RAM vs pgAdmin's ~194 Mi. |
| `pgadmin.yaml` | **Deleted** (was added then replaced by Adminer). |
| `pgloader-migration.yaml` | **New.** ConfigMap + Job for data migration. |
| `external-secrets.yaml` | Added `postgresql-credentials`. MySQL secrets kept temporarily. |
| `bot-config.yaml` | Connection string changed to PostgreSQL format. |
| `bot.yaml` | Init container waits for port 5432 instead of 3306. |

### 3.3. ExternalSecrets (1Password)

**New permanent items:**

| 1Password Item | K8s Secret | Purpose |
|----------------|-----------|---------|
| `PostgreSQL Server - Dev` | `postgresql-credentials` | PG password (dev) |
| `PostgreSQL Server - Prod` | `postgresql-credentials` | PG password (prod) |

**Temporary items (remove after migration):**

| 1Password Item | K8s Secret | Purpose |
|----------------|-----------|---------|
| `MySQL Server - Root User - Dev/Prod` | `mysql-credentials` | For pgloader |
| `MySQL Server - UDC User - Dev/Prod` | `mysql-user-credentials` | For pgloader |

### 3.4. Resource Comparison

Dev PostgreSQL stack uses ~47% less RAM than prod MySQL stack:

| Component | MySQL Stack (prod) | PostgreSQL Stack (dev) |
|-----------|-------------------|----------------------|
| Database | ~614 Mi | ~303 Mi |
| Admin UI | ~230 Mi (phpMyAdmin) | ~13 Mi (Adminer) |
| **Total** | **~844 Mi** | **~400 Mi (est.)** |

---

## 4. pgloader Gotchas

Lessons learned from 5 iterations of pgloader testing:

| Issue | What Happened | Solution |
|-------|--------------|---------|
| `CAST` not supported in `data only` mode | pgloader threw syntax error on `CAST type int unsigned to integer` | Remove CAST — pgloader handles type coercion automatically in data-only mode |
| Env vars ignored | `PGPASSWORD` and `MYSQL_PWD` had no effect | Embed passwords directly in connection URLs (pgloader uses its own connection libraries) |
| Schema mismatch | pgloader couldn't find schema "udcbot" in PostgreSQL | Add `ALTER SCHEMA 'udcbot' RENAME TO 'public'` — MySQL DB name maps to a PG schema |
| Column name mismatch | `token_transactions` had different column names in MySQL vs PG | Changed PG schema to match MySQL column names (see section 1.3) |
| Missing `Birthday` column | MySQL `users` has `Birthday`, PG doesn't | Temp `ALTER TABLE ADD/DROP COLUMN` in BEFORE/AFTER LOAD |

---

## 5. Files Summary

### New Files

| File | Purpose |
|------|---------|
| `Services/KarmaResetService.cs` | Replaces MySQL EVENT scheduler |
| `k8s/*/postgresql.yaml` | PostgreSQL StatefulSet + Service + PVC |
| `k8s/*/postgresql-backup.yaml` | CronJob for PG backups to S3 |
| `k8s/*/adminer.yaml` | Adminer web UI |
| `k8s/*/pgloader-migration.yaml` | Data migration Job |
| `docs/plans/data-migration-mysql-to-postgresql.md` | Operational migration guide |
| `docs/plans/mysql-to-postgresql-changes.md` | This document |
| `docs/plans/done/mysql-to-postgresql-migration.md` | Original PR summary |

### Modified Files (17+ C# + infra)

| File | Change |
|------|--------|
| `DiscordBot.csproj` | NuGet changes |
| `Domain/Casino/CasinoUser.cs` | `uint`→`int`, enum rename, schema property renames |
| `Domain/ProfileData.cs` | `uint`→`int` |
| `Domain/Casino/Game*.cs` (4 files) | `uint`→`int` |
| `Extensions/CasinoRepository.cs` | SQL syntax, parameter types |
| `Extensions/DBConnectionExtension.cs` | MySQL→Npgsql, `information_schema` |
| `Extensions/UserDBRepository.cs` | `RAND()`→`RANDOM()`, `RETURNING *` |
| `Modules/Casino/CasinoSlashModule*.cs` | Enum rename |
| `Modules/ModerationModule.cs` | Removed orphaned import |
| `Modules/UserModule.cs` | `uint`→`int` |
| `Services/DatabaseService.cs` | Complete DDL rewrite |
| `Services/Casino/CasinoService.cs` | Enum rename |
| `Services/Casino/GameService.cs` | Enum rename |
| `Services/UserService.cs` | `uint`→`int`, path changes |
| `Settings/Deserialized/Settings.cs` | `uint`→`int` |
| `Program.cs` | Registered `KarmaResetService` |
| `docker-compose.yml` | MySQL→PostgreSQL, phpMyAdmin→Adminer |
| `README.md` | Updated setup instructions |
| `k8s/*/external-secrets.yaml` | Added PG secrets |
| `k8s/*/bot-config.yaml` | PG connection string |
| `k8s/*/bot.yaml` | Init container port 5432 |

### Deleted Files

| File | Reason |
|------|--------|
| `k8s/*/mysql-backup.yaml` | Replaced by `postgresql-backup.yaml` |
| `k8s/*/pgadmin.yaml` | Replaced by Adminer |

### Temporary Files (remove after migration)

| File | Remove When |
|------|------------|
| `k8s/*/mysql.yaml` | After prod data migration verified (24-48h) |
| `k8s/*/pgloader-migration.yaml` | After prod migration complete |
| MySQL ExternalSecret entries | After MySQL fully decommissioned |
