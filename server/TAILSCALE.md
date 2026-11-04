# Cadenza Friends: private HTTPS with Tailscale (no domain needed)

The Raspberry Pi runs Cadenza Friends in Docker and publishes it **only** to `127.0.0.1:5080` on the host. Tailscale can provide an HTTPS hostname ending in `.ts.net` without port-forwarding or purchasing a domain. **Start with Tailscale Serve**, which is only accessible inside your tailnet. Do not use Funnel (public internet) until you are ready to operate a public messaging service.

Prerequisites: [DEPLOY-DOCKER-PI.md](DEPLOY-DOCKER-PI.md) completed, `curl -fsS http://127.0.0.1:5080/health` prints `ok`, and the six-hour backup timer is active. Existing snapshots live on the **same microSD card**, and uploaded music is not backed up.

## 1. Install Tailscale on the Raspberry Pi

In the Raspberry Pi terminal, add the **official Raspberry Pi OS Bookworm** package source and install the daemon:

```bash
sudo mkdir -p --mode=0755 /usr/share/keyrings
curl -fsSL https://pkgs.tailscale.com/stable/raspbian/bookworm.noarmor.gpg | sudo tee /usr/share/keyrings/tailscale-archive-keyring.gpg >/dev/null
curl -fsSL https://pkgs.tailscale.com/stable/raspbian/bookworm.tailscale-keyring.list | sudo tee /etc/apt/sources.list.d/tailscale.list
sudo apt update
sudo apt install -y tailscale
sudo systemctl enable --now tailscaled
sudo tailscale up
```

The last command prints a sign-in URL. Open it on your Windows laptop and approve the Pi in your Tailscale account. Don't paste the authentication URL into chat or public logs. Confirm:

```bash
tailscale status
curl -fsS http://127.0.0.1:5080/health
```

Installing Tailscale does **not** expose Cadenza to the public internet.

## 2. Connect Windows to the same tailnet

Install the official [Tailscale Windows client](https://tailscale.com/download/windows), sign in to the **same** tailnet as the Pi, and wait for both devices to appear in the Tailscale admin console. You don't need to change your router's firewall settings.

## 3. Private HTTPS reverse proxy

On the **Pi**, enable Tailscale Serve in the background:

```bash
sudo tailscale serve --bg 5080
sudo tailscale serve status
```

Tailscale may ask you to enable HTTPS certificates / MagicDNS through its web admin interface. Follow that interactive confirmation. `serve status` should display an `https://...ts.net` URL; use your **actual URL**, not an invented hostname.

From Windows, while signed into the same tailnet, test:

```powershell
curl.exe https://YOUR-PI-NAME.YOUR-TAILNET.ts.net/health
```

Expected response: `ok`. You can then open Cadenza's **Friends → Join Cadenza Friends**, enter the private HTTPS address in **Server address**, use the **first-account invite** from the Pi's server logs, and choose your account name. Keep the invite code private. The desktop app stores its access token in Windows Credential Manager. Be aware that the first invite gives its holder the first account; make sure you claim it before wider access.

Only devices joined to your tailnet can reach the service through **Serve**. Other friends would need to be permitted on your tailnet to connect during this private testing stage.

## 4. Public access later: Funnel

When ready for users who **do not** have Tailscale, [Funnel](https://tailscale.com/docs/features/tailscale-funnel) exposes a TLS-secured `.ts.net` hostname to the internet. It does not require a domain purchase or opening router ports. Do **not** run the public Funnel command yet: unauthenticated endpoints (such as `/health` and the invite join endpoint), music upload limits, WebSocket authentication, abuse throttling, platform dependencies, and off-device recovery all need review first.

**Before public launch:** update to a server release that has passed CI; create your first account; configure and test encrypted off-device backups (both SQLite database and music uploads); review/patch vulnerable dependencies; test WebSockets through the tunnel; keep SSH inaccessible from the public internet. SQLite snapshots on one microSD are not disaster recovery.

When those are complete, the documented CLI flow is `sudo tailscale funnel --bg 5080`, then `sudo tailscale funnel status`. **This replaces the private Serve HTTPS endpoint on the same port with one open to everyone on the internet.** If you need to return to private mode, run `sudo tailscale serve --bg 5080` and verify the active status. Don't assume a public endpoint is private solely because it has an uncommon hostname.

## Diagnostics

```bash
sudo systemctl status tailscaled --no-pager
tailscale status
sudo tailscale serve status
sudo tailscale funnel status
sudo docker compose ps
curl -v http://127.0.0.1:5080/health
```

References: [Tailscale Linux installation](https://tailscale.com/docs/install/linux), [Serve](https://tailscale.com/docs/features/tailscale-serve), [Funnel](https://tailscale.com/docs/features/tailscale-funnel).
