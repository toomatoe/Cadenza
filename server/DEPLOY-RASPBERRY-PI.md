# Deploying Cadenza Friends on a Raspberry Pi

This runs the Friends server on a Raspberry Pi at home and makes it reachable over HTTPS through a **Cloudflare Tunnel**, so **no router ports are opened** and your home IP isn't exposed. It's sized for an invite-only group: the server only handles accounts, friend lists, chat and track IDs. Audio never passes through it.

**What you need:**
- A Raspberry Pi 4/5 running **64-bit** Raspberry Pi OS (Bookworm) or Ubuntu Server arm64.
- SSH access to the Pi.
- A domain on Cloudflare (any cheap domain works).
- Ideally, a USB SSD for the database. SD cards wear out under database writes.

Commands marked **(PC)** run in the repo on your Windows PC. Everything else runs on the Pi.

---

## 1. Install the .NET 10 runtime (ASP.NET Core, arm64)

```bash
sudo apt update && sudo apt install -y curl sqlite3
curl -sSL https://dot.net/v1/dotnet-install.sh -o dotnet-install.sh
sudo bash dotnet-install.sh --channel 10.0 --runtime aspnetcore --install-dir /opt/dotnet
sudo ln -sf /opt/dotnet/dotnet /usr/local/bin/dotnet
dotnet --list-runtimes   # should list Microsoft.AspNetCore.App 10.0.x
```

## 2. Create a service user and data folder

```bash
sudo useradd --system --home /var/lib/cadenza --shell /usr/sbin/nologin cadenza
sudo mkdir -p /opt/cadenza /var/lib/cadenza /var/backups/cadenza
sudo chown cadenza:cadenza /var/lib/cadenza /var/backups/cadenza
```

With a USB SSD mounted at `/mnt/ssd`, use `/mnt/ssd/cadenza` instead of `/var/lib/cadenza` here and in the unit file below. For encryption at rest, put that folder on a LUKS-encrypted partition.

## 3. Publish and copy the server

**(PC)**
```powershell
dotnet publish server/Cadenza.Server.csproj -c Release -r linux-arm64 --self-contained false -o artifacts/server
scp -r artifacts/server/* pi@raspberrypi.local:/tmp/cadenza/
```

**(Pi)**
```bash
sudo cp -r /tmp/cadenza/* /opt/cadenza/ && rm -rf /tmp/cadenza
```

## 4. Run it with systemd

Create `/etc/systemd/system/cadenza.service`:

```ini
[Unit]
Description=Cadenza Friends server
After=network-online.target
Wants=network-online.target

[Service]
User=cadenza
WorkingDirectory=/opt/cadenza
ExecStart=/usr/local/bin/dotnet /opt/cadenza/Cadenza.Server.dll
Environment=ASPNETCORE_ENVIRONMENT=Production
Environment=Urls=http://localhost:5080
Environment=Cadenza__Database=/var/lib/cadenza/cadenza.db
Environment=Cadenza__MusicFolder=/var/lib/cadenza/music
Restart=on-failure
RestartSec=5

# Hardening: read-only system, writable data folder only.
NoNewPrivileges=true
ProtectSystem=strict
ProtectHome=true
PrivateTmp=true
ReadWritePaths=/var/lib/cadenza

[Install]
WantedBy=multi-user.target
```

```bash
sudo systemctl daemon-reload
sudo systemctl enable --now cadenza
curl http://localhost:5080/health        # prints: ok
sudo journalctl -u cadenza | grep "invite code"
```

The first start logs a **bootstrap invite code**. Use it from Cadenza (Friends → Join) to create your own account. After that, everyone joins with an invite created from inside the app, and each invite works once for 7 days. To choose the bootstrap code yourself, add `Environment=Cadenza__BootstrapInvite=ABCDE-FGHJK` before the first start.

## 5. Put it on the internet with Cloudflare Tunnel

```bash
# Install cloudflared (see Cloudflare's docs for the current package source):
curl -L https://github.com/cloudflare/cloudflared/releases/latest/download/cloudflared-linux-arm64.deb -o cloudflared.deb
sudo dpkg -i cloudflared.deb

cloudflared tunnel login                      # opens a link; pick your domain
cloudflared tunnel create cadenza
cloudflared tunnel route dns cadenza friends.example.com
```

Create `~/.cloudflared/config.yml` (use the tunnel ID that `create` printed):

```yaml
tunnel: <TUNNEL-ID>
credentials-file: /home/pi/.cloudflared/<TUNNEL-ID>.json
ingress:
  - hostname: friends.example.com
    service: http://localhost:5080
  - service: http_status:404
```

```bash
sudo cloudflared --config ~/.cloudflared/config.yml service install
sudo systemctl enable --now cloudflared
curl https://friends.example.com/health       # from any machine: ok
```

WebSockets pass through the tunnel automatically. The server sends keep-alives every 30 s, well inside Cloudflare's 100 s idle limit. Kestrel listens on `localhost` only, so the tunnel is the only way in.

In Cadenza, everyone uses `https://friends.example.com` as the server address.

## 5a. The shared music library

Songs people add from Cadenza (Library → **Add songs**) are stored in `Cadenza__MusicFolder`, one file per song named by ID, and listed in the database. Plan for space: an album of MP3s is about 100 MB, and FLAC is 3–4 times that. Put the folder on a USB drive if you can (for example `Environment=Cadenza__MusicFolder=/mnt/ssd/cadenza/music`, and add that path to `ReadWritePaths`).

- Uploads are limited to 95 MB per file, because Cloudflare's free plan accepts 100 MB per request.
- Music streams through the same tunnel as chat. Cloudflare's terms discourage using its network mainly for large media files. For a few friends that's a judgement call. If it becomes a concern, serve the server over **Tailscale** instead: it's free for personal use, and each person installs Tailscale and uses the Pi's Tailscale address as the server.
- For phone apps or a web player, you can run Navidrome next to it and point it at the same folder, read-only.

## 6. Nightly backups

`/usr/local/bin/cadenza-backup`:

```bash
#!/bin/sh
set -e
sqlite3 /var/lib/cadenza/cadenza.db ".backup '/var/backups/cadenza/cadenza-$(date +%F).db'"
rsync -a /var/lib/cadenza/music/ /var/backups/cadenza/music/
find /var/backups/cadenza -name 'cadenza-*.db' -mtime +14 -delete
```

```bash
sudo chmod +x /usr/local/bin/cadenza-backup
echo "30 3 * * * cadenza /usr/local/bin/cadenza-backup" | sudo tee /etc/cron.d/cadenza-backup
```

Also copy `/var/backups/cadenza` off the Pi, to another disk or to cloud storage (for example `rclone`). Test a restore once: stop the service, copy a backup over `cadenza.db`, then start it again.

## 7. Keeping it healthy

- **OS updates:** `sudo apt install unattended-upgrades` and keep it on.
- **SSH:** keys only (`PasswordAuthentication no`), or reachable only over Tailscale.
- **Updating Cadenza:** repeat step 3, then `sudo systemctl restart cadenza`. The database schema is created and upgraded on start.
- **Logs:** `sudo journalctl -u cadenza -f`.
- **Outages:** if the Pi or your internet goes down, Cadenza keeps playing music. Friends shows "Offline. Retrying…" and reconnects on its own.

## 8. Moderation (you are the reviewer)

Reports keep a copy of the reported message, even if the sender deletes their account:

```bash
sudo -u cadenza sqlite3 -header -column /var/lib/cadenza/cadenza.db \
  "SELECT r.id, datetime(r.created_at,'unixepoch') AS at, u.username AS sender, r.text, r.reason
   FROM reports r LEFT JOIN users u ON u.id = r.from_id ORDER BY r.id DESC LIMIT 20;"
```

To remove an account, along with its friendships, messages and invites:

```bash
sudo -u cadenza sqlite3 /var/lib/cadenza/cadenza.db "PRAGMA foreign_keys=ON; DELETE FROM users WHERE username='someone';"
```

## Limits

This setup is a single machine on home upload bandwidth, which is fine for tens of people. Messages are protected by TLS in transit and by disk encryption at rest if you set up LUKS. They aren't end-to-end encrypted, so whoever runs the server can read them. Tell your friends that.
