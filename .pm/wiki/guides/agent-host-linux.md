---
title: Linux Agent Host Setup
createdAt: 2026-07-27T11:03:26.0456420Z
modifiedAt: 2026-07-29T07:50:23.1064470Z
---

This guide prepares a Linux workstation to host the PM execution plane. It is intentionally conservative: the trusted runner controls rootless Podman, while each unattended agent runs in a separately hardened worker container.

The repository includes the complete v1 runner lifecycle and a rootless user-service template. Accepted work is materialized from an immutable Git base, executed in an isolated agent container, validated in a fresh credential-free container, and reduced to retained evidence before transient state is removed.

## Intended layout

```text
Linux host
  pm-agent-host user service (trusted)
    -> rootless Podman
      -> isolated worker container per run (untrusted)
```

Use a dedicated local account for the runner if practical. That account should not own personal files, unrelated repositories, broadly privileged SSH keys, Git push credentials, or other long-lived secrets.

## Bazzite and Distrobox

Bazzite is an image-based Fedora Atomic desktop and already uses Podman for its container tooling. Podman plus rootless systemd Quadlet is the preferred service model.

Distrobox is useful for compiling the host, running development tools, and accessing a conventional package manager. It is not a worker security boundary. Distrobox deliberately integrates with the host and normally exposes the user's home and other host resources. Do not execute unattended Codex tasks in a normal Distrobox container.

If a development Distrobox is useful, give it a dedicated home:

```sh
mkdir -p ~/.local/share/pm-agent-dev-home
distrobox create \
  --name pm-agent-dev \
  --image registry.fedoraproject.org/fedora:latest \
  --home ~/.local/share/pm-agent-dev-home
```

This reduces accidental development-tool access, but it does not turn Distrobox into the production sandbox.

References:

- [Bazzite container guidance](https://docs.bazzite.gg/Installing_and_Managing_Software/Containers/)
- [Bazzite Distrobox guidance](https://docs.bazzite.gg/Installing_and_Managing_Software/Distrobox/)
- [Distrobox security implications](https://distrobox.it/)

## Arch Linux

The current integration host is Arch Linux with Podman 6, rootless native overlay on ext4, cgroup v2 managed by systemd, `crun`, and seccomp. SELinux and AppArmor are not active. SELinux-specific runtime labeling is deferred; the runner fails closed on an SELinux-enabled host rather than silently disabling labels.

Node 26.5.0 is installed system-wide. Pinned npm 11 and Socket live under `~/.local/bin`; non-interactive SSH and systemd environments must prepend that directory explicitly:

```sh
export PATH="$HOME/.local/bin:$PATH"
npm --version
socket --version
```

## Host prerequisites

The v1 runner requires:

- A current supported Linux host; Arch Linux is the live v1 integration target.
- Tailscale connectivity between the Mac and Linux host.
- Rootless Podman available to the runner account.
- cgroup v2 available for CPU, memory, and PID limits.
- A working per-user systemd manager.
- Git and enough local disk for images, bare mirrors, isolated workspaces, and retained artifacts.
- Node 26 and npm 11 while the TypeScript host is run from source. A packaged host should eventually remove this development prerequisite.
- The PM repository available for development only; production runs will use runner-owned mirrors and workspaces instead of the normal checkout.

On Bazzite, prefer Homebrew, Distrobox, or the eventual packaged runner over `rpm-ostree` package layering. On Arch, install host prerequisites normally but keep the dedicated runner account free of unrelated credentials and personal data.

When working from source, install the Socket CLI before restoring the agent-host workspace. Every npm operation that installs or changes dependencies must use `socket npm ...`; the normal validation scripts may continue to use `npm run ...` after the locked install.

## Verify rootless Podman

Run these commands as the intended runner user, without `sudo`:

```sh
podman version
podman info --format 'rootless={{.Host.Security.Rootless}} cgroups={{.Host.CgroupsVersion}} manager={{.Host.CgroupManager}}'
podman info --format 'graphRoot={{.Store.GraphRoot}}'
```

Expected properties:

- `rootless=true`
- `cgroups=v2`
- the cgroup manager is normally `systemd`
- the graph root belongs to the runner user

Check subordinate user and group mappings:

```sh
grep "^$USER:" /etc/subuid
grep "^$USER:" /etc/subgid
podman unshare cat /proc/self/uid_map
podman unshare cat /proc/self/gid_map
```

Missing subordinate mappings must be fixed before relying on rootless user namespaces.

## Resource-limit smoke test

This disposable command verifies the baseline controls used by the runner:

```sh
podman run --rm \
  --pull=missing \
  --cpus 0.50 \
  --memory 256m \
  --pids-limit 64 \
  --cap-drop all \
  --security-opt no-new-privileges \
  --read-only \
  --tmpfs /tmp:rw,noexec,nosuid,size=32m \
  docker.io/library/alpine:3.22 \
  sh -c 'id && cat /proc/self/cgroup && touch /tmp/ok'
```

The image tag will be pinned by digest in installed runtime profiles. This preparatory smoke uses a versioned public image only to verify the host.

Do not continue to unattended execution if CPU, memory, PID, user-namespace, read-only-root, or `no-new-privileges` controls fail.

## Runner directories

Protocol examples use `/var/lib/pm-runner`, but a rootless user service should use owner-controlled user directories:

```sh
install -d -m 700 ~/.local/share/pm-runner
install -d -m 700 ~/.config/pm-agent-host
install -d -m 700 ~/.config/pm-agent-host/tls
install -m 600 /secure/source/auth.json ~/.config/pm-agent-host/codex-auth.json
```

The intended split is:

```text
~/.config/pm-agent-host/
  capabilities.json
  repositories.json
  codex-auth.json
  host.env
  tls/
    certificate.pem
    key.pem

~/.local/share/pm-runner/
  runner.sqlite
  credentials.sqlite
  mirrors/
    <remote-sha256>.git/
  runs/
    <run-id>/
      artifacts/
```

`repositories.json` contains an exact allowlist of trusted HTTPS or SSH Git remotes. The Codex auth file, TLS key, runner databases, and configuration must be owner-only. They never belong in Git or a worker image.

During execution, each run temporarily gains `workspace/`, `codex-home/`, `runtime/`, and `scratch/` directories. The runner removes those before committing a terminal state. Only bounded artifacts and reusable bare mirrors survive according to retention policy.

## User service prerequisites

The packaged host will run as a user systemd service. Confirm the user manager works:

```sh
systemctl --user status
systemctl --user show-environment
```

For a dedicated runner that must start before interactive login and survive logout, enable lingering once from an administrator account:

```sh
sudo loginctl enable-linger <runner-user>
```

The repository provides `agent-host/systemd/pm-agent-host.service` as a user-service template. The trusted host invokes the Podman CLI directly; never expose the Podman socket or executable control channel to worker containers.

Rootless Quadlet documentation and its cgroup v2 requirement are covered by the [Podman systemd unit documentation](https://docs.podman.io/en/stable/markdown/podman-systemd.unit.5.html).

## Tailscale checks

Find the Linux host's Tailscale address:

```sh
tailscale ip -4
tailscale status
```

The runner HTTPS service must bind to the explicit Tailscale address, not `0.0.0.0` or `::`. Pairing will require the Mac to verify the runner certificate fingerprint out of band.

Before pairing, verify from the Mac that:

- the Linux Tailscale address is reachable;
- the selected runner port is reachable only over the intended private path;
- the certificate name or IP and pinned fingerprint match the runner;
- sleep, logout, and a Mac disconnect do not stop the Linux user service.

Do not publish the runner port through a router, public reverse proxy, Funnel, or an unrestricted firewall rule.

## Pair PM with the runner

Open a one-use pairing window locally on the Linux host, then copy the displayed runner ID and TLS fingerprint:

```sh
node agent-host/dist/src/main.js pair \
  --data-root ~/.local/share/pm-runner \
  --tls-cert ~/.config/pm-agent-host/tls.crt
```

With the runner service listening on its explicit Tailscale address, pair from the PM machine. PM reads the short-lived code from a masked prompt and stores the TLS pin plus a runner-scoped signing credential in private OS user configuration outside every project:

```sh
pm runner pair https://<tailscale-ip>:7443 \
  --runner-id <runner-id> \
  --fingerprint sha256:<certificate-fingerprint>
```

Verify authenticated transport and advertised capacity after pairing:

```sh
pm runner list
pm runner status <runner-id>
```

`pm runner rotate <runner-id>` replaces only that runner's signing credential. `pm runner revoke <runner-id>` revokes the remote client before deleting the local registration. A changed TLS certificate is never trusted automatically; perform local recovery on the runner and explicitly pair again with the new fingerprint.

## Storage and workstation safety

Worker workspaces and images can consume substantial disk. Before a test session, record:

```sh
df -h ~/.local/share/pm-runner
podman system df
findmnt -T ~/.local/share/pm-runner
```

Set explicit retention and disk limits before allowing concurrent runs. Keep the runner data root on a local Linux filesystem with normal ownership and permission semantics.

Because the runner host may also be used interactively, the runner account and its containers must not receive access to gaming libraries, mounted personal drives, the desktop session, GPU devices, audio devices, or the user's normal home directory.

## Information to capture for integration

When diagnosing Linux integration, capture the output of:

```sh
cat /etc/os-release
uname -a
podman version
podman info --format json
systemctl --user status
tailscale status
df -h ~/.local/share/pm-runner
```

Review the output for hostnames, addresses, usernames, registry credentials, or other private values before sharing it. Never include TLS private keys, Podman auth files, Codex credentials, PM identity private keys, pairing codes, or recovery keys.

## Readiness checklist

- [ ] Dedicated runner account or an explicitly accepted shared-account risk.
- [ ] Rootless Podman works without `sudo`.
- [ ] cgroup v2 and systemd cgroup management are active.
- [ ] CPU, memory, PID, read-only-root, and privilege restrictions pass the smoke test.
- [ ] User services can survive logout and reboot.
- [ ] Tailscale private connectivity works from the Mac.
- [ ] Runner config and data directories are private and outside repositories.
- [ ] No personal home, host credentials, Podman socket, or unrelated mounts will reach workers.
- [ ] Adequate disk space and retention policy are available.
