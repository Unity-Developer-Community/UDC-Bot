# MySQL → PostgreSQL Migration

**Status:** Done
**Date:** 2025-07-15

## Summary

Migrated the entire data layer and infrastructure from MySQL 8.0 to PostgreSQL 16.

## Changes

### Application Code

- **NuGet:** Removed `MySql.Data`, added `Insight.Database.Providers.PostgreSQL` (brings Npgsql transitively)
- **DatabaseService:** `MySqlConnection` → `NpgsqlConnection`, DDL rewritten to PostgreSQL syntax, registered `PostgreSQLInsightDbProvider`
- **DBConnectionExtension:** `SHOW COLUMNS` → `information_schema` query
- **UserDBRepository:** `RAND()` → `RANDOM()`, `INSERT...SELECT` → `INSERT...RETURNING *`
- **CasinoRepository:** `LAST_INSERT_ID()` → `RETURNING *`, added `::jsonb` cast
- **KarmaResetService (new):** Replaces MySQL EVENT scheduler with C# polling loop for weekly/monthly/yearly karma resets
- **Program.cs:** Registered `KarmaResetService`
- **ModerationModule:** Removed orphaned `BouncyCastle` import

### Infrastructure

- **docker-compose.yml:** MySQL → postgres:16, phpMyAdmin → pgAdmin4
- **K8s manifests (dev + prod):** Renamed and rewrote mysql.yaml → postgresql.yaml, mysql-backup.yaml → postgresql-backup.yaml, phpmyadmin.yaml → pgadmin.yaml
- **K8s bot.yaml:** Init container updated to wait for PostgreSQL on port 5432
- **K8s external-secrets.yaml:** MySQL secrets → `postgresql-credentials` + `pgadmin-credentials`
- **K8s bot-config.yaml:** PostgreSQL connection string format
- **Settings.example.json:** Updated connection string and removed XAMPP comment

### Documentation

- **README.md:** Updated manual database setup instructions for PostgreSQL

## Checklist

- [x] NuGet packages updated
- [x] All SQL queries ported to PostgreSQL syntax
- [x] MySQL EVENT scheduler replaced with KarmaResetService
- [x] Docker Compose updated
- [x] K8s manifests updated and renamed (dev + prod)
- [x] Settings and connection strings updated
- [x] Build verified (0 errors)
- [x] No remaining MySQL references (except historical comment in KarmaResetService)

## Data Migration Note

Existing MySQL data needs to be manually exported and imported into PostgreSQL using `pg_dump`/`pg_restore` or a migration tool like `pgloader`.
