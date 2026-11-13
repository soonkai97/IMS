#!/usr/bin/env bash
# SQL Server backups of the Ims database and the state around it, run on the VM (systemd timers in deploy/scripts/systemd/ or cron.example).
#
#   backup.sh init      once: sets the FULL recovery model and takes the first full backup
#   backup.sh full      a full backup (nightly)
#   backup.sh log       a transaction-log backup (every 15 minutes; needs a full backup first and the FULL recovery model)
#   backup.sh prune     delete .bak and .trn files older than BACKUP_RETENTION_DAYS (default 14), inside the database container (it owns the files)
#   backup.sh verify    RESTORE VERIFYONLY on the newest full backup (a quick check; the real restore test is in deploy/scripts/rollback.md)
#   backup.sh ship      encrypt every .bak and .trn not yet shipped with age (public key BACKUP_AGE_RECIPIENT) and hand it to BACKUP_SHIP_COMMAND
#   backup.sh offsite   the same for the state the SQL backups do not hold: the files and keys volumes and deploy/.env.prod, then ship
#
# .bak and .trn files stay on the VM disk, and a backup on the same disk is not a backup: `ship` is what puts an encrypted copy elsewhere. The age PRIVATE key
# must never be on this VM (keep it with the owner, on paper or in a password manager); without it nothing can be restored from the shipped copies.
set -euo pipefail
umask 077

here=$(CDPATH='' cd -- "$(dirname -- "$0")" && pwd)
deploy_dir=$(dirname -- "$here")
compose_file="$deploy_dir/compose.prod.yml"
env_file="${IMS_ENV_FILE:-$deploy_dir/.env.prod}"
state_dir="${IMS_STATE_DIR:-$deploy_dir/.state}"
db_name="${IMS_DB_NAME:-Ims}"
bak_dir=/var/opt/mssql/backup

[ -r "$env_file" ] || { echo "env file not found: $env_file" >&2; exit 1; }

env_value() { sed -n "s/^$1=\([^#]*\).*/\1/p" "$env_file" | tail -n 1 | sed 's/[[:space:]]*$//'; }
setting() { local v="${!1:-}"; [ -n "$v" ] || v=$(env_value "$1"); printf '%s' "$v"; }

backup_dir=$(setting IMS_BACKUP_DIR); backup_dir="${backup_dir:-/srv/ims/backups}"
retention=$(setting BACKUP_RETENTION_DAYS); retention="${retention:-14}"
compression=$(setting BACKUP_COMPRESSION)
with_compression=""
[ "${compression:-0}" = 1 ] && with_compression=", COMPRESSION"
outbox=$(setting BACKUP_OUTBOX); outbox="${outbox:-/srv/ims/outbox}"

compose() { docker compose --env-file "$env_file" -f "$compose_file" "$@"; }

# T-SQL runs inside the db container; sqlcmd takes the sa password from SQLCMDPASSWORD there, so no command line carries it.
sql() { compose exec -T db sh -c '/opt/mssql-tools18/bin/sqlcmd -C -b -S localhost -U sa -Q "$1"' sh "$1"; }

stamp=$(date -u +%Y%m%dT%H%M%SZ)

# Encrypts a stream to the age public key into the outbox, hands the file to the ship command, and removes it when that succeeded.
ship_file() {
  local file=$1 ship_cmd
  ship_cmd=$(setting BACKUP_SHIP_COMMAND)
  [ -n "$ship_cmd" ] || { echo "BACKUP_SHIP_COMMAND is not set: nothing leaves this machine" >&2; return 1; }
  sh -c "$ship_cmd \"\$1\"" sh "$file" && rm -f "$file"
}

require_age() {
  command -v age > /dev/null 2>&1 || { echo "age is not installed (apt install age)" >&2; exit 1; }
  recipient=$(setting BACKUP_AGE_RECIPIENT)
  case "$recipient" in age1*) ;; *) echo "BACKUP_AGE_RECIPIENT must be an age public key (age1...)" >&2; exit 1 ;; esac
  mkdir -p "$outbox" "$state_dir"
}

case "${1:-}" in
  init)
    sql "ALTER DATABASE [$db_name] SET RECOVERY FULL;"
    sql "BACKUP DATABASE [$db_name] TO DISK = N'$bak_dir/${db_name}_full_${stamp}.bak' WITH INIT, CHECKSUM${with_compression};"
    ;;
  full)
    sql "BACKUP DATABASE [$db_name] TO DISK = N'$bak_dir/${db_name}_full_${stamp}.bak' WITH INIT, CHECKSUM${with_compression};"
    ;;
  log)
    sql "BACKUP LOG [$db_name] TO DISK = N'$bak_dir/${db_name}_log_${stamp}.trn' WITH INIT, CHECKSUM${with_compression};"
    ;;
  prune)
    # The files belong to the SQL Server user (10001), which the deploy user cannot delete from: do it where the files are owned.
    compose exec -T db find "$bak_dir" -maxdepth 1 -type f '(' -name "${db_name}_full_*.bak" -o -name "${db_name}_log_*.trn" ')' -mtime "+$retention" -print -delete
    ;;
  verify)
    newest=$(compose exec -T db sh -c "ls -1 $bak_dir/${db_name}_full_*.bak 2>/dev/null | sort | tail -n 1" | tr -d '\r')
    [ -n "$newest" ] || { echo "no full backup in $bak_dir" >&2; exit 1; }
    sql "RESTORE VERIFYONLY FROM DISK = N'$newest' WITH CHECKSUM;"
    ;;
  ship)
    require_age
    shipped="$state_dir/shipped.list"
    touch "$shipped"
    compose exec -T db sh -c "ls -1 $bak_dir" | tr -d '\r' | grep -E '\.(bak|trn)$' | while read -r name; do
      grep -qxF "$name" "$shipped" && continue
      out="$outbox/$name.age"
      compose exec -T db cat "$bak_dir/$name" < /dev/null | age -r "$recipient" > "$out"
      if ship_file "$out"; then printf '%s\n' "$name" >> "$shipped"; else echo "could not ship $name; it stays in the outbox and on disk" >&2; fi
    done
    ;;
  offsite)
    require_age
    image=$(docker inspect --format '{{.Config.Image}}' "$(compose ps -q proxy)")
    # The attachment files and the Data Protection key ring are not in the SQL backups. A throw-away container tars each volume read-only.
    for volume in files keys; do
      out="$outbox/ims_${volume}_${stamp}.tar.gz.age"
      docker run --rm --entrypoint tar -v "ims_${volume}:/data:ro" "$image" -cz -C /data . | age -r "$recipient" > "$out"
      ship_file "$out"
    done
    # The env file holds every secret: it is shipped encrypted, like the rest.
    out="$outbox/env_prod_${stamp}.age"
    age -r "$recipient" < "$env_file" > "$out"
    ship_file "$out"
    "$0" ship
    ;;
  *)
    sed -n '2,14p' "$0" | sed 's/^# \{0,1\}//'
    exit 2
    ;;
esac
