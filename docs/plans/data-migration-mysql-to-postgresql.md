# MySQL → PostgreSQL Data Migration Plan

## Overview

Migrate production data from MySQL (`udc-bot-prod`) to PostgreSQL. The schema has already been migrated (DDL in `DatabaseService.cs`). This plan covers **data migration and the production cutover strategy**.

## Cutover Strategy

The PR (`feature/postgre`) replaces MySQL with PostgreSQL in k8s manifests. This means we **lose MySQL** once the new manifests are deployed. The migration must follow this sequence:

### Phase 0: Test on Dev (current step)

**Goal:** Validate the pgloader migration from prod MySQL → dev PostgreSQL before touching prod.

1. **Scale down dev bot** — `kubectl scale deployment/udc-bot -n udc-bot-dev --replicas=0`
2. **Deploy the pgloader Job** — `kubectl apply -f k8s/dev/pgloader-migration.yaml`
   - Creates a temporary ExternalSecret for MySQL prod credentials in dev namespace
   - Runs pgloader in `data only` mode (tables already exist from bot startup)
   - Truncates existing dev data, imports prod data, resets sequences
3. **Check Job logs** — `kubectl logs job/mysql-to-postgresql-migration -n udc-bot-dev`
4. **Verify data** — connect via Adminer or `kubectl exec` and check row counts
5. **Scale dev bot back up** — `kubectl scale deployment/udc-bot -n udc-bot-dev --replicas=1`
6. **Test bot commands** — `!profile`, `thanks @someone`, casino commands
7. **Clean up** — delete the Job and temporary MySQL secret:
   ```bash
   kubectl delete job mysql-to-postgresql-migration -n udc-bot-dev
   kubectl delete externalsecret mysql-prod-credentials -n udc-bot-dev
   kubectl delete secret mysql-prod-credentials -n udc-bot-dev
   ```

**File:** `k8s/dev/pgloader-migration.yaml` (ExternalSecret + ConfigMap + Job)

### Phase 1: Prepare (before merging PR)

1. **Deploy PostgreSQL alongside MySQL in prod** — add `postgresql.yaml` to `k8s/prod/` while keeping `mysql.yaml` intact. Both databases run simultaneously.
2. **Scale down the bot** — `kubectl scale deployment/udc-bot -n udc-bot-prod --replicas=0` — freeze writes to MySQL.
3. **Backup MySQL** — `mysqldump` as safety net.

### Phase 2: Migrate (both databases running)

4. **Run pgloader Job** — streams data from MySQL → PostgreSQL within the cluster.
5. **Verify data** — row counts, spot-checks, sequence values.

### Phase 3: Switch (controlled cutover)

6. **Deploy the new bot** (from `feature/postgre`) pointing to PostgreSQL.
7. **Verify bot commands** — `!profile`, karma, casino.
8. **Keep MySQL running** for 24-48h as rollback safety net.

### Phase 4: Cleanup

9. **Remove MySQL** from prod k8s manifests (`mysql.yaml`, ExternalSecrets).
10. **Remove MySQL 1Password items** if no longer needed.
11. **Move this plan** to `docs/plans/done/`.

## Data Inventory

| Table | Rows | Key Columns | Notes |
|-------|------|-------------|-------|
| `users` | **54,302** | `ID` (auto-inc), `UserID` (varchar PK) | Karma, XP, Level data |
| `casino_users` | **22** | `Id` (auto-inc), `UserID` (varchar unique) | Token balances |
| `token_transactions` | **1,441** | `Id` (auto-inc), `UserID`, `Amount`, `Type` | Transaction history |

### Schema Differences

| MySQL Type | PostgreSQL Type | Affected Columns |
|-----------|----------------|-----------------|
| `int unsigned` | `integer` | Karma*, KarmaGiven, Level, Id |
| `bigint unsigned` | `bigint` | Exp, Tokens |
| `timestamp` (MySQL default) | `timestamp` | CreatedAt, UpdatedAt, LastDailyReward |
| `varchar(32)` | `varchar(32)` | UserID (no change) |

## Migration Strategy

**Recommended: `pgloader`** — purpose-built MySQL-to-PostgreSQL ETL tool.

### Why pgloader

- Handles type mapping automatically (unsigned → signed)
- Streams data (no intermediate files for 54K rows)
- Single command, declarative config
- Handles timestamp conversion, encoding, and NULL differences

### Alternative: CSV export/import

Simpler but manual. Good fallback if pgloader isn't available in the cluster.

---

## Execution Plan

### Prerequisites

- [ ] PostgreSQL deployed in prod **alongside** MySQL (temporary dual-database state)
- [ ] Bot scaled to 0 replicas (no writes during migration)
- [ ] MySQL backup taken
- [ ] Cross-namespace DNS verified: `mysql.udc-bot-prod.svc.cluster.local` reachable from migration pod

### Option A: pgloader (Recommended)

#### Step 1: Create pgloader Job

Deploy a one-shot Kubernetes Job that runs pgloader with a config targeting MySQL→PostgreSQL.

```yaml
apiVersion: batch/v1
kind: Job
metadata:
  name: mysql-to-postgresql-migration
  namespace: udc-bot-dev
spec:
  template:
    spec:
      containers:
        - name: pgloader
          image: ghcr.io/dimitri/pgloader:latest
          command:
            - pgloader
            - /config/migration.load
          volumeMounts:
            - name: config
              mountPath: /config
      volumes:
        - name: config
          configMap:
            name: pgloader-config
      restartPolicy: Never
  backoffLimit: 1
```

#### Step 2: pgloader Configuration

```
LOAD DATABASE
  FROM mysql://root:PASSWORD@mysql.udc-bot-prod.svc.cluster.local:3306/udcbot
  INTO postgresql://udcbot:PASSWORD@postgresql.udc-bot-dev.svc.cluster.local:5432/udcbot

WITH include drop, create tables, create indexes, reset sequences,
     workers = 2, concurrency = 1

SET maintenance_work_mem to '128MB'

CAST type int unsigned to integer,
     type bigint unsigned to bigint

-- Only migrate these tables
INCLUDING ONLY TABLE NAMES MATCHING 'users', 'casino_users', 'token_transactions'

BEFORE LOAD DO
  $$ TRUNCATE users, casino_users, token_transactions CASCADE; $$
;
```

#### Step 3: Verify

```sql
SELECT COUNT(*) FROM users;         -- Expect: 54,302
SELECT COUNT(*) FROM casino_users;  -- Expect: 22
SELECT COUNT(*) FROM token_transactions; -- Expect: 1,441
```

### Option B: CSV Export/Import (Fallback)

#### Step 1: Export from MySQL

```bash
# Run inside MySQL pod
mysqldump -u root -p udcbot users casino_users token_transactions \
  --compatible=postgresql --no-create-info --complete-insert \
  --skip-quote-names > /tmp/data.sql
```

Or per-table CSV:

```bash
mysql -u root -p udcbot -e "SELECT * FROM users" --batch > /tmp/users.tsv
mysql -u root -p udcbot -e "SELECT * FROM casino_users" --batch > /tmp/casino_users.tsv
mysql -u root -p udcbot -e "SELECT * FROM token_transactions" --batch > /tmp/token_transactions.tsv
```

#### Step 2: Transfer files

```bash
kubectl cp udc-bot-prod/mysql-pod:/tmp/users.tsv ./users.tsv
kubectl cp udc-bot-prod/mysql-pod:/tmp/casino_users.tsv ./casino_users.tsv
kubectl cp udc-bot-prod/mysql-pod:/tmp/token_transactions.tsv ./token_transactions.tsv
```

#### Step 3: Import to PostgreSQL

```bash
kubectl cp ./users.tsv udc-bot-dev/postgresql-pod:/tmp/users.tsv
kubectl exec -it deployment/postgresql -n udc-bot-dev -- psql -U udcbot -d udcbot -c "\COPY users FROM '/tmp/users.tsv' WITH (FORMAT text, HEADER true)"
```

Repeat for each table.

#### Step 4: Fix sequences

After import, auto-increment sequences will be out of sync:

```sql
SELECT setval('users_id_seq', (SELECT MAX(id) FROM users));
SELECT setval('casino_users_id_seq', (SELECT MAX(id) FROM casino_users));
SELECT setval('token_transactions_id_seq', (SELECT MAX(id) FROM token_transactions));
```

---

## Post-Migration Checklist

- [ ] Verify row counts match source
- [ ] Spot-check specific users (karma, level, tokens)
- [ ] Test `!profile` with a known user
- [ ] Test `thanks @someone` and verify karma increment
- [ ] Test casino commands
- [ ] Verify auto-increment sequences are correct

## Risk Mitigation

- **MySQL stays running** for 24-48h after cutover — instant rollback by redeploying the old bot image
- **MySQL backup** before migration: `mysqldump -u root -p udcbot > backup.sql`
- **PostgreSQL is idempotent**: Tables are created by the bot on startup; migration can be re-run with `TRUNCATE` first
- **Bot is down during migration** (scaled to 0) — no data inconsistency possible
- **Rollback plan**: Scale down new bot → scale up old bot → MySQL is still there with original data

## K8s Manifest Changes for Cutover

To run both databases temporarily, you need to **add** `postgresql.yaml` + PostgreSQL ExternalSecret to `k8s/prod/` **before** removing `mysql.yaml`. The PR should be split or the manifests applied in steps:

1. First ArgoCD sync: Add PostgreSQL manifests (MySQL stays)
2. Run migration Job
3. Second ArgoCD sync: Deploy new bot image + remove MySQL manifests

## Timeline

1. Deploy PostgreSQL in prod alongside MySQL (5 min)
2. Scale bot to 0 (1 min)
3. Backup MySQL (5 min)
4. Deploy pgloader Job (5 min)
5. Wait for completion (< 1 min for 54K rows)
6. Verify data (10 min)
7. Deploy new bot pointing to PostgreSQL (5 min)
8. Test bot commands (10 min)
9. Monitor for 24-48h
10. Remove MySQL (5 min)

**Active work: ~45 minutes** | **Total with monitoring: 24-48h**
