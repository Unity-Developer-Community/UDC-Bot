---
post_title: "Deployment Guide"
author1: "UDC-Bot Contributors"
post_slug: "deployment"
microsoft_alias: "N/A"
featured_image: ""
categories: []
tags: ["deployment", "docker", "kubernetes", "k3s"]
ai_note: "Generated with AI assistance"
summary: "Guide to deploying UDC-Bot from scratch using Kubernetes (k3s) or Docker Compose."
post_date: "2026-04-03"
---

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
- AMD64 (`x86_64`) build and Kubernetes nodes for the x64 Magick.NET native runtime
- The repository cloned locally

## Option A: Kubernetes (k3s/k8s)

This is the primary deployment method used for both the production and dev servers.

### What You Get

- Bot container with read-only modular configuration and direct secret-backed environment variables
- PostgreSQL 16 with persistent storage
- Adminer with TLS ingress (Traefik + Let's Encrypt)
- Optionally: automated PostgreSQL backups to S3
- Optionally: secrets managed via External Secrets Operator (1Password)

### Step 1: Set Up the Cluster

Install k3s (lightweight Kubernetes) on your server:

```bash
curl -sfL https://get.k3s.io | sh -
```

Verify the cluster is running:

```bash
kubectl get nodes
kubectl get nodes -o custom-columns=NAME:.metadata.name,ARCH:.status.nodeInfo.architecture
```

Every node eligible to run the bot must report `amd64`.

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
  --from-literal=flight-api-key='YOUR_KEY' \
  --from-literal=flight-api-secret='YOUR_SECRET' \
  --from-literal=airlab-api-key='YOUR_KEY'
```

> For automated secret management, see [Using 1Password with External Secrets](#using-1password-with-external-secrets) at the end of this section.

### Step 5: Deploy ConfigMaps

These contain the bot's non-secret domain configuration and content catalogs:

```bash
kubectl apply -f k8s/prod/bot-config.yaml
kubectl apply -f k8s/prod/bot-settings-config.yaml
```

**Before applying**, edit `bot-config.yaml` to set your Discord server's channel and role IDs in
`CoreSettings.json` and `FeatureSettings.json`. The ConfigMap must remain non-secret. The deployment
maps Kubernetes Secrets directly to the documented `UDCBOT_` environment variables.

Recruitment supports Observe, Advisory practice and gated Enforce. Checked-in deployment settings remain
disabled. Production lists the four new forum IDs; development uses zero placeholders
until its own four forums are supplied. To activate observation, supply a staff-only text
channel as `Recruitment:FeedChannelId`, verify all five channels belong to the configured
guild, and set `Enabled: true` and `Mode: Observe`. Retain disabled enforcement gates.
The bot needs View Channel and Read Message History in all four forums, and View Channel,
Read Message History and Send Messages in the staff feed. No tags or Guidelines assets are required for Observe.
Configuration requires a process restart; runtime component toggle/restart is supported.

For an Advisory staging rollout, use `Mode: Advisory` with the same configured forums/feed.
The bot additionally needs Manage Channels for forum topics/tags, Send Messages in Threads
and Embed Links for public messages, Attach Files for optional banners, and Manage Threads
for explicitly confirmed owner close/remove actions. Keep every enforcement gate disabled.
The image packages `Assets/recruitment/guidelines/*.md`; a native forum topic is used for
Guidelines, so no guidelines-post or tag IDs need configuring. Empty topics are initialized;
existing or manually changed topics require staff preview/adoption. See the
[Recruitment setup and staging checklist](recruitment.md) before activation.

For Enforce staging, follow the independent-gate checks in the recruitment guide before
production activation. Earlier practice/imported attempts are not retroactively enforced.

The first enabled start creates a missing, backup-free recruitment state file and
imports existing posts as unverified. Keep a single replica and preserve the state volume.
A missing primary with an existing backup, corrupt state, or changed forum mapping requires
operator recovery; the bot will not reset history. Stop the bot before restoring a known
good snapshot, preserve the damaged primary separately, and restart to validate the restored
guild/schema before any observations resume. The store also provides explicit validated
backup recovery as an operator API; file-level recovery requires the bot to be stopped.
Recruitment starts at SchemaVersion 1 with no earlier-schema upgrades or legacy settings
projection. Preserve known-good backups separately; normal writes rotate `.bak`.
See the [Observe notes](features.md#recruitment-observe) for recovery, coverage limits and
staff-feed retention. Live guild permissions and restart/delivery behavior still need a
staging check before production activation.

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

- a projected, read-only `/app/Settings` volume containing non-secret Core/Feature JSON and content ConfigMaps;
- direct secret-backed environment variables for the Discord token, database connection, weather key, and airport keys;
- a `wait-for-postgresql` init container that blocks until PostgreSQL is reachable.

Configuration precedence is legacy projection (when present), Core JSON, Feature JSON, then `UDCBOT_`
environment variable overrides. General configuration changes require a pod restart. The legacy flat file is a
read-only fallback that emits a startup warning and is never migrated automatically.

| Environment variable | Kubernetes source |
|---|---|
| `UDCBOT_DiscordConnection__Token` | Discord token Secret |
| `UDCBOT_Database__ConnectionString` | PostgreSQL password Secret plus non-secret connection fields |
| `UDCBOT_Weather__ApiKey` | API-key Secret |
| `UDCBOT_Airport__FlightApiKey` | API-key Secret |
| `UDCBOT_Airport__FlightApiSecret` | API-key Secret |
| `UDCBOT_Airport__AirLabsApiKey` | API-key Secret |

The registry writes administrator overrides to `SERVER/component-overrides.v1.json`. The deployment
uses one replica and a persistent `SERVER` volume. `reset` removes the override and restores the
configured default; if the file is corrupt, startup preserves a timestamped backup and safely falls
back to configuration. Runtime changes never edit the mounted ConfigMaps.

### Step 8 (Optional): Backups and Adminer

**Database backups:** You should set up regular PostgreSQL backups using your preferred method.
Common options include:

- `pg_dump` via a cron job
- Volume snapshots (if your storage provider supports it)
- Cloud-managed backup (AWS RDS, GCP Cloud SQL, etc.)

The repository includes an S3-based backup manifest — see
[Using S3 Backups](#using-s3-backups) below if you want to use it.

**Adminer** (database UI):

```bash
kubectl apply -f k8s/prod/adminer.yaml
```

Adminer is exposed via Traefik ingress with TLS. Edit the ingress host to match your domain.
Access is restricted by IP allowlist.

### Verify

```bash
kubectl -n udc-bot-prod get pods
kubectl -n udc-bot-prod logs deployment/udc-bot
```

The Docker build itself runs `./DiscordBot --render-smoke` in the final runtime stage. A missing
native library, skin asset, or bundled font therefore fails the image build before rollout.

### Updating the Bot

Build and push a new image, then update the image tag in `bot.yaml`:

```bash
docker build -t ghcr.io/unity-developer-community/udc-bot:NEW_TAG .
docker push ghcr.io/unity-developer-community/udc-bot:NEW_TAG
```

Edit `bot.yaml` to use the new tag, then re-apply:

```bash
kubectl apply -f k8s/prod/bot.yaml
```

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
| `postgresql-credentials` | PostgreSQL application password |
| `discord-bot-token` | Discord bot token |
| `bot-api-keys` | Weather, Flight, and AirLabs API keys |
| `postgresql-backup-credentials` | AWS S3 keys (only needed for S3 backups) |

Secrets are refreshed every hour automatically.

#### Using S3 Backups

The repository includes a backup deployment using `eeshugerman/postgres-backup-s3` to run
`pg_dump` and upload the result to an S3 bucket daily.

**Prerequisites:**

- An S3 bucket (or S3-compatible storage like MinIO)
- AWS credentials with write access to the bucket

**Step 1: Create the backup credentials secret**

```bash
kubectl -n udc-bot-prod create secret generic postgresql-backup-credentials \
  --from-literal=AWS_ACCESS_KEY_ID='YOUR_AWS_KEY' \
  --from-literal=AWS_SECRET_ACCESS_KEY='YOUR_AWS_SECRET'
```

**Step 2: Deploy the backup service**

Review `k8s/prod/postgresql-backup.yaml` and update the S3 bucket name and region if needed, then:

```bash
kubectl apply -f k8s/prod/postgresql-backup.yaml
```

The backup runs every 24 hours (1440 minutes) and stores dumps in the configured S3 bucket.

---

## Option B: Docker Compose

Simpler setup for local development or single-server deployment.

### What You Get

- Bot container built from source
- PostgreSQL with persistent volume
- Adminer (port 8080)

### Step 1: Configure Settings

```bash
cp DiscordBot/Settings/CoreSettings.example.json DiscordBot/Settings/CoreSettings.json
cp DiscordBot/Settings/FeatureSettings.example.json DiscordBot/Settings/FeatureSettings.json
```

Edit the modular settings:

- Configure the token, connection string, guild, channel, and role values in `CoreSettings.json`.
- Configure feature values and optional weather/airport keys in `FeatureSettings.json`.
- The checked-in Compose service still accepts `UDCBOT_` overrides so it can supply the container-only database host and avoid rewriting the local JSON files.

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

To validate the exact built image without Discord or the database:

```bash
docker run --rm YOUR_IMAGE --render-smoke
docker run --rm --memory=512m --memory-swap=512m YOUR_IMAGE --render-stress 250 4
```

The smoke command checks the native Magick load, bundled fonts, 500x200 PNG output, and alpha
channel. The stress command exercises repeated concurrent renders under the production memory
ceiling. Both return a non-zero exit code on failure.

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
| Bot image | Pinned commit SHA | `latest` | Built from source |
| CPU request | 100m | 50m | Unlimited |
| Memory limit | 512Mi | 512Mi | Unlimited |
| PostgreSQL storage | PVC | PVC | Docker volume |
| Backups | User's choice | User's choice | Manual |
| Secrets | Manual or External Secrets | Manual or External Secrets | Hardcoded / `.env` file |
| Adminer | TLS ingress + IP allowlist | TLS ingress + IP allowlist | `localhost:8080` |

---
