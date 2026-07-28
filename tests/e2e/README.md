# Docker E2E Fixture

This fixture starts disposable Jellyfin and Plex servers without touching
Malcolm. Both containers mount the same generated files at `/media`, matching
the production shared-path model.

Pinned server versions:

- Jellyfin `10.11.6`
- Plex `1.43.1.10611-1e34174b1`

## Quick start

```bash
./tests/e2e/up.sh
./tests/e2e/smoke.sh
./tests/e2e/down.sh
```

The first run downloads both server images and generates a two-second fixture
movie and episode. Persistent state and media are ignored by Git.

Jellyfin is available at <http://127.0.0.1:18096>. Plex is available at
<http://127.0.0.1:32410/web>.

## Plex authentication tiers

The default fixture starts an unclaimed Plex server and restricts the published
port to loopback. `ALLOWED_NETWORKS=0.0.0.0/0` applies only inside this isolated
fixture and permits anonymous health/catalog setup.

Full watched-state E2E needs a disposable claimed Plex server:

1. Copy `.env.example` to `.env`.
2. Obtain a short-lived claim token from <https://www.plex.tv/claim>.
3. Set `PLEX_CLAIM` before the first start of a fresh fixture.
4. Set `PLEX_TOKEN` to the disposable test user's server token.

Never use or commit a production Plex token. `.env` and fixture state are
ignored.

The current smoke test proves:

- both pinned servers start;
- both expose their public identity endpoints;
- the plugin binary loads into the fixture Jellyfin process;
- the same generated media path is mounted into both servers;
- movie and show libraries can be created through each server API.

User creation, watched-state mutation, and migration assertions will be added as
the corresponding plugin adapters are implemented.

On the claimed Cortana stack, exercise the complete timestamp-only worker in
both directions:

```bash
./deploy/cortana/test-live-sync.sh
```

## Lifecycle

`down.sh` preserves fixture state for fast restarts:

```bash
./tests/e2e/down.sh
```

To remove the Compose containers and named resources, pass normal Compose
options:

```bash
./tests/e2e/down.sh --volumes
```

The bind-mounted `state/` directory remains available for inspection and can be
removed manually when a completely fresh server is required.
