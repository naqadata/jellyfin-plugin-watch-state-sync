# Jellyfin Plugin - Watch State Sync

Jellyfin plugin for migrating and synchronizing watched state with Plex.

## Planned delivery

1. Plex-authoritative baseline migration into Jellyfin.
2. Simple completed-view synchronization using Plex `lastViewedAt` and
   Jellyfin `LastPlayedDate`.
3. Advanced manual-state conflicts and resume progress only if later needed.

Movies and episodes are matched by exact canonical media path first because the
two servers normally index the same files. Provider IDs are fallback and
validation.

## Current state

The repository contains:

- a Jellyfin `10.11.6`-compatible plugin;
- per-user Plex token and username mapping configuration;
- mandatory dry-run and explicit-apply baseline controls;
- conservative unique exact-path matching;
- Plex-authoritative watched and unwatched writes through Jellyfin's API;
- stale-preview rejection, durable audits, and token-free baseline watermarks;
- native .NET unit tests;
- isolated Jellyfin and Plex Docker fixtures;
- deterministic shared movie and episode media generation.

The manual Plex-to-Jellyfin baseline migration is implemented. Completed-view
live sync remains deferred.

## Build and test

```bash
dotnet build Jellyfin.Plugin.WatchStateSync.csproj
./scripts/test.sh
```

Run the Docker fixture smoke test:

```bash
./scripts/test.sh --e2e
```

On the claimed Cortana stack, exercise the real manual migration path:

```bash
./deploy/cortana/test-baseline.sh
```

See [`tests/e2e/README.md`](tests/e2e/README.md) for authentication and lifecycle
details.

## Persistent Cortana development stack

The disposable fixture also has a persistent WSL deployment wrapper for
Cortana. It creates sample media and a Jellyfin development user, preserves
both server databases between runs, and can be exposed to the local network for
testing from real clients.

Deploy the current checkout from macOS:

```bash
./scripts/deploy-cortana.sh
```

After deployment, manage it from Cortana's Ubuntu WSL environment:

```bash
cd /opt/watch-state-sync-dev
./deploy/cortana/up.sh
./deploy/cortana/status.sh
./deploy/cortana/test-baseline.sh
./deploy/cortana/down.sh
```

See [`deploy/cortana/README.md`](deploy/cortana/README.md) for Plex account
claiming and LAN forwarding details.

## Why .NET tests instead of Bazel

The plugin targets Jellyfin's native C#/.NET plugin interfaces. `dotnet test`
provides direct project references, debugger integration, NuGet dependency
resolution, and standard coverage support without a second build graph.

The server fixture lifecycle remains explicit Docker Compose. A Bazel wrapper
can be added later if this repository joins a larger cross-language test graph,
but Bazel is not required to make the tests hermetic or repeatable today.
