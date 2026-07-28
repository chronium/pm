# PM Agent Host

`pm-agent-host` is the Linux execution-plane foundation for remote PM agent runs. It provides durable run state, authenticated HTTPS pairing and capability discovery, idempotent run submission, replayable event streaming, bounded scheduling seams, restart recovery, retention, a container-ready Codex SDK worker, and a hardened rootless Podman runtime adapter.

The workspace uses Node 26 and TypeScript 7. Install only through Socket:

```sh
socket npm install
npm run validate
```

Tests require OpenSSL to generate temporary loopback certificates.

## Configuration

The host uses CLI options with matching `PM_AGENT_HOST_*` environment variables. It fails closed if `serve` is missing its listen address, certificate, key, or capability manifest.

| Option                  | Environment                         | Default                         |
| ----------------------- | ----------------------------------- | ------------------------------- |
| `--data-root`           | `PM_AGENT_HOST_DATA_ROOT`           | `/var/lib/pm-runner`            |
| `--max-concurrency`     | `PM_AGENT_HOST_MAX_CONCURRENCY`     | `1`                             |
| `--queue-capacity`      | `PM_AGENT_HOST_QUEUE_CAPACITY`      | `32`                            |
| `--retention-days`      | `PM_AGENT_HOST_RETENTION_DAYS`      | `30`; `0` disables pruning      |
| `--min-free-disk-bytes` | `PM_AGENT_HOST_MIN_FREE_DISK_BYTES` | `5368709120`                    |
| `--listen-address`      | `PM_AGENT_HOST_LISTEN_ADDRESS`      | required for `serve`            |
| `--port`                | `PM_AGENT_HOST_PORT`                | `7443`                          |
| `--tls-cert`            | `PM_AGENT_HOST_TLS_CERT_PATH`       | required for `serve` and `pair` |
| `--tls-key`             | `PM_AGENT_HOST_TLS_KEY_PATH`        | required for `serve`            |
| `--capabilities`        | `PM_AGENT_HOST_CAPABILITIES_PATH`   | required for `serve`            |

The listen address must be an explicit non-wildcard IP. The operator provides the certificate and an owner-only private key, normally for a Tailscale or trusted private-network route. The capability manifest is host-owned and changes require restart; `capabilities.example.json` shows the validated shape.

## Pairing and serving

Initialize a one-use ten-minute pairing window locally:

```sh
node dist/src/main.js pair \
  --data-root /var/lib/pm-runner \
  --tls-cert /etc/pm-runner/tls.crt
```

The command prints the pairing code and TLS SHA-256 fingerprint once. Verify both in PM. Start the service with:

```sh
node dist/src/main.js serve \
  --data-root /var/lib/pm-runner \
  --listen-address 100.64.0.2 \
  --tls-cert /etc/pm-runner/tls.crt \
  --tls-key /etc/pm-runner/tls.key \
  --capabilities /etc/pm-runner/capabilities.json
```

Use `revoke-client --data-root /var/lib/pm-runner` for local recovery when the paired PM identity is unavailable. Normal rotation and revocation are authenticated HTTPS operations.

## Run transport

The authenticated HTTPS surface accepts immutable run requests, inspects and pages active runs, journals cancellation, lists artifact metadata, pages event history, and streams replayable SSE events. See `contracts/agent-runs/v1/transport.md` for endpoints, status codes, signed cursors, event namespaces, and reconnect behavior.

The current executable wires a queue-only execution controller. Accepted commands persist and survive disconnects or restart, but remain queued until immutable workspace preparation composes the Podman runtime, Codex driver, and existing scheduler. Artifact endpoints expose metadata only in this slice.

## Rootless Podman runtime

Runtime profiles carry an immutable container policy alongside the image digest and resource limits. The policy fixes the workspace, isolated Codex home, temporary filesystem, safe environment names, network mode, and hardening baseline. Host paths are derived exclusively from the runner data root; they are never accepted from a run request or repository.

The adapter requires Linux, rootless Podman 5 or newer, cgroup v2 with the systemd manager, seccomp, subordinate UID/GID mappings, and every configured image already installed by digest. It uses the Podman CLI from the trusted host and never mounts the Podman or Docker socket into a worker. SELinux runtime labeling is intentionally deferred; a host with SELinux enabled fails readiness instead of silently disabling labels.

V1 network policies are explicit: `offline` has no network, while `open` uses a private rootless namespace with unrestricted outbound access. Hostname-restricted egress requires a separate proxy boundary and is not represented by a misleading profile name. Writable workspace and Codex-home trees are watched against the profile disk budget, while tmpfs is hard-sized and the host maintains a configurable free-space reserve.

The opt-in integration suite consumes an administrator-installed image and never pulls:

```sh
PM_AGENT_HOST_TEST_IMAGE='docker.io/library/alpine@sha256:<digest>' npm test
```

`systemd/pm-agent-host.service` is a rootless user-service template. Place an owner-only environment file at `~/.config/pm-agent-host/host.env`, verify the unit, and enable it only after installing a built workspace at the path named by the unit:

```sh
systemd-analyze --user verify systemd/pm-agent-host.service
systemctl --user daemon-reload
systemctl --user enable --now pm-agent-host.service
```

The service remains queue-only until AGENT-0008 supplies immutable workspaces and per-run credential material.

## Codex worker

`pm-agent-worker` is a one-shot internal process intended to run inside an isolated OCI runtime. It accepts one validated request over stdin, starts the pinned Codex SDK, and reserves stdout for bounded JSONL runner events. The trusted runtime supplies its executable, workspace, isolated `CODEX_HOME`, environment allowlist, network policy, and PM MCP command; none of those command paths come from the repository run request.

The worker requires `pm mcp --profile run-worker --task-id <TASK-ID>`, uses `approvalPolicy: never` with `workspace-write`, and fails when PM MCP cannot initialize. It can read project context and append a note only to its assigned task. PM remains authoritative for task completion.

For v1 ChatGPT authentication, the runtime will copy only the dedicated runner's owner-only `auth.json` into a fresh per-run `CODEX_HOME`. Run-local Codex sessions, logs, and refreshed authentication are deleted with the runtime and are never copied back to the host source.

An opt-in smoke test exercises a real Codex turn in a disposable Git clone. It is excluded from normal tests and CI:

```sh
PM_AGENT_HOST_CODEX_SMOKE=1 \
PM_AGENT_HOST_CODEX_SMOKE_AUTH="$HOME/.codex/auth.json" \
PM_AGENT_HOST_PM_COMMAND_JSON='["dotnet","/absolute/path/to/PM.dll"]' \
npm run test:codex-smoke
```

The smoke test creates a synthetic PM task, asks Codex to write one marker file, confirms the task state was not changed, and removes the entire temporary workspace.

## Persistence and security

`runner.sqlite` stores immutable runs, queue order, cancellation intent, events, artifacts, the optional Codex thread ID, and stable runner identity. Events are sanitized and committed before live subscribers are notified. `credentials.sqlite` separately stores the single authorized public identity, pairing challenge hashes, and expiring request nonces. Both databases live under the owner-only data root and use WAL, full synchronous writes, and versioned schemas.

Routine logs are newline-delimited JSON with whitelisted operational fields. They omit specifications, repository paths, URLs, request paths, pairing codes, certificates, credentials, signatures, nonces, and arbitrary exception messages.

See `contracts/agent-runs/v1/transport.md` for the signed request and endpoint contract.
