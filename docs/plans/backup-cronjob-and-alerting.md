# Backup CronJob and failure alert rule

## Goal

PostgreSQL backups run as a Kubernetes CronJob, and a failed or missing backup is expressed as a Prometheus alert rule that the cluster's alerting can route.

## Acceptance criteria

- WHEN the backup schedule fires, THE SYSTEM SHALL run `pg_dump` for `udcbot` and upload the dump to `s3://udc-bot-postgresql-backups/<env>/` without a permission error.
- IF `pg_dump` or the upload fails, THEN THE SYSTEM SHALL mark the Job failed.
- WHEN a backup Job fails, THE SYSTEM SHALL fire the `UdcBotBackupFailed` alert.
- WHEN the last success is older than 36 hours, THE SYSTEM SHALL fire the `UdcBotBackupStale` alert.
- WHEN the CronJob has recorded no success for 36 hours, THE SYSTEM SHALL fire the `UdcBotBackupMissing` alert (never scheduled, suspended or deleted).

## Design

- Replace the `postgresql-backup` Deployment (`k8s/{dev,prod}/postgresql-backup.yaml`) with a `CronJob` using two official images, so no custom image is built or maintained:
  - Init container `postgres:16-alpine` runs `pg_dump -Fc` into an `emptyDir` (size-limited). A failed dump fails the pod, hence the Job, before anything is uploaded.
  - Main container `amazon/aws-cli` uploads the file with `aws s3 cp` to `s3://udc-bot-postgresql-backups/<env>/udcbot_<timestamp>.dump`.
  - Both run non-root with a read-only root filesystem and dropped capabilities; the `emptyDir` is the only writable path.
  - `concurrencyPolicy: Forbid`, `backoffLimit: 1`, `successfulJobsHistoryLimit: 3`, `failedJobsHistoryLimit: 3`.
  - Image tags pinned by digest. Credentials unchanged: secrets `postgresql-credentials` and `postgresql-backup-credentials`.
  - Retention is an S3 lifecycle rule on the bucket, not part of the job.
- Add a `PrometheusRule` per environment (same file as the CronJob), labelled `release: monitoring` so the monitoring stack's rule selector picks it up:
  - `UdcBotBackupFailed`: the latest scheduled run has no success at or after it, for 30 minutes (based on `kube_cronjob_status_last_schedule_time` vs `..._last_successful_time`, so it resolves on the next success; a `kube_job_status_failed` rule would keep firing until the failed Job is pruned)
  - `UdcBotBackupStale`: `time() - kube_cronjob_status_last_successful_time{namespace="udc-bot-<env>", cronjob="postgresql-backup"} > 36 * 3600`
- Update `docs/deployment.md` (backup sections) to describe the CronJob.

## Edge cases and failure modes

- A failed upload leaves the dump only in the `emptyDir`, which is discarded with the pod; the Job is failed and alerts.
- The rule is ignored if its label does not match the monitoring stack's selector: confirm it is loaded.
- Dump larger than the `emptyDir` limit: the pod is evicted and the Job fails visibly.

## Checklist

Status: manifests, rules and docs written and peer-reviewed; remaining items are the live checks in dev (need cluster access and a pushed commit).

- [x] Baseline: YAML parse check (no cluster reachable for `kubectl --dry-run`) on `k8s/dev` and `k8s/prod` (no automated tests cover manifests)
- [x] Resolve and pin image digests
- [x] CronJob manifests (dev, prod); remove the Deployments
- [x] PrometheusRule manifests (dev, prod)
- [x] `docs/deployment.md` backup sections
- [ ] Manual run in dev: `kubectl create job --from=cronjob/postgresql-backup test`, object appears in S3
- [ ] Failure test in dev: break the S3 key, the Job fails, the alert fires; restore
- [x] Peer review
