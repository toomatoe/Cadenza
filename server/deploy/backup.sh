#!/usr/bin/env bash
# Consistent online backup of Cadenza's WAL-mode SQLite database.
set -Eeuo pipefail
umask 077

DB=/srv/cadenza/data/cadenza.db
BACKUP_DIR=/srv/cadenza/backups
RETENTION_MINUTES=20160  # 14 days

if [[ ! -f "$DB" ]]; then
    echo "Cadenza database not found: $DB (start the server first)" >&2
    exit 1
fi
command -v sqlite3 >/dev/null || { echo "Install sqlite3 first" >&2; exit 1; }
install -d -m 0700 "$BACKUP_DIR"

temporary=$(mktemp "$BACKUP_DIR/.cadenza-XXXXXXXX.db")
trap 'rm -f "$temporary"' EXIT

# SQLite's online backup API makes a consistent snapshot even in WAL mode.
sqlite3 -cmd '.timeout 10000' "$DB" ".backup '$temporary'"
result=$(sqlite3 "$temporary" 'PRAGMA integrity_check;')
if [[ "$result" != "ok" ]]; then
    echo "Backup integrity check failed: $result" >&2
    exit 1
fi

destination="$BACKUP_DIR/cadenza-$(date -u +%Y%m%dT%H%M%SZ).db"
if [[ -e "$destination" ]]; then
    echo "Backup already exists: $destination" >&2
    exit 1
fi
mv "$temporary" "$destination"
trap - EXIT
chmod 0600 "$destination"
find "$BACKUP_DIR" -maxdepth 1 -type f -name 'cadenza-*.db' -mmin +"$RETENTION_MINUTES" -delete
echo "Verified Cadenza backup: $destination"
