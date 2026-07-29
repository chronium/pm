# PM Agent Host

`pm-agent-host` is the Linux execution plane for remote PM agent runs. It provides durable run state, authenticated HTTPS pairing, capability discovery, idempotent submission, replayable events, bounded scheduling, immutable Git workspaces, isolated Codex execution, fresh-container validation, retained evidence, restart recovery, and cleanup through hardened rootless Podman.

The workspace uses Node 26 and TypeScript 7. Install only through Socket:

```sh
socket npm install
npm run validate
```

Tests require OpenSSL to generate temporary loopback certificates.

## Configuration

The host uses CLI options with matching `PM_AGENT_HOST_*` environment variables. It fails closed if `serve` is missing its listen address, certificate, key, capability manifest, repository policy, or dedicated Codex authentication file.

| Option                  | Environment                           | Default                         |
| ----------------------- | ------------------------------------- | ------------------------------- |
| `--data-root`           | `PM_AGENT_HOST_DATA_ROOT`             | `/var/lib/pm-runner`            |
| `--max-concurrency`     | `PM_AGENT_HOST_MAX_CONCURRENCY`       | `1`                             |
| `--queue-capacity`      | `PM_AGENT_HOST_QUEUE_CAPACITY`        | `32`                            |
| `--retention-days`      | `PM_AGENT_HOST_RETENTION_DAYS`        | `30`; `0` disables pruning      |
| `--min-free-disk-bytes` | `PM_AGENT_HOST_MIN_FREE_DISK_BYTES`   | `5368709120`                    |
| `--listen-address`      | `PM_AGENT_HOST_LISTEN_ADDRESS`        | required for `serve`            |
| `--port`                | `PM_AGENT_HOST_PORT`                  | `7443`                          |
| `--tls-cert`            | `PM_AGENT_HOST_TLS_CERT_PATH`         | required for `serve` and `pair` |
| `--tls-key`             | `PM_AGENT_HOST_TLS_KEY_PATH`          | required for `serve`            |
| `--capabilities`        | `PM_AGENT_HOST_CAPABILITIES_PATH`     | required for `serve`            |
| `--repositories`        | `PM_AGENT_HOST_REPOSITORIES_PATH`     | required for `serve`            |
| `--codex-auth`          | `PM_AGENT_HOST_CODEX_AUTH_PATH`       | required for `serve`            |
| `--release-manifest`    | `PM_AGENT_HOST_RELEASE_MANIFEST_PATH` | optional development fallback   |

The listen address must be an explicit non-wildcard IP. The operator provides the certificate and an owner-only private key, normally for a Tailscale or trusted private-network route. The server permits TLS 1.2 and 1.3 so the .NET control plane remains compatible across macOS and Linux TLS stacks. The capability manifest is host-owned and changes require restart; `capabilities.example.json` shows the validated shape.

`repositories.json` is an owner-only exact allowlist. V1 accepts only explicit HTTPS, SSH URL, or `git@host:path` remotes; local paths, file transports, helpers, local hosts, and URL credentials are rejected:

```json
{
  "repositories": [{ "remote": "https://github.com/chronium/pm.git" }]
}
```

`--codex-auth` points to a dedicated owner-only `auth.json`, not a normal interactive Codex home. The runner copies its exact bytes into each fresh agent container home and never copies refreshed state back.

## Pairing and serving

Initialize a one-use ten-minute pairing window locally:

```sh
node dist/src/main.js pair \
  --data-root /var/lib/pm-runner \
  --tls-cert /etc/pm-runner/tls.crt
```

The command prints the stable runner ID, pairing code, expiry, and TLS SHA-256 fingerprint once. Start the service, then verify all displayed identity details while pairing from PM:

```sh
pm runner pair https://100.64.0.2:7443 \
  --runner-id <runner-id> \
  --fingerprint sha256:<certificate-fingerprint>
```

PM prompts for the pairing code without placing it in the process argument list. Development
checkouts may start the service with:

```sh
node dist/src/main.js serve \
  --data-root /var/lib/pm-runner \
  --listen-address 100.64.0.2 \
  --tls-cert /etc/pm-runner/tls.crt \
  --tls-key /etc/pm-runner/tls.key \
  --capabilities /etc/pm-runner/capabilities.json \
  --repositories /etc/pm-runner/repositories.json \
  --codex-auth /etc/pm-runner/codex-auth.json
```

Packaged installations use `pm-agent-host serve` through the systemd user service. Run
`pm-agent-host doctor` before starting it; `doctor --json` provides machine-readable checks without
mutating runner state. `pm-agent-host version --json` reports the installed package, source
revision, protocol, and immutable worker-image digest.

Use `revoke-client --data-root /var/lib/pm-runner` for local recovery when the paired PM identity is unavailable. Normal rotation and revocation are authenticated HTTPS operations.

Use `pm runner list`, `pm runner status <runner-id>`, `pm runner rotate <runner-id>`, and `pm runner revoke <runner-id>` from the PM installation. PM stores the certificate pin and runner-scoped signing credential in its private OS user configuration, never in a project `.pm` directory.

## PM control-plane API

`pm web --api` exposes the paired-runner lifecycle under `/api/v1/runners` and the project-scoped run lifecycle under `/api/v1/runs`. A run begins with `POST /api/v1/runs/preflight`, which validates the clean published Git base, exact committed task revision, runner health, capacity, and explicit provider/model/effort/profile selection. A ready response persists the immutable request outside `.pm` and returns its strong ETag. `POST /api/v1/runs/{runId}/start` requires that ETag in `If-Match`; PM repeats the checks and refuses stale drafts before contacting the runner.

Run inspection, active-run listing, event replay, SSE streaming, cancellation, and artifact metadata remain PM API operations. The Angular client never receives runner signing credentials or communicates with the runner directly. Run state is private, non-authoritative local cache data; tasks and wiki remain public repository artifacts, and a run never marks its task complete.

The environment-gated .NET smoke exercises this API against a real paired runner:

```sh
PM_AGENT_RUN_API_SMOKE=1 \
PM_AGENT_RUN_API_SMOKE_PROJECT_ROOT=/path/to/clean/smoke-checkout \
PM_AGENT_RUN_API_SMOKE_RUNNER=<runner-id> \
PM_AGENT_RUN_API_SMOKE_TASK=<task-id> \
PM_AGENT_RUN_API_SMOKE_PROFILE=<profile-id> \
PM_AGENT_RUN_API_SMOKE_PROVIDER=codex \
PM_AGENT_RUN_API_SMOKE_MODEL=<model-id> \
PM_AGENT_RUN_API_SMOKE_EFFORT=medium \
dotnet test PM.Tests/PM.Tests.csproj -m:1 --no-restore \
  --filter FullyQualifiedName~AgentRunApiSmokeTests
```

## Run transport

The authenticated HTTPS surface accepts immutable run requests, inspects and pages active runs, journals cancellation, lists artifact metadata, pages event history, and streams replayable SSE events. See `contracts/agent-runs/v1/transport.md` for endpoints, status codes, signed cursors, event namespaces, and reconnect behavior.

After durable acceptance, the runner owns execution independently of client or SSE connectivity. It serializes fetches per exact allowlisted remote, verifies the requested commit is still reachable, creates a detached credential-free checkout, verifies the assigned task's exact-byte revision, rejects Git submodules and LFS, and launches Codex through the scheduler. Validation runs sequentially in a fresh credential-free container. Validation failures are completed runs with failed evidence; agent, runtime, workspace, and collection failures are failed runs. Cancellation remains distinct.

Terminal runs retain bounded artifacts and metadata only. V1 can produce `changes.patch`, `changes-summary.json`, `validation.json`, `agent-response.md`, `run-report.json`, a bounded sanitized `events.jsonl`, and `manifest.json`. Oversized patches are omitted while their changed-path summary remains available. Workspaces, Codex homes, scratch data, and runtime state are removed before the terminal transition; bare mirrors remain reusable.

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

The service reconciles runner-owned containers and transient run directories before requeueing accepted work. A run interrupted after execution begins is failed once and is never silently rerun.

## Codex worker

`pm-agent-worker` is a one-shot internal process intended to run inside an isolated OCI runtime. It accepts one validated request over stdin, starts the pinned Codex SDK, and reserves stdout for bounded JSONL runner events. The trusted runtime supplies its executable, workspace, isolated `CODEX_HOME`, environment allowlist, network policy, and PM MCP command; none of those command paths come from the repository run request.

The worker requires `pm mcp --profile run-worker --task-id <TASK-ID>`, uses `approvalPolicy: never` with `workspace-write`, and fails when PM MCP cannot initialize. It can read project context and append a note only to its assigned task. PM remains authoritative for task completion.

For v1 ChatGPT authentication, the runtime will copy only the dedicated runner's owner-only `auth.json` into a fresh per-run `CODEX_HOME`. Run-local Codex sessions, logs, and refreshed authentication are deleted with the runtime and are never copied back to the host source.

## Worker images

`container/Containerfile.development` is intentionally not a production image. It pins its Node base by digest and pins installed Debian package versions, but exists only to exercise the complete v1 flow while AGENT-0012 owns production image hardening and publication. It contains Git, the built Codex SDK worker, and a self-contained Linux PM CLI/MCP; it contains no repository, runner configuration, or credential.

Build it on the Linux runner with the .NET 10 SDK, Node 26, npm 11 or 12, Socket, and rootless Podman available:

```sh
agent-host/container/build-development-image.sh
```

The script installs locked Node dependencies through `socket npm ci`, restores the explicit `linux-x64` .NET runtime graph, publishes PM self-contained, builds with `--pull=never`, and prints the local immutable image reference to place in the capability manifest. Recompute the profile revision after changing that image reference.

An opt-in smoke test exercises a real Codex turn in a disposable Git clone. It is excluded from normal tests and CI:

```sh
PM_AGENT_HOST_CODEX_SMOKE=1 \
PM_AGENT_HOST_CODEX_SMOKE_AUTH="$HOME/.codex/auth.json" \
PM_AGENT_HOST_PM_COMMAND_JSON='["dotnet","/absolute/path/to/PM.dll"]' \
npm run test:codex-smoke
```

The smoke test creates a synthetic PM task, asks Codex to write one marker file, confirms the task state was not changed, and removes the entire temporary workspace.

The stronger opt-in lifecycle smoke targets the private disposable `chronium/pm-agent-smoke` fixture and exercises the complete runner pipeline. It requires an installed development image and dedicated runner authentication:

```sh
PM_AGENT_HOST_LIFECYCLE_SMOKE=1 \
PM_AGENT_HOST_LIFECYCLE_SMOKE_REMOTE=https://github.com/chronium/pm-agent-smoke.git \
PM_AGENT_HOST_LIFECYCLE_SMOKE_COMMIT=<committed-fixture-sha> \
PM_AGENT_HOST_LIFECYCLE_SMOKE_AUTH="$HOME/.config/pm-agent-host/codex-auth.json" \
PM_AGENT_HOST_LIFECYCLE_SMOKE_IMAGE=localhost/pm-agent-development@sha256:<digest> \
npm run test:lifecycle-smoke
```

It validates that only the requested marker changed, validation passed in a fresh container, retained evidence is complete, and transient workspace, runtime, and Codex-auth state was removed.

`container/Containerfile.production` is the packaged v1 reference image. It adds the pinned .NET 10
SDK used for repository validation, is labeled with the exact source revision, and is distributed as
a local OCI archive by `npm run package:linux`. The release script runs only from a clean Linux x64
checkout, requires Node 26.5.0 and npm 11 or 12, performs every dependency install through Socket, and
records the resulting local image by digest in the generated capability snapshot.

See [OPERATIONS.md](OPERATIONS.md) for verified installation, explicit service activation, health,
upgrades, rollback, backup, credential rotation, incident response, and the v1 trust boundary.

## Persistence and security

`runner.sqlite` stores immutable runs, queue order, cancellation intent, events, artifacts, the optional Codex thread ID, and stable runner identity. Events are sanitized and committed before live subscribers are notified. `credentials.sqlite` separately stores the single authorized public identity, pairing challenge hashes, and expiring request nonces. Both databases live under the owner-only data root and use WAL, full synchronous writes, and versioned schemas.

Routine logs are newline-delimited JSON with whitelisted operational fields. They omit specifications, repository paths, URLs, request paths, pairing codes, certificates, credentials, signatures, nonces, and arbitrary exception messages.

See `contracts/agent-runs/v1/transport.md` for the signed request and endpoint contract.
