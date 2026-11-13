#!/usr/bin/env bash
# SQL Server backups of the Ims database, run on the VM (cron or the systemd timers in deploy/scripts/systemd/).
#
#   backup.sh full      a full backup (nightly)
#   backup.sh log       a transaction-log backup (every 15 minutes; needs a full backup first and the FULL recovery model)
#   backup.sh init      once: sets the FULL recovery model and takes the first full backup
#   backup.sh prune     delete .bak and .trn files older than BACKUP_RETENTION_DAYS (default 14)
#   backup.sh verify    RESTORE VERIFYONLY on the newest full backup (a quick check; a real restore test is in docs/architecture/deployment.md)
#
# Files land in IMS_BACKUP_DIR on the host (mounted into the db container at /var/opt/mssql/backup). They are NOT encrypted and NOT off the machine:
# copy them elsewhere (another host or an object store) with the tool the owner chooses, and encrypt them there. A backup on the same disk is not a backup.
set -euo pipefail

here=$(CDPATH='' cd -- "$(dirname -- "$0")" && pwd)
deploy_dir=$(dirname -- "$here")
compose_file="$deploy_dir/compose.prod.yml"
env_file="${IMS_ENV_FILE:-$deploy_dir/.env.prod}"
db_name="${IMS_DB_NAME:-Ims}"

[ -r "$env_file" ] || { echo "env file not found: $env_file" >&2; exit 1; }

env_value() { sed -n "s/^$1=\\([^#]*\\).*/\\1/p" "$env_file" | tail -n 1 | sed 's/[[:space:]]*$//'; }
backup_dir=$(env_value IMS_BACKUP_DIR)
backup_dir="${backup_dir:-/srv/ims/backups}"
retention="${BACKUP_RETENTION_DAYS:-$(env_value BACKUP_RETENTION_DAYS)}"
retention="${retention:-14}"
compression="${BACKUP_COMPRESSION:-$(env_value BACKUP_COMPRESSION)}"
with_compression=""
[ "${compression:-0}" = 1 ] && with_compression=", COMPRESSION"

# T-SQL runs inside the db container; the sa password is read there from its own environment and never appears on a command line.
sql() {
  docker compose --env-file "$env_file" -f "$compose_file" exec -T db \
    sh -c '/opt/mssql-tools18/bin/sqlcmd -C -b -S localhost -U sa -P "$MSSQL_SA_PASSWORD" -Q "$1"' sh "$1"
}

stamp=$(date -u +%Y%m%dT%H%M%SZ)

case "${1:-}" in
  init)
    sql "ALTER DATABASE [$db_name] SET RECOVERY FULL;"
    sql "BACKUP DATABASE [$db_name] TO DISK = N'/var/opt/mssql/backup/${db_name}_full_${stamp}.bak' WITH INIT, CHECKSUM${with_compression};"
    ;;
  full)
    sql "BACKUP DATABASE [$db_name] TO DISK = N'/var/opt/mssql/backup/${db_name}_full_${stamp}.bak' WITH INIT, CHECKSUM${with_compression};"
    ;;
  log)
    sql "BACKUP LOG [$db_name] TO DISK = N'/var/opt/mssql/backup/${db_name}_log_${stamp}.trn' WITH INIT, CHECKSUM${with_compression};"
    ;;
  prune)
    find "$backup_dir" -maxdepth 1 -type f \( -name "${db_name}_full_*.bak" -o -name "${db_name}_log_*.trn" \) -mtime "+$retention" -print -delete
    ;;
  verify)
    newest=$(find "$backup_dir" -maxdepth 1 -type f -name "${db_name}_full_*.bak" | sort | tail -n 1)
    [ -n "$newest" ] || { echo "no full backup in $backup_dir" >&2; exit 1; }
    sql "RESTORE VERIFYONLY FROM DISK = N'/var/opt/mssql/backup/$(basename -- "$newest")' WITH CHECKSUM;"
    ;;
  *)
    sed -n '2,12p' "$0" | sed 's/^# \{0,1\}//'
    exit 2
    ;;
esac
