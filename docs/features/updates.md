# Updating Cadenza

Open **Settings → Updates → Check for updates**. If a newer version exists, choose
**Download and install**. Cadenza downloads the Windows installer, verifies its SHA-256
against the release checksum, then closes and opens the normal setup wizard. Finish
setup and select Open Cadenza. The existing Inno Setup AppId preserves the installation
location. Settings and Credential Manager sign-ins remain; the session queue does not.
A portable copy uses the same installer and becomes an installed copy in the default
location. It does not overwrite the portable directory.

Preview releases are opt-in (initially checked for preview/development builds).
There are no startup checks, idle timers, extra packages, or download-before-consent.
Cancel stops the check/download. Closing the window cancels outstanding work. Failed,
truncated or checksum-mismatched downloads never launch. Downloads stream through a
64 KiB buffer, are capped at 300 MiB, time out after five minutes, and keep one installer
plus at most one partial file in the app's updates directory. GitHub API metadata is
bounded to 2 MiB; only the newest 30 published entries are considered.

## Release process

Use the existing version-tag workflow. A published release must contain
`Cadenza-Setup.exe` and `Cadenza-Setup.exe.sha256`. The checksum line contains the
SHA-256, whitespace and the exact installer filename. The assembly version is set
from the tag by build.ps1. Untagged builds use 0.0.0-dev. Stable users do not receive
preview releases, equal versions or downgrades. Numeric preview identifiers compare
numerically (beta.10 follows beta.9).

Only HTTPS assets under this repository's exact release/tag paths are accepted.
GitHub may redirect asset downloads to its CDN. Checksums detect corruption; they
are not independent publisher signatures. Windows trust prompts are retained.

Users need to install a release containing this feature once. Older installed
versions cannot acquire an updater they do not already contain.

## Validation

Core tests cover release ordering, channels, versions, verified download, checksum
failure preserving an existing installer, foreign hosts, and cancellation. On Windows,
install an older version containing the updater, publish a newer test release, check
and cancel a download, retry and complete setup, then verify the new version, sign-in,
settings and playback. Check offline/rate-limited GitHub, missing assets, and portable
migration. UI Automation verifies that the update controls are present; it must not
install software during tests. Live installation/relaunch requires a Windows session.
