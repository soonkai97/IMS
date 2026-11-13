#!/usr/bin/env bash
# SQL Server backups of the Ims database and the state around it, run on the VM (systemd timers in deploy/scripts/systemd/ or cron.example).
#
#   backup.sh init      once: sets the FULL recovery model and takes the first full backup
#   backup.sh full      a full backup (nightly)
#   backup.sh log       a transaction-log backup (every 15 minutes; needs a full backup first and the FULL recovery model)
#   backup.sh prune     delete .bak and .trn files past BACKUP_RETENTION_DAYS (default 14) that are already shipped, keeping the newest BACKUP_KEEP_FULLS (default 3) fulls; runs in the db container
#   backup.sh verify    RESTORE VERIFYONLY on the newest full backup (a quick check; the real restore test is in deploy/scripts/rollback.md)
#   backup.sh ship      encrypt every .bak and .trn not yet shipped with age (public key BACKUP_AGE_RECIPIENT) and hand it to BACKUP_SHIP_COMMAND
#   backup.sh offsite   the same for the state the SQL backups do not hold: the files and keys volumes and deploy/.env.prod, then ship
#   backup.sh check     hourly: warn when a disk is fuller than BACKUP_DISK_WARN_PCT (default 80) or a backup has waited longer than BACKUP_UNSHIPPED_WARN_HOURS (default 6) to be shipped; pings BACKUP_PING_COMMAND when all is well
#   backup.sh alert J   what the systemd OnFailure= unit runs when job J (full, log, prune, ship, offsite) failed: calls BACKUP_ALERT_COMMAND
#
# .bak and .trn files stay on the VM disk, and a backup on the same disk is not a backup: `ship` is what puts an encrypted copy elsewhere. The age PRIVATE key
# must never be on this VM (keep it with the owner, on paper or in a password manager); without it nothing can be restored from the shipped copies.
#
# WATCHING IT: prune deletes only files that were shipped, so a `ship` that keeps failing makes .bak and .trn files pile up until the disk is full (and then the
# database stops). Two optional hooks in the env file, both commands that live on the VM (no URL or token in the repository; keep any secret in a mode-600 file the command reads):
#   BACKUP_ALERT_COMMAND  called with ONE argument, the message, when a job fails or `check` finds a problem. Example: BACKUP_ALERT_COMMAND=/usr/local/bin/ims-alert
#                         where that small script posts the message to a chat webhook or sends mail, reading the address from /etc/ims/alert.conf (chmod 600).
#   BACKUP_PING_COMMAND   called with no argument after every successful `check` (a heartbeat). Point it at a dead-man's-switch monitor (a healthchecks-style URL with a
#                         1 hour period and a 30 minute grace): it alerts when the pings STOP, which covers a dead timer, a dead VM and a full disk that blocks everything.
# The systemd units do the wiring (ims-backup@.service has OnFailure=, ims-backup-check.timer runs `check`); cron.example shows the same for cron.
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

# Calls the alert hook with one message (see WATCHING IT above). Without a hook the message only goes to stderr (the journal or the cron log).
alert() {
  local cmd
  echo "ALERT: $1" >&2
  cmd=$(setting BACKUP_ALERT_COMMAND)
  [ -n "$cmd" ] || return 0
  sh -c "$cmd \"\$1\"" sh "$1" || echo "the alert command failed" >&2
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
    # A file is deleted only when it is older than the retention, it is already in shipped.list (an encrypted copy has left the machine), and, for full backups,
    # it is not one of the newest BACKUP_KEEP_FULLS (default 3). Without shipping nothing is deleted: that is deliberate, watch the disk.
    keep=$(setting BACKUP_KEEP_FULLS); keep="${keep:-3}"
    shipped="$state_dir/shipped.list"
    [ -s "$shipped" ] || { echo "nothing has been shipped yet: nothing is pruned" >&2; exit 0; }
    newest=$(compose exec -T db sh -c "ls -1 $bak_dir | grep -E '^${db_name}_full_.*[.]bak\$' | sort | tail -n $keep" < /dev/null | tr -d '\r')
    compose exec -T db find "$bak_dir" -maxdepth 1 -type f '(' -name "${db_name}_full_*.bak" -o -name "${db_name}_log_*.trn" ')' -mtime "+$retention" -printf '%f\n' < /dev/null | tr -d '\r' | while read -r name; do
      grep -qxF "$name" "$shipped" || { echo "keep $name: not shipped"; continue; }
      printf '%s\n' "$newest" | grep -qxF "$name" && { echo "keep $name: one of the newest $keep full backups"; continue; }
      compose exec -T db rm -f "$bak_dir/$name" < /dev/null && echo "deleted $name"
    done
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
    ship_failed=0
    # Only files that have not changed for 10 minutes: a backup SQL Server is still writing must not be shipped (and marked shipped) half done.
    while read -r name; do
      grep -qxF "$name" "$shipped" && continue
      out="$outbox/$name.age"
      compose exec -T db cat "$bak_dir/$name" < /dev/null | age -r "$recipient" > "$out"
      if ship_file "$out"; then printf '%s\n' "$name" >> "$shipped"; else echo "could not ship $name; it stays on disk and is tried again" >&2; rm -f "$out"; ship_failed=1; fi
    done < <(compose exec -T db find "$bak_dir" -maxdepth 1 -type f '(' -name '*.bak' -o -name '*.trn' ')' -mmin +10 -printf '%f\n' < /dev/null | tr -d '\r' | sort)
    # A non-zero exit lets systemd run OnFailure= (the alert); the files stay and are tried again at the next run.
    [ "$ship_failed" -eq 0 ] || exit 1
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
  check)
    problems=0
    warn_pct=$(setting BACKUP_DISK_WARN_PCT); warn_pct="${warn_pct:-80}"
    warn_hours=$(setting BACKUP_UNSHIPPED_WARN_HOURS); warn_hours="${warn_hours:-6}"
    # 1. disk usage of the filesystems that hold the backups, the outbox and Docker's data (database files, volumes)
    for dir in "$backup_dir" "$outbox" /var/lib/docker; do
      [ -d "$dir" ] || continue
      used=$(df -P "$dir" | awk 'NR==2 { gsub("%", "", $5); print $5 }')
      if [ -n "$used" ] && [ "$used" -ge "$warn_pct" ]; then
        alert "$(hostname): the disk holding $dir is $used% full (warning at $warn_pct%). Check that backup.sh ship works and that prune deletes shipped files."
        problems=1
      fi
    done
    # 2. backups that have waited too long to be shipped (prune never deletes an unshipped file, so these are what fills the disk)
    shipped="$state_dir/shipped.list"
    touch "$shipped"
    waiting=0
    while read -r name; do
      grep -qxF "$name" "$shipped" || waiting=$((waiting + 1))
    done < <(compose exec -T db find "$bak_dir" -maxdepth 1 -type f '(' -name '*.bak' -o -name '*.trn' ')' -mmin "+$((warn_hours * 60))" -printf '%f\n' < /dev/null | tr -d '\r')
    if [ "$waiting" -gt 0 ]; then
      alert "$(hostname): $waiting backup file(s) have waited more than $warn_hours hours to be shipped off the machine (journalctl -u ims-backup@ship)."
      problems=1
    fi
    [ "$problems" -eq 0 ] || exit 1
    echo "check ok: disks under $warn_pct%, nothing unshipped for more than $warn_hours hours"
    ping_cmd=$(setting BACKUP_PING_COMMAND)
    if [ -n "$ping_cmd" ]; then sh -c "$ping_cmd" || echo "the ping command failed" >&2; fi
    ;;
  alert)
    job="${2:-}"
    case "$job" in
      # `check` has already sent its own, specific message.
      check) echo "check failed: see its own alert above" >&2 ;;
      full|log|prune|ship|offsite|verify|init) alert "$(hostname): the IMS backup job '$job' FAILED (journalctl -u ims-backup@$job -n 50). Files that were not shipped stay on the disk until it works again." ;;
      *) echo "unknown job: $job" >&2; exit 2 ;;
    esac
    ;;
  *)
    sed -n '2,16p' "$0" | sed 's/^# \{0,1\}//'
    exit 2
    ;;
esac
