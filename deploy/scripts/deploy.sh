#!/usr/bin/env bash
# Deploy a released image tag to this VM (docker compose). Run on the VM, from any directory; the deploy workflow calls it over SSH.
#
#   deploy.sh --tag sha-1a2b3c4 [--migrated] [--env-file PATH]
#   deploy.sh --db-only                       first deploy: start only the database, so the DBA script can create the schema
#
# Migrations are NEVER run here (docs/dba/README.md section 17). Without --migrated the script pulls the images, prints how to run the
# migration script, and stops before anything is restarted (exit 3). With --migrated it starts the stack, waits for health and runs smoke.sh.
set -euo pipefail
umask 077

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
env_value() { sed -n "s/^$1=\([^#]*\).*/\1/p" "$env_file" | tail -n 1 | sed 's/[[:space:]]*$//'; }

# Rewrites KEY=value in the env file (the value is an image reference, never a secret). The new file is created under umask 077.
set_env() {
  if grep -q "^$1=" "$env_file"; then
    sed "s|^$1=.*|$1=$2|" "$env_file" > "$env_file.new" && cat "$env_file.new" > "$env_file" && rm -f "$env_file.new"
  else
    printf '%s=%s\n' "$1" "$2" >> "$env_file"
  fi
}

[ -r "$env_file" ] || { echo "env file not found: $env_file (copy deploy/prod.env.example and fill it in on this VM)" >&2; exit 1; }

# The file holds secrets: it must not be readable by anyone else.
mode=$(stat -c '%a' "$env_file" 2>/dev/null || stat -f '%Lp' "$env_file")
[ "$mode" = 600 ] || { echo "$env_file must be mode 600 (it is $mode): chmod 600 $env_file" >&2; exit 1; }

for name in IMS_GITHUB_OWNER IMS_DB_PASSWORD IMS_JWT_KEY IMS_CORS_ORIGINS MSSQL_PID DOMAIN; do
  [ -n "$(env_value "$name")" ] || { echo "$name is empty in $env_file" >&2; exit 1; }
done

# The API should not connect as sa on a production host: say so loudly on every deploy until ims_app is in use (docs/architecture/deployment.md section 6, gate 1).
db_user=$(env_value IMS_DB_USER)
if [ -z "$db_user" ] || [ "$db_user" = sa ]; then
  echo "WARNING: the API connects to the database as sa. That is not acceptable for real data: create the ims_app login (deploy/scripts/create-app-login.sql) and set IMS_DB_USER=ims_app." >&2
fi

mkdir -p "$state_dir"

if [ "$db_only" -eq 1 ]; then
  [ -n "$(env_value IMS_API_REF)" ] || set_env IMS_API_REF :none
  [ -n "$(env_value IMS_WEB_REF)" ] || set_env IMS_WEB_REF :none
  compose up -d db
  echo "database started. Run the migration script now (see docs/architecture/deployment.md, first deploy), then: deploy.sh --tag <tag> --migrated"
  exit 0
fi

[ -n "$tag" ] || { echo "--tag is required" >&2; usage; }

# This script is the trust boundary (the workflow checks the tag too, but a person can run it by hand): a tag is letters, digits, dot, dash and underscore only.
case "$tag" in
  ''|*[!A-Za-z0-9_.-]*|-*|.*) echo "invalid image tag: $tag" >&2; exit 1 ;;
esac
[ "${#tag}" -le 128 ] || { echo "image tag too long" >&2; exit 1; }

owner=$(env_value IMS_GITHUB_OWNER)
previous_ref=$(env_value IMS_API_REF)
previous=${previous_ref#:}

# A private package needs a login; the workflow passes a short-lived token in GHCR_TOKEN. docker login stores the credential in ~/.docker/config.json, so the script logs out again when it ends (also on an error).
if [ -n "${GHCR_TOKEN:-}" ]; then
  trap 'docker logout ghcr.io > /dev/null 2>&1 || true' EXIT
  printf '%s' "$GHCR_TOKEN" | docker login ghcr.io -u "${GHCR_USER:-$owner}" --password-stdin
fi

echo "pulling $tag"
IMS_API_REF=":$tag" IMS_WEB_REF=":$tag" compose pull api proxy

if [ "$migrated" -ne 1 ]; then
  cat <<MSG

Nothing has been restarted. Migrations are not run automatically.
1. Produce the script for the schema change between the running build and $tag (on a development machine, from the commit of $tag):
     dotnet ef migrations script <from-migration> <to-migration> --project src/Ims.Infrastructure --startup-project src/Ims.Api --output ims-migrate.sql
2. Back up first:  $here/backup.sh full
3. Put ims-migrate.sql into the database container (the backup directory is owned by the SQL Server user, so the deploy user cannot write to it):
     docker compose --env-file $env_file -f $compose_file cp ims-migrate.sql db:/var/opt/mssql/backup/ims-migrate.sql
   Stop the API when the migration needs a schema lock, and run it by hand
   (docs/dba/README.md sections 12, 15, 16, 17). The sa password is read inside the container from SQLCMDPASSWORD, so it is on no command line:
     docker compose --env-file $env_file -f $compose_file exec -T db /opt/mssql-tools18/bin/sqlcmd -C -I -b -S localhost -U sa -d Ims -i /var/opt/mssql/backup/ims-migrate.sql
4. Then run:  $here/deploy.sh --tag $tag --migrated
If $tag needs no schema change, run step 4 at once.
MSG
  exit 3
fi

# Keep the previous tag for the rollback hint, unless this is a redeploy of the same tag.
if [ -n "$previous" ] && [ "$previous" != none ] && [ "$previous" != "$tag" ]; then
  printf '%s\n' "$previous" > "$state_dir/previous-tag"
fi
set_env IMS_API_REF ":$tag"
set_env IMS_WEB_REF ":$tag"
printf '%s\n' "$tag" > "$state_dir/current-tag"

compose up -d --remove-orphans

echo "waiting for health (up to 180 s)"
waited=0
api=missing
proxy=missing
while [ "$waited" -lt 180 ]; do
  api=$(docker inspect --format '{{if .State.Health}}{{.State.Health.Status}}{{else}}none{{end}}' "$(compose ps -q api)" 2>/dev/null || echo missing)
  proxy=$(docker inspect --format '{{if .State.Health}}{{.State.Health.Status}}{{else}}none{{end}}' "$(compose ps -q proxy)" 2>/dev/null || echo missing)
  if [ "$api" = healthy ] && [ "$proxy" = healthy ]; then break; fi
  sleep 5
  waited=$((waited + 5))
done
rollback_to=$(cat "$state_dir/previous-tag" 2>/dev/null || echo "")
if [ "$api" != healthy ] || [ "$proxy" != healthy ]; then
  echo "not healthy after 180 s (api: $api, proxy: $proxy). Logs: docker compose --env-file $env_file -f $compose_file logs --tail 100 api proxy" >&2
  echo "rollback: $here/rollback.md (previous tag: ${rollback_to:-none})" >&2
  exit 1
fi

if ! SMOKE_BASE_URL="https://$(env_value DOMAIN)" "$here/smoke.sh"; then
  echo "smoke check FAILED for $tag. Rollback: $here/rollback.md (previous tag: ${rollback_to:-none})" >&2
  exit 1
fi

echo "deployed $tag (previous: ${rollback_to:-none})."
echo "rollback hint: $here/deploy.sh --tag ${rollback_to:-<previous-tag>} --migrated  (schema changes are rolled back by hand, in order: see $here/rollback.md)"
