# Cortana Development Stack

This is the persistent, manually operated version of the disposable E2E
fixture. It runs alongside Cortana's caption worker without using its ports or
changing Malcolm.

The deployment lives at `/opt/watch-state-sync-dev`. From Cortana's WSL:

```bash
cd /opt/watch-state-sync-dev
./deploy/cortana/up.sh
./deploy/cortana/status.sh
./deploy/cortana/test-baseline.sh
./deploy/cortana/down.sh
```

State and generated sample media live below `tests/e2e/state` and
`tests/e2e/media`. `down.sh` preserves both.

## Accounts

The first `up.sh` run creates the Jellyfin user configured in
`deploy/cortana/.env`.

Plex does not support local users. Open Plex Web, sign in with a disposable Plex
account, and claim the server. You may instead set a short-lived `PLEX_CLAIM`
before the first run. Once claimed, set `PLEX_TOKEN` for that account in `.env`
so the sync plugin can make authenticated calls.

`test-baseline.sh` deliberately creates opposite watched states for the sample
movie and episode, runs the plugin's required dry run, explicitly applies that
preview, verifies both writes, and proves a second dry run is idempotent.

## Network access

The containers publish WSL ports `18096` and `32410`. Windows normally exposes
those through localhost. `scripts/deploy-cortana.sh` also configures LAN
forwarding automatically.

If WSL's internal IP changes later, copy the forwarding script to a Windows
path and run it from an elevated Windows PowerShell prompt:

```powershell
$source = "\\wsl$\Ubuntu-24.04\opt\watch-state-sync-dev\deploy\cortana\refresh-lan-forwarding.ps1"
$script = "$env:TEMP\watch-state-sync-refresh-lan.ps1"
Copy-Item $source $script -Force
powershell.exe -ExecutionPolicy Bypass -File $script
```

The forwarding script limits the Windows firewall rules to the local subnet.
Copying it to Windows before execution avoids asking `wsl.exe` to re-enter the
same distro while PowerShell still has the script open through `\\wsl$`.
