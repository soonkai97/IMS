# Deployment

Owner decision 2026-11-13 (O3): ONE small VM, Docker Compose, Caddy as the TLS proxy, nightly full backups plus a log backup every 15 minutes. No Kubernetes. There is no VM and no domain yet: everything below is built and checked without a host, and has not been run on one.

## 1. Topology

```
 Internet
    |  80, 443 (tcp), 443 (udp, HTTP/3)          the ONLY published ports
    v
 +------------------------------ VM (Docker network "ims", 172.30.0.0/24) --------------------------------+
 |  proxy  (Caddy + the web app in /srv/ims-web, image ghcr.io/<owner>/ims-web:<tag>)   172.30.0.10        |
 |     |  /api/*, /health  ->  api:8080                          everything else: the PWA (static files)   |
 |     v                                                                                                   |
 |  api    (ghcr.io/<owner>/ims-api:<tag>, port 8080 inside only)                                          |
 |     |  Server=db  (SQL login sa, password from the env file)                                            |
 |     v                                                                                                   |
 |  db     (SQL Server 2022, port 1433 inside only)                                                        |
 +---------------------------------------------------------------------------------------------------------+
 volumes: dbdata (database files), files (attachments), keys (Data Protection key ring), applogs, caddy_data (certificates), caddy_config
 host directory: IMS_BACKUP_DIR (default /srv/ims/backups): .bak and .trn files
```

The web app is static files, not served by the API, so a second image is built by the Release workflow (`Dockerfile.web`: the Angular build on top of `caddy:2-alpine`). The proxy serves it and forwards `/api/*`; one host means no CORS, a same-site refresh cookie and `connect-src 'self'`. The header set follows `docs/security/web-headers.md`. The API keeps its own stricter headers.

Files: `deploy/compose.prod.yml`, `deploy/Caddyfile`, `deploy/.env.prod.example` (names only), `deploy/scripts/` (`deploy.sh`, `smoke.sh`, `backup.sh`, `rollback.md`, `systemd/` timers, `cron.example`), `Dockerfile.web`, `.github/workflows/deploy.yml`.

## 2. What the owner must provide

1. A VM (Linux, 2 vCPU and 4 GB RAM are enough for the pilot; about US$6 to 24 a month, `ROADMAP.md` O3) with Docker Engine and the compose plugin, `git`, a deploy user in the `docker` group, SSH key login only, and a firewall that opens 22 (ideally from known addresses), 80 and 443 and nothing else.
2. A domain name with an A (and AAAA) record to the VM. Without one, the stack can be tested over plain http by IP address (leave `DOMAIN` empty); do not run real data that way.
3. The SQL Server edition and licence: `MSSQL_PID` (Express is free and limited to 10 GB per database; Standard needs a licence). Never Developer on a production host.
4. Where backups go OFF the machine and how they are encrypted (another host, or an object store). `backup.sh` writes to a host directory only.
5. The real approval amounts for purchase and sales orders before those modules go live (`docs/dba/README.md` section 17, STOP).
6. GitHub settings, in the repository: Settings > Environments > New environment `production`, tick Required reviewers (the people who may approve a deploy). In that environment add the secrets `DEPLOY_HOST`, `DEPLOY_USER`, `DEPLOY_SSH_KEY`, `DEPLOY_KNOWN_HOSTS` and optionally the variables `DOMAIN` (runs the smoke check against the public name after a deploy) and `DEPLOY_PATH` (default `/opt/ims`). The application secrets are NOT GitHub secrets: they live only in `deploy/.env.prod` on the VM. Until the four secrets exist the Deploy workflow stops at its first step with "Secrets not configured" and the list of what is missing.

## 3. First deploy (runbook)

1. On the VM: `git clone https://github.com/<owner>/IMS.git /opt/ims` (or the `DEPLOY_PATH`), `cd /opt/ims`.
2. `cp deploy/.env.prod.example deploy/.env.prod && chmod 600 deploy/.env.prod`, then fill in every name (generate `IMS_JWT_KEY` and the passwords; set `MSSQL_PID`, `DOMAIN`, `IMS_CORS_ORIGINS=https://<DOMAIN>`, `IMS_GITHUB_OWNER`). `mkdir -p /srv/ims/backups && chown 10001:0 /srv/ims/backups`.
3. Pick a released tag (the Release workflow publishes `sha-<7 hex>` for the API and the web image of the same commit). Log in to the registry if the packages are private (`docker login ghcr.io` with a token that has `read:packages`).
4. `deploy/scripts/deploy.sh --db-only` starts the database only.
5. Create the schema: either run the DBA script by hand (`docs/dba/README.md` sections 12 to 17, `sqlcmd -I -b`), or, for the very first deploy only, set `IMS_MIGRATE_ON_STARTUP=true` in the env file, run step 6, check the log, and set it back to `false` (the start with it on also creates the first administrator from `IMS_ADMIN_PASSWORD`; a script-only deploy creates none, so do one start with it on).
6. `deploy/scripts/deploy.sh --tag sha-1a2b3c4 --migrated`: pulls the images, starts the stack, waits for health and runs `smoke.sh`.
7. `deploy/scripts/backup.sh init` (sets the FULL recovery model and takes the first full backup), then install the timers: copy `deploy/scripts/systemd/*` to `/etc/systemd/system/`, adjust `User` and the paths, `systemctl enable --now ims-backup-full.timer ims-backup-log.timer ims-backup-prune.timer` (or use `cron.example`).
8. Do the restore test of `deploy/scripts/rollback.md` section 4 once, and set up the off-machine copy.
9. Sign in as `admin`, change the password, then follow the owner checks of `docs/dba/README.md` section 17 before switching any module on.

## 4. Every later deploy

Run the Deploy workflow (Actions > Deploy > Run workflow) with the image tag. Without `migrated` it pulls and stops, printing the migration command; run the script by hand when the release has a migration, then run the workflow again with `migrated` ticked. Reviewers on the `production` environment approve each run. Rollback: `deploy/scripts/rollback.md`.

## 5. Security notes (for the security review)

- Only Caddy is published; the database and the API are on the internal network. The API trusts forwarded headers from the proxy address only (`ForwardedHeaders__KnownProxies=172.30.0.10`).
- Containers run with `no-new-privileges`; the API and the proxy drop all capabilities (the proxy adds `NET_BIND_SERVICE`) and have a read-only root file system; the database does not drop capabilities because that was not verified without a host.
- Secrets exist in one place, `deploy/.env.prod` on the VM, mode 600. The workflow holds SSH access only, pinned by `DEPLOY_KNOWN_HOSTS` (strict host key checking, no trust on first use). The registry token reaches the VM over the SSH stdin.
- Backups are not encrypted and stay on the VM until the owner chooses an off-machine copy; the SQL `sa` login is used by the API (a least-privilege login is a later hardening).
- Not verified: no image was built or started, no certificate was requested, `caddy validate` and `shellcheck` were not run (see HANDOFFS).
