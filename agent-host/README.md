# PM Agent Host

`pm-agent-host` is the persistent Linux execution-plane service for PM agent runs. This initial workspace provides protocol 1.0 validation, durable SQLite run state, replayable events, bounded scheduling primitives, restart recovery, retention, and driver interfaces.

It does not yet listen on a network interface or execute Codex, Docker, Git, or validation commands. The production entry point recovers state, prunes expired terminal runs, reports readiness, and leaves queued work untouched until later tasks install real transports and drivers.

## Development

Use Node 26 from the repository `.node-version`. Install the exact locked development toolchain through Socket:

```sh
cd agent-host
socket npm install
npm run format
npm run check
npm test
npm run build
```

`npm run validate` runs the non-mutating formatting check, strict TypeScript check, build, and Node test suite. CI installs the lockfile with `socket npm ci` before running it.

Run the idle host against a disposable absolute data root:

```sh
npm run build
npm start -- --data-root /tmp/pm-agent-host
```

The service accepts these CLI settings, with matching `PM_AGENT_HOST_*` environment fallbacks and CLI precedence:

| CLI                 | Environment                     | Default              |
| ------------------- | ------------------------------- | -------------------- |
| `--data-root`       | `PM_AGENT_HOST_DATA_ROOT`       | `/var/lib/pm-runner` |
| `--max-concurrency` | `PM_AGENT_HOST_MAX_CONCURRENCY` | `1`                  |
| `--queue-capacity`  | `PM_AGENT_HOST_QUEUE_CAPACITY`  | `32`                 |
| `--retention-days`  | `PM_AGENT_HOST_RETENTION_DAYS`  | `30`                 |

The data root must be absolute and host-owned. Repository configuration never changes these settings. A retention value of `0` disables automatic pruning.

## Persistence

The host stores `runner.sqlite` beneath the data root using WAL, foreign keys, full synchronous writes, and versioned migrations. It persists immutable run specifications, queue order, current state, monotonically sequenced events, artifact metadata and relative locations, and a stable generated runner ID.

Acceptance, initial events, and queue insertion are one transaction. Restart recovery preserves queued work and marks interrupted active work failed with a durable `runner_restarted` event. Terminal retention deletes only verified run-owned paths beneath the data root before removing database records.

Routine logs are newline-delimited JSON containing only whitelisted operational fields. Specifications, repository paths and remotes, credentials, arbitrary exception messages, and artifact contents are not logged.
