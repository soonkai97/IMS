# Deploy kit security retest: follow-ups and accepted risks

Source: a security retest of the merged deploy kit (PR #243: Docker Compose, Caddy, `deploy.sh`, `smoke.sh`, `backup.sh`, the Release and Deploy workflows). No new design flaw; hardening items, fixed in one pull request, and two accepted dependency risks. The open go-live gates are in `docs/architecture/deployment.md` section 6.

| Id | Finding | Status | Where |
| -- | ------- | ------ | ----- |
| D-1 | The API defaulted its database login to `sa` when `IMS_DB_USER` was unset | Fixed: `IMS_DB_USER` is mandatory in compose; `deploy.sh` refuses `sa` unless `--first-migration` is passed | `deploy/compose.prod.yml`, `deploy/scripts/deploy.sh` |
| D-2 | `smoke.sh` wrote the login response headers (with the refresh cookie) to a predictable file and removed it only on success | Fixed: `mktemp` (mode 600) and an `EXIT` trap | `deploy/scripts/smoke.sh` |
| D-3 | A failing `backup.sh ship` leaves `.bak` and `.trn` files that `prune` never deletes (it deletes only shipped files), so the disk fills and the database stops; nothing told anyone | Fixed: `ship` exits non-zero when a file could not be shipped; systemd `OnFailure=` alert; hourly `backup.sh check` for disk use and unshipped backups; optional alert and heartbeat hooks (commands on the VM, no secret in the repository) | `deploy/scripts/backup.sh`, `deploy/scripts/systemd/` |
| D-4 | Third-party actions in `release.yml` and `deploy.yml` were pinned to major tags; the `latest` image tag was published although compose uses `sha-` tags | Fixed: pinned by commit SHA with a version comment (as `dast.yml`); `latest` is no longer published (nothing referenced it) | `.github/workflows/release.yml`, `deploy.yml` |
| D-5 | `Dockerfile.web` base images were not pinned by digest; `npm ci` runs dependency install scripts | Digests pinned. `--ignore-scripts` NOT applied: the lock file lists install scripts of build tools (`esbuild`, `@swc/core`, `nx`, `@parcel/watcher`, `lmdb`, `msgpackr-extract`, `unrs-resolver`) and the build could not be tried without Docker. Try it in a pull request | `Dockerfile.web` |
| D-6 | `deploy/.state/` (written by `deploy.sh` on the VM) was not ignored | Fixed | `.gitignore` |

## Operating the backup alerts

`ims-backup@.service` has `OnFailure=ims-backup-alert@%i.service`; that unit runs `backup.sh alert <job>`, which calls `BACKUP_ALERT_COMMAND` with one message argument. `ims-backup-check.timer` runs `backup.sh check` hourly: it alerts when the disk holding the backups, the outbox or `/var/lib/docker` is at or above `BACKUP_DISK_WARN_PCT` (default 80) or when a backup has waited longer than `BACKUP_UNSHIPPED_WARN_HOURS` (default 6) to be shipped, and after a clean pass it runs `BACKUP_PING_COMMAND` (a heartbeat for a dead-man's-switch monitor, which also catches a dead timer or VM). Both commands live on the VM; the address or token they use belongs in a mode-600 file they read, never in this repository or in a command line (`ps` shows it). Without either setting the messages only reach the journal.

## Accepted risks (dependencies, dev tooling only)

Neither package is in the production dependency set: the CI gate `npm audit --omit=dev --audit-level=high` stays green, and the web image ships only the built files.

| Package | Advisory | Severity | Reach | Decision |
| ------- | -------- | -------- | ----- | -------- |
| `deepmerge` | no patched version exists | Low | dev only, through jest (`jest-config`) | Accepted. Nothing to apply |
| `glob` | GHSA-3g3m-2fjr-6pw4 | Moderate | dev only, through the jest and nx tool chains | Accepted. `npm audit fix --force` suggests a downgrade to `jest@25`: do NOT apply it (it breaks the Angular 22 and Nx test setup and trades one advisory for many older ones) |

Revisit when: a jest release (the `jest-config` and `glob` chain) or an nx release drops or upgrades these packages, when `deepmerge` publishes a fix, or when either one is found to be reachable from code that ships (the production audit gate or the build output would show it). Dependabot's weekly npm run proposes the update; read its alert list then.
