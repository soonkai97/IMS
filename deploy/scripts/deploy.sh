#!/usr/bin/env bash
# Deploy a released image tag to this VM (docker compose). Run on the VM, from any directory; the deploy workflow calls it over SSH.
#
#   deploy.sh --tag sha-1a2b3c4 [--migrated] [--env-file PATH]
#   deploy.sh --db-only                       first deploy: start only the database, so the DBA script can create the schema
#
# Migrations are NEVER run here (docs/dba/README.md section 17). Without --migrated the script pulls the images, prints how to run the
# migration script, and stops before anything is restarted. With --migrated it starts the stack, waits for health and runs smoke.sh.
set -euo pipefail

here=$(CDPATH='' cd -- "$(dirname -- "$0")" && pwd)
deploy_dir=$(dirname -- "$here")
compose_file="$deploy_dir/compose.prod.yml"
env_file="$deploy_dir/.env.prod"
state_dir="${IMS_STATE_DIR:-$deploy_dir/.state}"
tag=""
migrated=0
db_only=0

usage() {
  sed -n '2,9p' "$0" | sed 's/^# \{0,1\}//'
  exit 2
}

while [ $# -gt 0 ]; do
  case "$1" in
    --tag) [ $# -ge 2 ] || usage; tag=$2; shift 2 ;;
    --migrated) migrated=1; shift ;;
    --db-only) db_only=1; shift ;;
    --env-file) [ $# -ge 2 ] || usage; env_file=$2; shift 2 ;;
    -h|--help) usage ;;
    *) echo "unknown argument: $1" >&2; usage ;;
  esac
done

compose() { docker compose --env-file "$env_file" -f "$compose_file" "$@"; }

# Reads KEY from the env file without sourcing it (the file holds secrets and comments).
env_value() { sed -n "s/^$1=\\([^#]*\\).*/\\1/p" "$env_file" | tail -n 1 | sed 's/[[:space:]]*$//'; }

# Rewrites KEY=value in the env file (the value is a tag, never a secret).
set_env() {
  if grep -q "^$1=" "$env_file"; then
    sed "s|^$1=.*|$1=$2|" "$env_file" > "$env_file.new" && cat "$env_file.new" > "$env_file" && rm -f "$env_file.new"
  else
    printf '%s=%s\n' "$1" "$2" >> "$env_file"
  fi
}

[ -r "$env_file" ] || { echo "env file not found: $env_file (copy deploy/.env.prod.example and fill it in on this VM)" >&2; exit 1; }

for name in IMS_GITHUB_OWNER IMS_DB_PASSWORD IMS_JWT_KEY IMS_ADMIN_PASSWORD IMS_CORS_ORIGINS MSSQL_PID; do
  [ -n "$(env_value "$name")" ] || { echo "$name is empty in $env_file" >&2; exit 1; }
done

mkdir -p "$state_dir"

if [ "$db_only" -eq 1 ]; then
  [ -n "$(env_value IMS_API_TAG)" ] || set_env IMS_API_TAG none
  [ -n "$(env_value IMS_WEB_TAG)" ] || set_env IMS_WEB_TAG none
  compose up -d db
  echo "database started. Run the migration script now (see docs/architecture/deployment.md, first deploy), then: deploy.sh --tag <tag> --migrated"
  exit 0
fi

[ -n "$tag" ] || { echo "--tag is required" >&2; usage; }

owner=$(env_value IMS_GITHUB_OWNER)
previous=$(env_value IMS_API_TAG)

# A private package needs a login; the workflow passes a short-lived token in GHCR_TOKEN (never written to disk by this script).
if [ -n "${GHCR_TOKEN:-}" ]; then
  printf '%s' "$GHCR_TOKEN" | docker login ghcr.io -u "${GHCR_USER:-$owner}" --password-stdin
fi

echo "pulling $tag"
IMS_API_TAG=$tag IMS_WEB_TAG=$tag compose pull api proxy

if [ "$migrated" -ne 1 ]; then
  cat <<EOF

Nothing has been restarted. Migrations are not run automatically.
1. Produce the script for the schema change between the running build and $tag (on a development machine, from the commit of $tag):
     dotnet ef migrations script <from-migration> <to-migration> --project src/Ims.Infrastructure --startup-project src/Ims.Api --output ims-migrate.sql
2. Back up first:  deploy/scripts/backup.sh full
3. Run it by hand, with the API stopped when the migration needs a schema lock (docs/dba/README.md sections 12, 15, 16, 17):
     docker compose --env-file $env_file -f $compose_file exec -T db /opt/mssql-tools18/bin/sqlcmd -C -I -b -S localhost -U sa -P "\$(grep '^IMS_DB_PASSWORD=' $env_file | cut -d= -f2-)" -d Ims -i /var/opt/mssql/backup/ims-migrate.sql
   (copy ims-migrate.sql into the backup directory first). The password stays out of this script's output; type it instead if the shell history matters.
4. Then run:  deploy.sh --tag $tag --migrated
If $tag needs no schema change, run step 4 at once.
EOF
  exit 3
fi

set_env IMS_API_TAG "$tag"
set_env IMS_WEB_TAG "$tag"
printf '%s\n' "$previous" > "$state_dir/previous-tag"
printf '%s\n' "$tag" > "$state_dir/current-tag"

compose up -d --remove-orphans

echo "waiting for health (up to 180 s)"
waited=0
while [ "$waited" -lt 180 ]; do
  api=$(docker inspect --format '{{if .State.Health}}{{.State.Health.Status}}{{else}}none{{end}}' "$(compose ps -q api)" 2>/dev/null || echo missing)
  proxy=$(docker inspect --format '{{if .State.Health}}{{.State.Health.Status}}{{else}}none{{end}}' "$(compose ps -q proxy)" 2>/dev/null || echo missing)
  if [ "$api" = healthy ] && [ "$proxy" = healthy ]; then break; fi
  sleep 5
  waited=$((waited + 5))
done
if [ "$api" != healthy ] || [ "$proxy" != healthy ]; then
  echo "not healthy after 180 s (api: $api, proxy: $proxy). Logs: docker compose --env-file $env_file -f $compose_file logs --tail 100 api proxy" >&2
  echo "rollback: $deploy_dir/scripts/rollback.md (previous tag: ${previous:-none})" >&2
  exit 1
fi

domain=$(env_value DOMAIN)
if [ -n "$domain" ]; then base="https://$domain"; else base="http://127.0.0.1"; fi
if ! SMOKE_BASE_URL=$base "$here/smoke.sh"; then
  echo "smoke check FAILED for $tag. Rollback: $deploy_dir/scripts/rollback.md (previous tag: ${previous:-none})" >&2
  exit 1
fi

echo "deployed $tag (previous: ${previous:-none})."
echo "rollback hint: deploy.sh --tag ${previous:-<previous-tag>} --migrated  (schema changes are rolled back by hand, in order: see deploy/scripts/rollback.md)"
