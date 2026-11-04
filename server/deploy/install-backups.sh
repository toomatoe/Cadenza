#!/usr/bin/env bash
# Installs the backup job and timer on Debian/Raspberry Pi OS.
set -Eeuo pipefail
here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

if ! command -v sqlite3 >/dev/null; then
    echo "sqlite3 is required: sudo apt install sqlite3" >&2
    exit 1
fi
if ! sudo test -f /srv/cadenza/data/cadenza.db; then
    echo "Cadenza database not found at /srv/cadenza/data/cadenza.db. Verify the server is running and check its storage mount." >&2
    exit 1
fi

sudo install -d -m 0700 /srv/cadenza/backups
sudo install -m 0755 "$here/backup.sh" /usr/local/sbin/cadenza-backup
sudo install -m 0644 "$here/cadenza-backup.service" /etc/systemd/system/cadenza-backup.service
sudo install -m 0644 "$here/cadenza-backup.timer" /etc/systemd/system/cadenza-backup.timer
sudo systemctl daemon-reload
sudo systemctl enable --now cadenza-backup.timer
sudo systemctl start cadenza-backup.service
sudo systemctl --no-pager list-timers cadenza-backup.timer
echo "Backups are ON-CARD only. Copy them off-device before trusting them for recovery from card failure."
