# UDC-Bot Deployment Guide

## Overview

UDC-Bot can be deployed in two ways:

| Method | Use Case | Complexity |
|--------|----------|------------|
| **Kubernetes (k3s/k8s)** | Production and dev server | Medium-High |
| **Docker Compose** | Local development or simple single-server deployment | Low |

Both methods run the same Docker image and use PostgreSQL as the database.

## Table of Contents

- [UDC-Bot Deployment Guide](#udc-bot-deployment-guide)
  - [Overview](#overview)
  - [Table of Contents](#table-of-contents)
  - [Prerequisites](#prerequisites)
  - [Option A: Kubernetes (k3s/k8s)](#option-a-kubernetes-k3sk8s)
    - [What You Get](#what-you-get)
    - [Step 1: Set Up the Cluster](#step-1-set-up-the-cluster)
    - [Step 2: Install cert-manager](#step-2-install-cert-manager)
    - [Step 3: Create the Namespace](#step-3-create-the-namespace)
    - [Step 4: Create Secrets](#step-4-create-secrets)
    - [Step 5: Deploy ConfigMaps](#step-5-deploy-configmaps)
    - [Step 6: Deploy PostgreSQL](#step-6-deploy-postgresql)
    - [Step 7: Deploy the Bot](#step-7-deploy-the-bot)
    - [Step 8 (Optional): Backups and Adminer](#step-8-optional-backups-and-adminer)
    - [Verify](#verify)
    - [Updating the Bot](#updating-the-bot)
    - [The Web Casino](#the-web-casino)
    - [Advanced: Optional Integrations](#advanced-optional-integrations)
      - [Using 1Password with External Secrets](#using-1password-with-external-secrets)
      - [Using S3 Backups](#using-s3-backups)
  - [Option B: Docker Compose](#option-b-docker-compose)
    - [What You Get](#what-you-get-1)
    - [Step 1: Configure Settings](#step-1-configure-settings)
    - [Step 2: Start Everything](#step-2-start-everything)
    - [Step 3: Verify](#step-3-verify)
    - [Production Hardening (if using Compose for production)](#production-hardening-if-using-compose-for-production)
  - [Environment Differences](#environment-differences)

## Prerequisites

Regardless of deployment method, you need:

- A Discord bot token from the [Discord Developer Portal](https://discord.com/developers/applications)
- A server or machine with Docker installed
- The repository cloned locally

## Option A: Kubernetes (k3s/k8s)

This is the primary deployment method used for both the production and dev servers.

### What You Get

- Bot container with init-container config rendering
- PostgreSQL 16 with persistent storage (5 Gi)
- Adminer with TLS ingress (Traefik + Let's Encrypt)
- Optionally: automated daily PostgreSQL backups to S3
- Optionally: secrets managed via External Secrets Operator (1Password)

### Step 1: Set Up the Cluster

Install k3s (lightweight Kubernetes) on your server:

```bash
curl -sfL https://get.k3s.io | sh -
```

Verify the cluster is running:

```bash
kubectl get nodes
```

### Step 2: Install cert-manager

cert-manager handles TLS certificates for ingress (Adminer):

```bash
kubectl apply -f https://github.com/cert-manager/cert-manager/releases/latest/download/cert-manager.yaml
```

### Step 3: Create the Namespace

```bash
kubectl apply -f k8s/prod/namespace.yaml
```

This creates the `udc-bot-prod` namespace (or `udc-bot-dev` for the dev environment).

### Step 4: Create Secrets

Create Kubernetes secrets for the bot's credentials:

```bash
kubectl -n udc-bot-prod create secret generic postgresql-credentials \
  --from-literal=password='YOUR_POSTGRESQL_PASSWORD'

kubectl -n udc-bot-prod create secret generic discord-bot-token \
  --from-literal=identifiant='YOUR_DISCORD_BOT_TOKEN'

kubectl -n udc-bot-prod create secret generic bot-api-keys \
  --from-literal=weather-api-key='YOUR_KEY' \
  --from-literal=ipgeo-api-key='YOUR_KEY' \
  --from-literal=flight-api-key='YOUR_KEY' \
  --from-literal=flight-api-secret='YOUR_SECRET' \
  --from-literal=airlab-api-key='YOUR_KEY'
```

> For automated secret management, see [Using 1Password with External Secrets](#using-1password-with-external-secrets) at the end of this section.

### Step 5: Deploy ConfigMaps

These contain the bot's configuration templates and settings:

```bash
kubectl apply -f k8s/prod/bot-config.yaml
kubectl apply -f k8s/prod/bot-settings-config.yaml
```

**Before applying**, edit `bot-config.yaml` to set your Discord server's channel and role IDs
in the `Settings.json` template. API keys and tokens are injected from secrets automatically
via the init container's `envsubst`.

### Step 6: Deploy PostgreSQL

```bash
kubectl apply -f k8s/prod/postgresql.yaml
```

Wait for PostgreSQL to be ready:

```bash
kubectl -n udc-bot-prod wait --for=condition=ready pod -l app.kubernetes.io/name=postgresql --timeout=120s
```

### Step 7: Deploy the Bot

```bash
kubectl apply -f k8s/prod/bot.yaml
```

The bot deployment includes:

- An init container (`render-config`) that renders `Settings.json` from the template by
  substituting environment variables from secrets
- An init container (`wait-for-postgresql`) that blocks until PostgreSQL is reachable
- The main bot container with the rendered config mounted at `/app/Settings`

### Step 8 (Optional): Backups and Adminer

**Database backups:** You should set up regular PostgreSQL backups using your preferred method.
Common options include:

- `pg_dump` via a cron job
- Volume snapshots (if your storage provider supports it)
- Dedicated backup tools like [eeshugerman/postgres-backup-s3](https://github.com/eeshugerman/postgres-backup-s3)
- Cloud-managed backup (AWS RDS, GCP Cloud SQL, etc.)

The repository includes an S3-based backup manifest — see
[Using S3 Backups](#using-s3-backups) below if you want to use it.

**Adminer** (database UI):

```bash
kubectl apply -f k8s/prod/adminer.yaml
```

Adminer is exposed via Traefik ingress with TLS at `adminer.bot.udc.ovh` (prod)
or `adminer.dev.bot.udc.ovh` (dev). Edit the ingress host to match your domain.
Access is restricted by IP allowlist.

The manifest sets no replica count, so Adminer can be switched off when unused:
`kubectl scale deployment adminer -n udc-bot-prod --replicas=0` (`1` to switch it
back on, `-n udc-bot-dev` for dev), or the Scale action on the `adminer` Deployment
in the Argo CD UI. Argo CD leaves the count as set. The on/off state lives only in
the cluster: a recreated Deployment starts with Adminer on.

### Verify

```bash
kubectl -n udc-bot-prod get pods
kubectl -n udc-bot-prod logs deployment/udc-bot
```

### Updating the Bot

The UDC servers update through CI and [Argo CD](https://argo-cd.readthedocs.io/), which syncs the cluster with the manifests in `k8s/`:

- **Prod:** every push to `master` (except changes limited to `k8s/`, `docs/`, `CasinoWeb/`, `CasinoWeb.Tests/` or Markdown) builds `ghcr.io/unity-developer-community/udc-bot:<short-sha>`, commits that tag to `k8s/prod/bot.yaml`, and Argo CD rolls it out.
- **Prod web casino:** a push to `master` that changes `CasinoWeb/**`, `DiscordBot/**` or `NuGet.config` (Markdown aside) builds `ghcr.io/unity-developer-community/udc-casino:<short-sha>` and commits that tag to `k8s/prod/casino.yaml` (see [The Web Casino](#the-web-casino)).
- **Dev:** the **Deploy to Dev** workflow (run manually with a PR number or a ref) builds the bot and the web casino from the same commit, as `udc-bot-dev:<short-sha>` and `udc-casino-dev:<short-sha>`, and commits both tags to `k8s/dev/bot.yaml` and `k8s/dev/casino.yaml` on `master`. Both deploys wait for both builds, so a failed build commits neither tag. A `/build` comment on a PR only builds and pushes both images; to run them with the PR's manifest changes, set the two tags in `k8s/dev/bot.yaml` and `k8s/dev/casino.yaml` on the PR branch and point the `udc-bot-dev` Argo CD app at that branch (the comments posted on the PR give these steps).
- **Workflow changes:** a push to `master` that changes any file under `.github/` runs **Deploy Prod**, because its filter does not ignore `.github/**`; the run redeploys the production bot from `master` and changes nothing in production.

Without Argo CD, build and push an image yourself, set its tag in `bot.yaml`, and re-apply:

```bash
docker build -t ghcr.io/unity-developer-community/udc-bot:NEW_TAG .
docker push ghcr.io/unity-developer-community/udc-bot:NEW_TAG
kubectl apply -f k8s/prod/bot.yaml
```

### The Web Casino

`k8s/prod/casino.yaml` runs the web casino (`CasinoWeb/`) in the same namespace as the bot, as its own Deployment: a casino crash or deploy does not touch the bot, and the casino never holds the bot token. It plays against the bot's PostgreSQL and reads the bot's `Settings.json` (the `bot-config` ConfigMap) for `GuildId`, `DbConnectionString` and `Casino`; its `render-config` init container fills in only `${DB_PASSWORD}`.

- **Image and rollout:** a push to `master` that changes `CasinoWeb/**`, `DiscordBot/**` or `NuGet.config` (Markdown aside) runs the **Deploy Casino** workflow: it builds `CasinoWeb/Dockerfile` into `ghcr.io/unity-developer-community/udc-casino:<short-sha>`, commits the tag to `k8s/prod/casino.yaml`, and Argo CD syncs it. **Deploy Prod** ignores `CasinoWeb/**`, so a casino-only change never rebuilds the bot. A change under `DiscordBot/**` rebuilds both. Both workflows commit to `master`: the push step rebases and retries when the other one wins.
- **Address:** `https://casino.udc.ovh`, set in the Ingress host and in `Casino__PublicOrigin`. The OAuth redirect URLs, the cookie and the WebSocket Origin check all derive from `Casino__PublicOrigin`. Point the DNS record at the cluster, as for Adminer.
- **Discord application:** in the bot's application (developer portal, OAuth2), add the redirect URL `https://casino.udc.ovh/signin-discord`.
- **Secret:** the ExternalSecret `casino-oauth` reads the fields `Client ID` and `Client Secret` of the 1Password item `Discord App` in the UDC vault (the bot's whole Discord application).
- **Big-win announcements:** the casino posts web jackpots and rare slots wins through a Discord webhook, so it still never holds the bot token. Create the webhook in Discord on the channel where members play `/casino` (its name and avatar are the author members see) and store its URL in the field `Announcement Webhook URL` of the 1Password item `Casino` (UDC vault, the item for everything casino-specific). The ExternalSecret `casino-announcements` feeds it to `Casino__AnnouncementWebhookUrl`. The setting is required at start outside Development: the pod fails to start, with a message naming the setting, when the field is empty or not a `https://` Discord webhook URL. The URL carries the webhook's token: never paste it in an issue or a log.
- **First rollout:** the manifest starts at `udc-casino:latest`, which exists once the first Deploy Casino run has pushed it, so the pod may show `ImagePullBackOff` until that run has committed the real tag.
- **Keys volume:** the PVC `casino-keys` holds the Data Protection keys (unencrypted on disk, private to the pod), so a deploy keeps everyone logged in. Losing it only logs players out.
- **One replica, recreated on deploy:** open Blackjack hands, poker tables and slots hold offers live in the process. A graceful stop gives back every open Blackjack bet and every token committed to an unfinished poker hand, and closes the tables; a crash does not, and the log has a `WebBlackjack: reserved …` or `WebPoker: took …` line per bet and a `settled` or `gave back` line per finished hand, so a bet with neither can be credited with `/admin casino tokens-add`. `Casino__AllowAiSeats` is set only in `k8s/dev/casino.yaml`.
- **Verify:** `kubectl -n udc-bot-prod get pods -l app.kubernetes.io/name=udc-casino`, `kubectl -n udc-bot-prod logs deployment/udc-casino`, then log in at the address with a guild member account.

#### Dev environment

`k8s/dev/casino.yaml` runs the same casino in the `udc-bot-dev` namespace, next to the dev bot. It reads the dev `bot-config` and the dev `postgresql-credentials`, so it sees only the dev database and the dev Discord server; nothing in it refers to production. It differs from the production manifest in these environment values:

- **Address:** `https://casino.dev.udc.ovh` (Ingress host, TLS secret `casino-dev-tls`, and `Casino__PublicOrigin`). Point the DNS record at the cluster, as for `adminer.dev.bot.udc.ovh`.
- **Discord application:** in the dev bot's application, add the redirect URL `https://casino.dev.udc.ovh/signin-discord`.
- **Secrets:** `casino-oauth` reads the fields `Client ID` and `Client Secret` of the 1Password item `Discord App - Dev`; `casino-announcements` reads the field `Announcement Webhook URL` of the item `Casino - Dev`, a webhook on the dev server's casino channel (the setting is required outside Development, so the dev pod needs its webhook too). Both items are in the UDC vault.
- **Image and rollout:** `ghcr.io/unity-developer-community/udc-casino-dev:<short-sha>`, built and committed to the manifest by **Deploy to Dev** together with the dev bot. No push to `master` deploys the dev casino. The manifest starts at `:latest`, so the pod may show `ImagePullBackOff` until the first **Deploy to Dev** run has committed the real tag.
- **Verify:** `kubectl -n udc-bot-dev get pods -l app.kubernetes.io/name=udc-casino`, then log in at the address with a dev-server account.

### Advanced: Optional Integrations

#### Using 1Password with External Secrets

The repository includes manifests for managing secrets via
[External Secrets Operator](https://external-secrets.io/) with 1Password as the backend.
This automates secret rotation and avoids storing credentials in manifests.

**Step 1: Install the External Secrets Operator**

```bash
helm repo add external-secrets https://charts.external-secrets.io
helm install external-secrets external-secrets/external-secrets \
  -n external-secrets --create-namespace
```

**Step 2: Configure the 1Password Connect server**

Deploy 1Password Connect in your cluster.
See the [1Password Connect documentation](https://developer.1password.com/docs/connect/)
for setup instructions. You need:

- A 1Password Connect server running in the cluster
- A `1password-credentials.json` token
- A `ClusterSecretStore` resource pointing to your 1Password Connect instance

Example `ClusterSecretStore`:

```yaml
apiVersion: external-secrets.io/v1beta1
kind: ClusterSecretStore
metadata:
  name: onepassword
spec:
  provider:
    onepassword:
      connectHost: http://onepassword-connect.onepassword.svc.cluster.local:8080
      vaults:
        my-vault: 1
      auth:
        secretRef:
          connectTokenSecretRef:
            name: onepassword-token
            namespace: onepassword
            key: token
```

**Step 3: Apply the External Secrets manifests**

```bash
kubectl apply -f k8s/prod/external-secrets.yaml
```

This creates the following secrets automatically from 1Password:

| Secret | 1Password Item |
|--------|----------------|
| `postgresql-credentials` | PostgreSQL password |
| `discord-bot-token` | Discord bot token |
| `bot-api-keys` | Weather, IP Geo, Flight, Airlab API keys |
| `casino-oauth` | Discord OAuth client ID and secret of the web casino (declared in `k8s/prod/casino.yaml`) |
| `casino-announcements` | Discord webhook URL of the casino channel, for web big-win announcements (declared in `k8s/prod/casino.yaml`) |
| `postgresql-backup-credentials` | AWS S3 keys (only needed for S3 backups) |

Secrets are refreshed every hour automatically.

#### Using S3 Backups

The repository includes a backup `CronJob` that dumps PostgreSQL to an S3 bucket daily. An init
container runs `pg_dump` into a scratch volume and the main container uploads the file with the
AWS CLI, so a failure in either step fails the Job.

**Prerequisites:**

- An S3 bucket (or S3-compatible storage like MinIO)
- AWS credentials with write access to the bucket

**Step 1: Create the backup credentials secret**

```bash
kubectl -n udc-bot-prod create secret generic postgresql-backup-credentials \
  --from-literal=AWS_ACCESS_KEY_ID='YOUR_AWS_KEY' \
  --from-literal=AWS_SECRET_ACCESS_KEY='YOUR_AWS_SECRET'
```

**Step 2: Deploy the backup job**

Review `k8s/prod/postgresql-backup.yaml` and update the S3 bucket name and region if needed, then:

```bash
kubectl apply -f k8s/prod/postgresql-backup.yaml
```

The backup runs daily at 00:00 UTC and stores dumps as `udcbot_<timestamp>.dump` under the
environment prefix of the bucket. Retention is not handled by the job: configure an S3 lifecycle
rule on the bucket to expire old dumps.

The manifest also contains a `PrometheusRule` that fires `UdcBotBackupFailed` (the latest run did
not succeed) and `UdcBotBackupStale` (no success for 36 hours). It requires the Prometheus
Operator CRDs and a rule selector matching the `release: monitoring` label; delete that document
from the file if you do not run them.

To test a backup immediately: `kubectl -n udc-bot-prod create job --from=cronjob/postgresql-backup backup-test`.

---

## Option B: Docker Compose

Simpler setup for local development or single-server deployment.

### What You Get

- Bot container built from source
- PostgreSQL with persistent volume
- Adminer (port 8080)

### Step 1: Configure Settings

```bash
cp DiscordBot/Settings/Settings.example.json DiscordBot/Settings/Settings.json
```

Edit `Settings.json`:

- Set `Token` to your Discord bot token
- Set `DbConnectionString` to `Host=db;Port=5432;Database=udcbot;Username=udcbot;Password=123456789`
- Configure channel and role IDs for your Discord server
- Set API keys for Weather, Flight, etc.

### Step 2: Start Everything

```bash
docker-compose up --build
```

Or start only the database (and run the bot from your IDE):

```bash
docker-compose up db
```

### Step 3: Verify

- Bot logs appear in the terminal
- Adminer is available at `http://localhost:8080`
- Database is on `localhost:5432`

### Production Hardening (if using Compose for production)

The default `docker-compose.yml` uses hardcoded credentials and is meant for local dev.
For a production deployment with Docker Compose:

1. **Change all default passwords** in the compose file
2. **Use environment variables or a `.env` file** instead of hardcoded values:

```yaml
environment:
  POSTGRES_PASSWORD: ${POSTGRES_PASSWORD}
```

1. **Restrict Adminer access** — either remove it or bind to localhost only:

```yaml
ports:
  - "127.0.0.1:8080:80"
```

1. **Set up database backups** — use `pg_dump` via cron, volume snapshots, or a dedicated
   backup tool. See [Using S3 Backups](#using-s3-backups) for an example.

2. **Pin image versions** — use specific tags instead of `latest`:

```yaml
db:
  image: postgres:16
adminer:
  image: adminer:4
```

---

## Environment Differences

| Setting | Production (k8s) | Dev Server (k8s) | Local (Compose) |
|---------|------------------|-------------------|-----------------|
| Namespace | `udc-bot-prod` | `udc-bot-dev` | N/A |
| Bot image | Commit SHA of `master` | Commit SHA of the deployed PR or ref | Built from source |
| Casino image | `udc-casino`, commit SHA of `master` | `udc-casino-dev`, commit SHA of the deployed PR or ref | Built from source |
| Casino address | `https://casino.udc.ovh` | `https://casino.dev.udc.ovh` | `localhost:5080` |
| Casino secrets | 1Password items `Discord App` and `Casino` | 1Password items `Discord App - Dev` and `Casino - Dev` | `dotnet user-secrets` |
| CPU request | 100m | 50m | Unlimited |
| Memory limit | 512Mi | 512Mi | Unlimited |
| PostgreSQL storage | 5 Gi PVC | 5 Gi PVC | Docker volume |
| Backups | Daily CronJob to S3 | Daily CronJob to S3 | Manual |
| Secrets | Manual or External Secrets | Manual or External Secrets | Hardcoded / `.env` file |
| Adminer | TLS ingress + IP allowlist | TLS ingress + IP allowlist | `localhost:8080` |

---
