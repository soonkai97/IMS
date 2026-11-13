# deploy/

The production kit for ONE small VM (Docker Compose + Caddy). The full description, the first-deploy runbook and the go-live gates are in `docs/architecture/deployment.md`; read it before anything here.

| File | What it is |
|---|---|
| `compose.prod.yml` | the stack: db, api, proxy (Caddy with the web app). Only the proxy publishes ports (80, 443) |
| `compose.iptest.yml` | override for a throw-away test by IP address over plain http. NOT for real data |
| `Caddyfile` | TLS, security headers, request limits, the web app and the API route |
| `prod.env.example` | the NAMES of every setting. Copy to `deploy/.env.prod` on the VM, chmod 600. Never commit a value |
| `scripts/deploy.sh` | pull a tag, start, wait for health, smoke check. Never runs migrations |
| `scripts/smoke.sh`, `scripts/backup.sh`, `scripts/create-app-login.sql`, `scripts/rollback.md`, `scripts/systemd/`, `scripts/cron.example` | the rest |

## Mandatory GitHub settings (without them the Deploy workflow must not be used)

1. Settings > Environments > `production` > **Deployment branches and tags: Selected branches and tags, add `main` only.** Otherwise any branch that changes `deploy.yml` or the scripts can run with the environment's secrets. (The workflow also stops itself unless it runs from `main` and unless the image's commit is on `main`.)
2. The same environment: **Required reviewers** (the people who approve each deploy), **Prevent self-review** on, and a second human among the reviewers.
3. The four environment secrets `DEPLOY_HOST`, `DEPLOY_USER`, `DEPLOY_SSH_KEY`, `DEPLOY_KNOWN_HOSTS`, and optionally the variables `DOMAIN` and `DEPLOY_PATH`.

Nothing in this folder was run on a host: there is no VM yet. `docs/architecture/deployment.md` section 7 lists what could not be verified.
