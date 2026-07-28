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

- a Jellyfin `10.11.6`-compatible plugin scaffold;
- initial connection configuration;
- a testable canonical-path matcher;
- native .NET unit tests;
- isolated Jellyfin and Plex Docker fixtures;
- deterministic shared movie and episode media generation.

Synchronization writes are not implemented yet.

## Build and test

```bash
dotnet build Jellyfin.Plugin.WatchStateSync.csproj
./scripts/test.sh
```

Run the Docker fixture smoke test:

```bash
./scripts/test.sh --e2e
```

See [`tests/e2e/README.md`](tests/e2e/README.md) for authentication and lifecycle
details.

## Why .NET tests instead of Bazel

The plugin targets Jellyfin's native C#/.NET plugin interfaces. `dotnet test`
provides direct project references, debugger integration, NuGet dependency
resolution, and standard coverage support without a second build graph.

The server fixture lifecycle remains explicit Docker Compose. A Bazel wrapper
can be added later if this repository joins a larger cross-language test graph,
but Bazel is not required to make the tests hermetic or repeatable today.
