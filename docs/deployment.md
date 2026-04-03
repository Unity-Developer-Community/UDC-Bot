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

Both methods run the same Docker image and use MySQL as the database.

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
    - [Step 6: Deploy MySQL](#step-6-deploy-mysql)
    - [Step 7: Deploy the Bot](#step-7-deploy-the-bot)
    - [Step 8 (Optional): Backups and phpMyAdmin](#step-8-optional-backups-and-phpmyadmin)
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
- The repository cloned locally

## Option A: Kubernetes (k3s/k8s)

This is the primary deployment method used for both the production and dev servers.

### What You Get

- Bot container with init-container config rendering
- MySQL 8.0 with persistent storage (5 Gi)
- phpMyAdmin with TLS ingress (Traefik + Let's Encrypt)
- Optionally: automated daily MySQL backups to S3
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

cert-manager handles TLS certificates for ingress (phpMyAdmin):

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
kubectl -n udc-bot-prod create secret generic mysql-credentials \
  --from-literal=password='YOUR_MYSQL_ROOT_PASSWORD'

kubectl -n udc-bot-prod create secret generic mysql-user-credentials \
  --from-literal=password='YOUR_MYSQL_USER_PASSWORD'

kubectl -n udc-bot-prod create secret generic discord-bot-token \
  --from-literal=token='YOUR_DISCORD_BOT_TOKEN'

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

### Step 6: Deploy MySQL

```bash
kubectl apply -f k8s/prod/mysql.yaml
```

Wait for MySQL to be ready:

```bash
kubectl -n udc-bot-prod wait --for=condition=ready pod -l app.kubernetes.io/name=mysql --timeout=120s
```

### Step 7: Deploy the Bot

```bash
kubectl apply -f k8s/prod/bot.yaml
```

The bot deployment includes:

- An init container (`render-config`) that renders `Settings.json` from the template by
  substituting environment variables from secrets
- An init container (`wait-for-mysql`) that blocks until MySQL is reachable
- The main bot container with the rendered config mounted at `/app/Settings`

### Step 8 (Optional): Backups and phpMyAdmin

**Database backups:** You should set up regular MySQL backups using your preferred method.
Common options include:

- `mysqldump` via a cron job
- Volume snapshots (if your storage provider supports it)
- Dedicated backup tools like [databack/mysql-backup](https://github.com/databacker/mysql-backup)
- Cloud-managed backup (AWS RDS, GCP Cloud SQL, etc.)

The repository includes an S3-based backup manifest — see
[Using S3 Backups](#using-s3-backups) below if you want to use it.

**phpMyAdmin** (database UI):

```bash
kubectl apply -f k8s/prod/phpmyadmin.yaml
```

phpMyAdmin is exposed via Traefik ingress with TLS at `phpmyadmin.bot.udc.ovh` (prod)
or `phpmyadmin.dev.bot.udc.ovh` (dev). Edit the ingress host to match your domain.
Access is restricted by IP allowlist.

### Verify

```bash
kubectl -n udc-bot-prod get pods
kubectl -n udc-bot-prod logs deployment/udc-bot
```

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
| `mysql-credentials` | MySQL root password |
| `mysql-user-credentials` | MySQL user password |
| `discord-bot-token` | Discord bot token |
| `bot-api-keys` | Weather, IP Geo, Flight, Airlab API keys |
| `mysql-backup-credentials` | AWS S3 keys (only needed for S3 backups) |

Secrets are refreshed every hour automatically.

#### Using S3 Backups

The repository includes a backup deployment using
[databack/mysql-backup](https://github.com/databacker/mysql-backup) that dumps MySQL
to an S3 bucket daily.

**Prerequisites:**

- An S3 bucket (or S3-compatible storage like MinIO)
- AWS credentials with write access to the bucket

**Step 1: Create the backup credentials secret**

```bash
kubectl -n udc-bot-prod create secret generic mysql-backup-credentials \
  --from-literal=access-key-id='YOUR_AWS_KEY' \
  --from-literal=secret-access-key='YOUR_AWS_SECRET'
```

**Step 2: Deploy the backup service**

Review `k8s/prod/mysql-backup.yaml` and update the S3 bucket name and region if needed, then:

```bash
kubectl apply -f k8s/prod/mysql-backup.yaml
```

The backup runs every 24 hours (1440 minutes) and stores dumps in the configured S3 bucket.

---

## Option B: Docker Compose

Simpler setup for local development or single-server deployment.

### What You Get

- Bot container built from source
- MySQL with persistent volume
- phpMyAdmin (port 8080)

### Step 1: Configure Settings

```bash
cp DiscordBot/Settings/Settings.example.json DiscordBot/Settings/Settings.json
```

Edit `Settings.json`:

- Set `Token` to your Discord bot token
- Set `DbConnectionString` to `Server=db;Database=udcbot;Uid=udcbot;Pwd=123456789;`
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
- phpMyAdmin is available at `http://localhost:8080`
- Database is on `localhost:3306`

### Production Hardening (if using Compose for production)

The default `docker-compose.yml` uses hardcoded credentials and is meant for local dev.
For a production deployment with Docker Compose:

1. **Change all default passwords** in the compose file
2. **Use environment variables or a `.env` file** instead of hardcoded values:

```yaml
environment:
  MYSQL_ROOT_PASSWORD: ${MYSQL_ROOT_PASSWORD}
  MYSQL_PASSWORD: ${MYSQL_PASSWORD}
```

1. **Restrict phpMyAdmin access** — either remove it or bind to localhost only:

```yaml
ports:
  - "127.0.0.1:8080:80"
```

1. **Set up database backups** — use `mysqldump` via cron, volume snapshots, or a dedicated
   backup tool. See [Using S3 Backups](#using-s3-backups) for an example.

2. **Pin image versions** — use specific tags instead of `latest`:

```yaml
db:
  image: mysql:8.0
phpmyadmin:
  image: phpmyadmin:5.2.3
```

---

## Environment Differences

| Setting | Production (k8s) | Dev Server (k8s) | Local (Compose) |
|---------|------------------|-------------------|-----------------|
| Namespace | `udc-bot-prod` | `udc-bot-dev` | N/A |
| Bot image | Pinned commit SHA | `latest` | Built from source |
| CPU request | 100m | 50m | Unlimited |
| Memory limit | 512Mi | 512Mi | Unlimited |
| MySQL storage | 5 Gi PVC | 5 Gi PVC | Docker volume |
| Backups | User's choice | User's choice | Manual |
| Secrets | Manual or External Secrets | Manual or External Secrets | Hardcoded / `.env` file |
| phpMyAdmin | TLS ingress + IP allowlist | TLS ingress + IP allowlist | `localhost:8080` |

---
