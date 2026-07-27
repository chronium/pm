# PM Agent Host

`pm-agent-host` is the Linux execution-plane foundation for remote PM agent runs. It currently provides durable run state, authenticated HTTPS pairing and capability discovery, bounded scheduling seams, restart recovery, and retention. Codex, Docker execution, run submission, and event streaming are intentionally deferred.

The workspace uses Node 26 and TypeScript 7. Install only through Socket:

```sh
socket npm install
npm run validate
```

Tests require OpenSSL to generate temporary loopback certificates.

## Configuration

The host uses CLI options with matching `PM_AGENT_HOST_*` environment variables. It fails closed if `serve` is missing its listen address, certificate, key, or capability manifest.

| Option              | Environment                       | Default                         |
| ------------------- | --------------------------------- | ------------------------------- |
| `--data-root`       | `PM_AGENT_HOST_DATA_ROOT`         | `/var/lib/pm-runner`            |
| `--max-concurrency` | `PM_AGENT_HOST_MAX_CONCURRENCY`   | `1`                             |
| `--queue-capacity`  | `PM_AGENT_HOST_QUEUE_CAPACITY`    | `32`                            |
| `--retention-days`  | `PM_AGENT_HOST_RETENTION_DAYS`    | `30`; `0` disables pruning      |
| `--listen-address`  | `PM_AGENT_HOST_LISTEN_ADDRESS`    | required for `serve`            |
| `--port`            | `PM_AGENT_HOST_PORT`              | `7443`                          |
| `--tls-cert`        | `PM_AGENT_HOST_TLS_CERT_PATH`     | required for `serve` and `pair` |
| `--tls-key`         | `PM_AGENT_HOST_TLS_KEY_PATH`      | required for `serve`            |
| `--capabilities`    | `PM_AGENT_HOST_CAPABILITIES_PATH` | required for `serve`            |

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

## Persistence and security

`runner.sqlite` stores immutable runs, queue order, events, artifacts, and stable runner identity. `credentials.sqlite` separately stores the single authorized public identity, pairing challenge hashes, and expiring request nonces. Both databases live under the owner-only data root and use WAL, full synchronous writes, and versioned schemas.

Routine logs are newline-delimited JSON with whitelisted operational fields. They omit specifications, repository paths, URLs, request paths, pairing codes, certificates, credentials, signatures, nonces, and arbitrary exception messages.

See `contracts/agent-runs/v1/transport.md` for the signed request and endpoint contract.
