# Cortana Development Stack

This is the persistent, manually operated version of the disposable E2E
fixture. It runs alongside Cortana's caption worker without using its ports or
changing Malcolm.

The deployment lives at `/opt/watch-state-sync-dev`. From Cortana's WSL:

```bash
cd /opt/watch-state-sync-dev
./deploy/cortana/up.sh
./deploy/cortana/status.sh
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

## Network access

The containers publish WSL ports `18096` and `32410`. Windows normally exposes
those through localhost. To reach the stack from another device on the LAN, run
the following from an elevated Windows PowerShell prompt after WSL starts:

```powershell
powershell.exe -ExecutionPolicy Bypass -File `
  "\\wsl$\Ubuntu-24.04\opt\watch-state-sync-dev\deploy\cortana\refresh-lan-forwarding.ps1"
```

The forwarding script limits the Windows firewall rules to the local subnet.
Rerun it if WSL's internal IP address changes.
