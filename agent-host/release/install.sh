#!/bin/sh
set -eu

command_name=${1:-help}
if [ "$#" -gt 0 ]; then shift; fi

fail() {
  printf '%s\n' "$1" >&2
  exit 1
}

[ "$(uname -s)" = Linux ] || fail 'The PM agent host installer requires Linux.'
[ "$(uname -m)" = x86_64 ] || fail 'The PM agent host installer requires x86_64.'
[ "$(id -u)" -ne 0 ] || fail 'Run this installer as the dedicated unprivileged runner user.'

data_home=${XDG_DATA_HOME:-$HOME/.local/share}
config_home=${XDG_CONFIG_HOME:-$HOME/.config}
install_root=$data_home/pm-agent-host
config_root=$config_home/pm-agent-host
service_root=$config_home/systemd/user

verify_artifacts() {
  artifact_dir=$1
  [ -f "$artifact_dir/SHA256SUMS" ] || fail 'SHA256SUMS is missing.'
  (cd "$artifact_dir" && sha256sum --check SHA256SUMS)
}

install_release() {
  artifact_dir=${1:-.}
  artifact_dir=$(CDPATH= cd -- "$artifact_dir" && pwd)
  verify_artifacts "$artifact_dir"
  [ "$(node --version)" = v26.5.0 ] || fail 'Node 26.5.0 is required.'
  command -v podman >/dev/null 2>&1 || fail 'Rootless Podman is required.'
  podman info >/dev/null

  release_info=$artifact_dir/release-info.json
  version=$(node -e "process.stdout.write(require(process.argv[1]).packageVersion)" "$release_info")
  revision=$(node -e "process.stdout.write(require(process.argv[1]).sourceRevision)" "$release_info")
  image_reference=$(node -e "process.stdout.write(require(process.argv[1]).workerImageReference)" "$release_info")
  release_id="${version}-$(printf '%s' "$revision" | cut -c1-12)"
  host_archive=$artifact_dir/pm-agent-host-${version}-linux-x64.tar.gz
  image_archive=$artifact_dir/pm-agent-worker-${version}-linux-x64.oci.tar
  [ -f "$host_archive" ] || fail 'Host archive is missing.'
  [ -f "$image_archive" ] || fail 'Worker image archive is missing.'

  temporary=$(mktemp -d)
  trap 'rm -rf "$temporary"' EXIT HUP INT TERM
  tar -xzf "$host_archive" -C "$temporary"
  [ -x "$temporary/pm-agent-host/bin/pm-agent-host" ] || fail 'Host archive layout is invalid.'

  mkdir -p -m 0700 "$install_root/releases" "$config_root" "$service_root"
  destination=$install_root/releases/$release_id
  if [ ! -e "$destination" ]; then
    mv "$temporary/pm-agent-host" "$destination"
  fi
  podman load --input "$image_archive" >/dev/null
  podman image exists "$image_reference" || fail 'Loaded worker image digest does not match the release.'

  installer_next=$install_root/install.sh.next
  cp "$0" "$installer_next"
  chmod 0555 "$installer_next"
  mv "$installer_next" "$install_root/install.sh"
  cp "$destination/share/systemd/pm-agent-host.service" "$service_root/pm-agent-host.service"
  cp "$destination/share/capabilities.json" "$config_root/capabilities.json"
  chmod 0600 "$config_root/capabilities.json"
  if [ -L "$install_root/current" ]; then
    current=$(readlink "$install_root/current")
    ln -sfn "$current" "$install_root/previous"
  fi
  ln -sfn "$destination" "$install_root/current"
  systemctl --user daemon-reload
  printf 'Installed PM agent host %s. Run configure before enabling the service.\n' "$release_id"
}

configure_release() {
  [ "$#" -eq 3 ] || fail 'Usage: install.sh configure <listen-ip> <repository-remote> <codex-auth-source>'
  listen_ip=$1
  repository_remote=$2
  authentication_source=$3
  [ -L "$install_root/current" ] || fail 'Install a release before configuring it.'
  [ -f "$authentication_source" ] || fail 'Codex authentication source is missing.'
  node -e "if (require('node:net').isIP(process.argv[1]) === 0) process.exit(1)" "$listen_ip" \
    || fail 'Listen address must be an explicit IP address.'

  mkdir -p -m 0700 "$config_root/tls" "$data_home/pm-runner"
  if [ ! -f "$config_root/tls/certificate.pem" ] || [ ! -f "$config_root/tls/key.pem" ]; then
    openssl req -x509 -newkey ec -pkeyopt ec_paramgen_curve:prime256v1 \
      -keyout "$config_root/tls/key.pem" -out "$config_root/tls/certificate.pem" \
      -sha256 -days 365 -nodes -subj "/CN=$listen_ip" -addext "subjectAltName=IP:$listen_ip"
  fi
  chmod 0600 "$config_root/tls/key.pem"
  authentication_destination=$config_root/codex-auth.json
  if [ "$(readlink -f "$authentication_source")" != "$(readlink -f "$authentication_destination" 2>/dev/null || true)" ]; then
    cp "$authentication_source" "$authentication_destination"
  fi
  chmod 0600 "$authentication_destination"
  node -e "require('node:fs').writeFileSync(process.argv[2], JSON.stringify({repositories:[{remote:process.argv[1]}]}, null, 2)+'\\n', {mode:0o600})" \
    "$repository_remote" "$config_root/repositories.json"
  chmod 0600 "$config_root/repositories.json"

  umask 077
  {
    printf 'PM_AGENT_HOST_DATA_ROOT=%s\n' "$data_home/pm-runner"
    printf 'PM_AGENT_HOST_MAX_CONCURRENCY=1\n'
    printf 'PM_AGENT_HOST_QUEUE_CAPACITY=32\n'
    printf 'PM_AGENT_HOST_RETENTION_DAYS=30\n'
    printf 'PM_AGENT_HOST_MIN_FREE_DISK_BYTES=5368709120\n'
    printf 'PM_AGENT_HOST_LISTEN_ADDRESS=%s\n' "$listen_ip"
    printf 'PM_AGENT_HOST_PORT=7443\n'
    printf 'PM_AGENT_HOST_TLS_CERT_PATH=%s/tls/certificate.pem\n' "$config_root"
    printf 'PM_AGENT_HOST_TLS_KEY_PATH=%s/tls/key.pem\n' "$config_root"
    printf 'PM_AGENT_HOST_CAPABILITIES_PATH=%s/capabilities.json\n' "$config_root"
    printf 'PM_AGENT_HOST_REPOSITORIES_PATH=%s/repositories.json\n' "$config_root"
    printf 'PM_AGENT_HOST_CODEX_AUTH_PATH=%s/codex-auth.json\n' "$config_root"
    printf 'PM_AGENT_HOST_RELEASE_MANIFEST_PATH=%s/current/lib/release-info.json\n' "$install_root"
  } > "$config_root/host.env"
  chmod 0600 "$config_root/host.env"
  set -a
  . "$config_root/host.env"
  set +a
  "$install_root/current/bin/pm-agent-host" doctor
  printf 'Configuration is ready. Pair, then enable pm-agent-host.service explicitly.\n'
}

rollback_release() {
  [ -L "$install_root/previous" ] || fail 'No previous release is available.'
  previous=$(readlink "$install_root/previous")
  current=$(readlink "$install_root/current")
  [ -d "$previous" ] || fail 'Previous release is missing.'
  ln -sfn "$previous" "$install_root/current"
  ln -sfn "$current" "$install_root/previous"
  cp "$previous/share/systemd/pm-agent-host.service" "$service_root/pm-agent-host.service"
  cp "$previous/share/capabilities.json" "$config_root/capabilities.json"
  chmod 0600 "$config_root/capabilities.json"
  systemctl --user daemon-reload
  printf 'Rolled back to %s. Restart the service after running doctor.\n' "$previous"
}

case "$command_name" in
  verify) verify_artifacts "${1:-.}" ;;
  install|upgrade) install_release "${1:-.}" ;;
  configure) configure_release "$@" ;;
  rollback) rollback_release ;;
  *)
    printf '%s\n' \
      'Usage: install.sh verify <artifact-dir>' \
      '       install.sh install <artifact-dir>' \
      '       install.sh upgrade <artifact-dir>' \
      '       install.sh configure <listen-ip> <repository-remote> <codex-auth-source>' \
      '       install.sh rollback'
    ;;
esac
