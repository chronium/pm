# PM Agent Host Operations

This guide operates one trusted, personal Linux x64 runner. The runner executes only exact,
committed revisions from explicitly allowlisted repositories. It is not a multi-tenant service and
does not make untrusted repositories safe to execute.

## Host prerequisites

- A dedicated unprivileged user with systemd user lingering enabled.
- Linux x64, Node 26.5.0, npm 11 or 12, rootless Podman 5 or newer, cgroup v2, seccomp, Git, OpenSSL,
  SHA-256 utilities, and the .NET 10 SDK for building the reference image.
- Socket CLI for every npm dependency installation. The release build refuses to continue without
  it.
- A Tailscale address or another explicit private IP. Wildcard listeners are rejected.
- A dedicated Codex `auth.json`. Do not reuse a general-purpose home directory inside runs.

SELinux runtime labeling, restricted-hostname egress, GPU access, GUI execution, and hostile
multi-tenant workloads are outside v1.

## Build and verify

Build from a clean committed checkout on the Linux host:

```sh
cd agent-host
socket npm ci
npm run package:linux
cd ../artifacts/agent-host
./install.sh verify .
```

The release gate runs formatting, static checks, and tests before creating artifacts. The output
contains a versioned host archive, an OCI worker-image archive, generated capabilities,
`release-info.json`, `release-manifest.json`, and `SHA256SUMS`. The manifest records the exact Git
revision and immutable worker-image digest. V1 checksums detect accidental or unauthorized local
modification; public signatures and provenance are deferred to the release-distribution task.

## Install and configure

Installation is entirely user-scoped and never invokes `sudo`:

```sh
./install.sh install .
./install.sh configure \
  100.64.0.2 \
  https://github.com/chronium/pm-agent-smoke.git \
  "$HOME/.config/pm-agent-host/codex-auth-source.json"
```

The installer creates:

- `~/.local/share/pm-agent-host/releases/<version>-<revision>/` for immutable host releases.
- `~/.local/share/pm-agent-host/current` and `previous` atomic symlinks.
- `~/.local/share/pm-runner/` for databases, mirrors, runs, and retained artifacts.
- `~/.config/pm-agent-host/` for TLS, capabilities, repository policy, Codex authentication, and
  the systemd environment.
- `~/.config/systemd/user/pm-agent-host.service` as an explicitly managed user service.

Configuration creates an owner-only self-signed certificate for the exact listen IP. Replacing that
certificate requires pairing again because PM pins its fingerprint. Review the generated files,
then run:

```sh
~/.local/share/pm-agent-host/current/bin/pm-agent-host doctor
systemd-analyze --user verify ~/.config/systemd/user/pm-agent-host.service
~/.local/share/pm-agent-host/current/bin/pm-agent-host pair
systemctl --user enable --now pm-agent-host.service
journalctl --user -u pm-agent-host.service -f
```

Pair from the Mac with `pm runner pair`, verifying the displayed runner ID and TLS fingerprint.
Pairing codes are one-use and expire after ten minutes.

The user unit deliberately avoids systemd namespace and syscall restrictions such as
`NoNewPrivileges`, `PrivateTmp`, `LockPersonality`, and `RestrictAddressFamilies`. They prevent
rootless Podman from using the setuid `newuidmap` helper or creating its own namespaces. The fixed
worker profile still enables `no-new-privileges` inside every container and drops all capabilities.

## Health and logging

`pm-agent-host doctor` is non-mutating. It verifies the installed release, Linux/Node/runtime
requirements, owner-only files, TLS, manifests, repository policy, immutable OCI image, free-space
reserve, and SQLite integrity. Use `--json` for automation. The systemd service runs doctor before
every start.

The authenticated `/v1/health` response includes package version, source revision, and worker-image
digest. Routine service logs go only to journald and contain whitelisted operational fields. Set
journal retention in the host's journald policy; do not mirror raw agent output into a second log.

Useful commands:

```sh
systemctl --user status pm-agent-host.service
journalctl --user -u pm-agent-host.service --since today
pm runner status <runner-id>
```

## Upgrade and rollback

Verify a new artifact directory before installing it:

```sh
./install.sh verify /path/to/new-release
./install.sh upgrade /path/to/new-release
~/.local/share/pm-agent-host/current/bin/pm-agent-host doctor
systemctl --user restart pm-agent-host.service
```

Upgrade installs beside the active release, records the former target as `previous`, updates the
managed capability snapshot, and loads the new OCI archive. It does not automatically restart a
running service. To roll back:

```sh
~/.local/share/pm-agent-host/install.sh rollback
~/.local/share/pm-agent-host/current/bin/pm-agent-host doctor
systemctl --user restart pm-agent-host.service
```

Accepted and queued runs are recovered after a restart. A run interrupted after execution starts is
failed exactly once and is never silently executed again.

## Backup and restore

Stop the service before copying SQLite state so the database and WAL files remain a consistent set:

```sh
systemctl --user stop pm-agent-host.service
tar -C "$HOME" -czf pm-agent-host-backup.tar.gz \
  .local/share/pm-runner \
  .config/pm-agent-host
systemctl --user start pm-agent-host.service
```

The backup contains private runner credentials, Codex authentication, retained run evidence, and
TLS keys. Encrypt it and treat it as a credential. Restore it only to the dedicated runner user,
restore directories as mode `0700` and private files as `0600`, then run doctor before starting the
service. Reinstall the matching host release and OCI image if they are not present.

Mirrors and expired terminal artifacts may be omitted from space-constrained backups. The runner
identity and paired-client state live in the databases and must be retained to avoid re-pairing.

## Credential rotation and incident response

Normal PM client rotation uses `pm runner rotate`. If the client identity is lost, stop the service,
run `pm-agent-host revoke-client`, and pair a replacement. Replacing TLS material always requires a
new pairing.

If a credential or run may be compromised:

1. Stop `pm-agent-host.service` and leave the worker offline.
2. Revoke the paired PM client and rotate the dedicated Codex authentication.
3. Inspect retained sanitized events and artifacts. Never publish the private databases or raw
   Codex home.
4. Remove affected run directories and mirrors while the service is stopped.
5. Reinstall a verified release and worker image, generate new TLS material, run doctor, and pair
   again.

Codex authentication is copied into a fresh per-run home, is never copied back, and is removed with
the transient workspace. The worker sees no Git push credential, runner configuration, host home,
container socket, or another run's workspace.

## V1 boundaries

The runner returns patches and bounded evidence. It does not create or push branches, merge changes,
mark PM tasks complete, accept dirty workspace snapshots, steer a live thread, execute milestones,
or accept arbitrary commands, mounts, images, networks, or container options from the UI. PM owns
project semantics; the runner owns only accepted execution lifecycles.
