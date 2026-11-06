# #415 Web casino: deploy to the dev environment

Status: finished, waiting for the user's review.

## Goal

The web casino runs in the dev environment next to the dev bot, against the dev database and the dev Discord server, at its own address. The maintainers can try a casino change (multiplayer tables, #412, is next) with several accounts before it reaches production. Production does not change.

## Acceptance criteria

- AC1 WHEN **Deploy to Dev** runs (PR number or ref), THE SYSTEM SHALL build `udc-bot-dev` and `udc-casino-dev` from the same commit and commit both tags, to `k8s/dev/bot.yaml` and `k8s/dev/casino.yaml` on `master`; WHEN a `/build` comment is posted on a PR, it SHALL build both images and name both tags in its reply. Check: read the workflows (`actionlint` if installed, otherwise a YAML parse); the first real run, by the user.
- AC2 WHEN `k8s/dev/casino.yaml` is synced and its secrets exist, THE SYSTEM SHALL serve the casino at `https://casino.dev.udc.ovh`, and a member of the dev Discord server who logs in sees their name, avatar and the balance of the dev database. Check: manual, by the user.
- AC3 The dev casino touches nothing of production: no reference to `udc-bot-prod`, the prod hosts or the prod 1Password items. Check: `grep` on `k8s/dev/casino.yaml`; a diff against `k8s/prod/casino.yaml` showing only environment differences.
- AC4 Production deployment is unchanged: no change under `k8s/prod/`, `deploy-prod.yml` or `deploy-casino.yml`, and a push to `master` that touches only `k8s/dev/` triggers neither (a change to any workflow file runs Deploy Prod, as today: its filter does not ignore `.github/**`). Check: `git diff --stat`; reading the path filters.

## Design

- **Manifest `k8s/dev/casino.yaml`:** a copy of the prod one with the dev values: namespace `udc-bot-dev`, label `environment: dev`, image `ghcr.io/unity-developer-community/udc-casino-dev:<tag>` (started at `latest`, rewritten by the workflow), `Casino__PublicOrigin` and the Ingress host `casino.dev.udc.ovh` (next to `adminer.dev.bot.udc.ovh`), TLS secret `casino-dev-tls`. It reads the dev `bot-config` (dev guild, same defaults for `Casino`) and the dev `postgresql-credentials`, so the dev database and dev server are the only ones it sees. Same hardening as prod (non-root, read-only filesystem, keys PVC, one replica).
- **Secrets:** two ExternalSecrets in the same file, named like the prod ones so the Deployment env is identical: `casino-oauth` from the 1Password item `Discord App - Dev` (fields `Client ID`, `Client Secret`, the dev bot's Discord application) and `casino-announcements` from the item `Casino - Dev` (field `Announcement Webhook URL`, a webhook on the dev server's casino channel). The announcement setting stays required outside Development (`docs/decisions.md` D5), so the dev pod needs its webhook too.
- **Own image name:** `udc-casino-dev`, like `udc-bot-dev`. The dev workflow builds arbitrary PR commits, which must never overwrite a tag production could use. The tag rewrite in `deploy.yml` matches the exact image name, so `udc-casino-dev` and `udc-casino` cannot touch each other's manifest line.
- **Workflows:** **Deploy to Dev** gets a casino build and a casino deploy job next to the bot's, both from the resolved ref: one click gives a coherent dev, since the casino reuses the bot's code. The two deploys commit to the same branch; `deploy.yml` already rebases and retries. The "PR deployed" comment names both images. `pr-build.yml` builds both images and its reply gives the steps for `k8s/dev/bot.yaml` and `k8s/dev/casino.yaml`; `pr-deploy-instructions.yml` mentions the second file. Cost: every dev deploy builds a second image, in parallel with the first.
- **Argo CD:** the `udc-bot-dev` app is assumed to sync the whole `k8s/dev/` folder (the repository holds no Argo definition), so the new file is picked up by itself; the plan cannot check it.
- **Not in this plan:** the multiplayer tables (#412), a dev deploy on every push to `master` (the dev environment stays manual, as for the bot).

## Checklist

### #415.1 Dev manifest

- [x] Create `k8s/dev/casino.yaml` as in Design, from `k8s/prod/casino.yaml` (header comment adapted).
- [x] Verify AC3 (grep, diff against prod); `kubectl apply --dry-run=client -f` if `kubectl` is installed.

Visible after: nothing: the file is only synced once pushed, and the pod needs its secrets and DNS first.
Needs you: before the push, create the 1Password items `Discord App - Dev` and `Casino - Dev`, point the DNS record `casino.dev.udc.ovh` at the cluster, and add the redirect URL `https://casino.dev.udc.ovh/signin-discord` to the dev Discord application (at the end).

### #415.2 Dev workflows

- [x] `deploy-to-dev.yml`: add the casino build (`image_name: udc-casino-dev`, `dockerfile: CasinoWeb/Dockerfile`) and deploy (`manifest_path: k8s/dev/casino.yaml`, own commit message) jobs; the PR comment lists both images.
- [x] `pr-build.yml`: build both images; the reply names both tags and both manifests. `pr-deploy-instructions.yml`: mention `k8s/dev/casino.yaml` in the manifest-change steps.
- [x] Verify AC1 and AC4 by reading (keep the SHA-pinned actions and the 4-space style of `deploy-to-dev.yml`).

Decided during the run: `deploy-to-dev.yml` resolves a branch or tag `ref` to its commit SHA in the `resolve` job (`repos.getCommit`), so the bot and casino builds, which run as separate jobs, cannot pick up different commits if the branch moves between them (AC1 "same commit"). Alternative: pass the ref name through as before. Change: set `checkout_ref` back to `ref`.
Decided during the run: the success comment needs both deploys to succeed and the failure comment fires when any job failed (`contains(needs.*.result, 'failure')`), so a failed casino build is reported and a half-deployed dev is not announced as done. Alternative: test `deploy` only. Change: edit the two `if:` lines of `notify`.
Decided during the run: the casino deploy commits with its own message (`chore(k8s): deploy <ref or PR #n> casino to dev ({sha})`), from a new `casino_commit_message` output of `resolve`.
Decided during the run: both deploy jobs wait for both builds (`needs: [resolve, build, build-casino]`), so a failed build of either image commits no tag and dev never ends up with a new casino next to an old bot, or the reverse (the goal's "coherent dev"). Alternative: independent chains, which deploy whichever image built. Change: set `deploy` back to `needs: [resolve, build]` and `deploy-casino` to `needs: [resolve, build-casino]`.

Decided during the run: AC4 is reworded, not met by editing `deploy-prod.yml`: `deploy-prod.yml` does not ignore `.github/**`, so the push carrying this item redeploys the production bot once from `master` (existing behavior, no change in production). Alternative: add the three dev workflow files to its `paths-ignore`, which edits a production workflow. Change: add them and restore the original AC4 wording.

Visible after: the next **Deploy to Dev** run also deploys the casino.
Needs you: run **Deploy to Dev** once after the push and log in at the dev address with a dev-server account (AC2) (at the end).

### #415.3 Docs

- [x] `docs/deployment.md`: a dev paragraph in "The Web Casino" (address, the two 1Password items, redirect URL, image `udc-casino-dev`, deployed with **Deploy to Dev**), the casino row in "Updating the Bot", the table "Environment Differences".
- [x] `AGENTS.md`: the Deployment dev bullet names the casino dev image and manifest.

Visible after: nothing: documentation.
Needs you: nothing.

## Finishing

Delete this plan in the last commit, naming its path in the message. No `docs/decisions.md` entry expected. Close #415 and tick it in #410's roadmap when the user asks for repository operations.
