# Cadenza Friends: Docker on Raspberry Pi 5

This is the **recommended lightweight deployment** for a Pi running **64-bit Raspberry Pi OS Bookworm** with Docker Engine and Docker Compose installed. It runs **only** the Cadenza Friends ASP.NET Core backend, not the Windows desktop, Rust playback code, or tests. The existing systemd/.NET deployment is documented separately in [DEPLOY-RASPBERRY-PI.md](DEPLOY-RASPBERRY-PI.md).

This phase is **private/local only**. No domain, port forwarding, public tunnel, or PostgreSQL is required. The existing `pi-web` Nginx container on host port 8080 can remain running because Friends uses host port 5080.

## 1. Prepare data storage on the Pi

In a Raspberry Pi SSH terminal (not Windows PowerShell):

```bash
uname -m                 # aarch64
sudo docker compose version
sudo install -d -m 0700 -o 1654 -g 1654 /srv/cadenza/data
sudo install -d -m 0700 -o root -g root /srv/cadenza/backups
```

The `1654` owner matches the unprivileged `app` user of the official .NET 10 ASP.NET Core image. The live database and uploaded music persist at `/srv/cadenza/data` outside the container. Backups live at `/srv/cadenza/backups`; **both locations are currently on the same microSD card**.

If you already deployed Cadenza with systemd, **do not start this stack before migrating that existing database**. Otherwise it will create a separate empty database.

## 2. Build and start

```bash
git clone https://github.com/toomatoe/Cadenza.git
cd Cadenza/server
sudo docker compose up -d --build
sudo docker compose ps
curl --fail http://127.0.0.1:5080/health
```

Expected health response: `ok`. If you already have a checkout, update it instead of cloning again. On the first deployment the build downloads .NET SDK/runtime images and NuGet dependencies, which can take a while on a Pi.

The first start logs a **bootstrap invite code**. Treat it like a password:

```bash
sudo docker compose logs --tail=100 friends
```

After creating your first account in Cadenza, no further bootstrap invite is printed. Don't publish logs that contain invite codes.

Run `sudo docker compose up -d --build` from the `server/` directory after pulling future changes. `restart: unless-stopped` brings Cadenza back after an ordinary reboot or power restoration (assuming the Pi and Docker recover normally).

## 3. Check it from Windows without exposing the API

The Compose service binds only `127.0.0.1:5080` **on the Pi**, not the Pi's network address. Keep this private until public authentication and transport have been reviewed.

Open a **separate** Windows PowerShell terminal, replacing the Pi IP if it has changed:

```powershell
ssh -N -L 5080:127.0.0.1:5080 toomatoe@192.168.1.147
```

Keep the SSH window running; in another Windows terminal:

```powershell
curl.exe http://127.0.0.1:5080/health
```

Expected result: `ok`. Later, the same localhost port can be published through a properly configured HTTPS tunnel (for example, Tailscale Funnel) without opening a router port. **Do not configure public access yet**, and do not send account tokens over plain HTTP to other machines.

## 4. Automatic six-hour SQLite backups

The Cadenza database uses **SQLite WAL mode**, so never copy a live `cadenza.db` using `cp` as a backup. The script uses SQLite's online `.backup` command to make a consistent database snapshot, verifies it with `PRAGMA integrity_check`, and deletes database snapshots older than **14 days**. Backups run at 00:00, 06:00, 12:00, and 18:00 (Pi local time), including one catch-up run after a missed scheduled time when it boots again.

```bash
sudo apt update
sudo apt install -y sqlite3
bash deploy/install-backups.sh
```

That installer registers and starts `cadenza-backup.timer` and makes an immediate test backup.

Inspect it:

```bash
sudo systemctl status cadenza-backup.timer --no-pager
sudo systemctl status cadenza-backup.service --no-pager
sudo ls -lh /srv/cadenza/backups/
sudo journalctl -u cadenza-backup.service --no-pager -n 30
```

To make an extra verified backup now:

```bash
sudo systemctl start cadenza-backup.service
```

**Limitations:** These snapshots protect against accidental database changes and some SQLite corruption, but a completely failed microSD card would destroy both the live database and all local backups. Until off-device backups are enabled, **this is not disaster recovery**. Also, these snapshots cover **messages/accounts/friendships/database records only**. Uploaded files in `/srv/cadenza/data/music` are **not** backed up by this job. Add off-device backup for the database and music directory before depending on shared uploads.

## 5. Restore a database backup

**This overwrites current server database state, so only restore intentionally.** First stop the Cadenza container (in `server/`), and retain a copy of the current state. With the container stopped, SQLite should checkpoint and close normally.

```bash
sudo docker compose down
sudo install -d -m 0700 /srv/cadenza/recovery
sudo cp -a /srv/cadenza/data/cadenza.db* /srv/cadenza/recovery/
sudo ls -lh /srv/cadenza/backups/
```

Choose the timestamped backup you want to restore; then substitute its **actual filename** below:

```bash
sudo cp /srv/cadenza/backups/cadenza-YYYYMMDDTHHMMSSZ.db /srv/cadenza/data/cadenza.db
sudo rm -f /srv/cadenza/data/cadenza.db-wal /srv/cadenza/data/cadenza.db-shm
sudo chown 1654:1654 /srv/cadenza/data/cadenza.db
sudo chmod 0600 /srv/cadenza/data/cadenza.db
sudo docker compose up -d
curl --fail http://127.0.0.1:5080/health
```

Before overwriting any database, verify that you have selected the correct backup and that your recovery copy exists. Newer messages written after the snapshot will not be restored.

## 6. Operations

```bash
sudo docker compose ps
sudo docker compose logs -f friends
sudo docker compose restart friends
sudo docker compose up -d --build    # deploy a newer checkout
df -h /srv/cadenza
```

Data is stored outside the container; `docker compose down` does **not** delete it. **Never use `rm -rf /srv/cadenza/data` to clean up the service.** If you switch to an SSD later, move both the data directory and backup destination to appropriate mounted locations and adjust Compose and the backup script together.

## 7. Connect securely without a domain

Follow **[TAILSCALE.md](TAILSCALE.md)** to install Tailscale on the Pi and Windows, give the server a private HTTPS address with Tailscale Serve, and join your first Cadenza account. This does not require a domain or public router ports.

Do not enable public Funnel access until you have reviewed authentication, rate limits, WebSocket handling, uploaded music storage, and security dependencies, and configured **off-device encrypted backups with a tested restore**. Keep the host binding on loopback even when the HTTPS proxy is active.
