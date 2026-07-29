#!/bin/sh
set -eu

script_dir=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
repository_root=$(CDPATH= cd -- "$script_dir/../.." && pwd)
artifact_root=${1:-$repository_root/artifacts/agent-host}
artifact_parent=$(dirname "$artifact_root")
artifact_name=$(basename "$artifact_root")
node_version=v26.5.0

fail() {
  printf '%s\n' "$1" >&2
  exit 1
}

[ "$(uname -s)" = Linux ] || fail 'Linux release artifacts must be built on Linux.'
[ "$(uname -m)" = x86_64 ] || fail 'Linux release artifacts require x86_64.'
[ "$(id -u)" -ne 0 ] || fail 'Linux release artifacts must be built without root.'
[ "$(node --version)" = "$node_version" ] || fail 'Node 26.5.0 is required.'
npm_major=$(npm --version | cut -d. -f1)
case "$npm_major" in
  11 | 12) ;;
  *) fail 'npm 11 or 12 is required.' ;;
esac
command -v socket >/dev/null 2>&1 || fail 'Socket CLI is required for dependency installation.'
command -v podman >/dev/null 2>&1 || fail 'Rootless Podman is required.'

if command -v dotnet >/dev/null 2>&1; then
  dotnet_command=$(command -v dotnet)
  dotnet_root=$(dirname "$(readlink -f "$dotnet_command")")
elif [ -x "$HOME/.dotnet/dotnet" ]; then
  dotnet_command=$HOME/.dotnet/dotnet
  dotnet_root=$HOME/.dotnet
else
  fail 'The .NET 10 SDK is required.'
fi

dotnet_version=$($dotnet_command --version)
case "$dotnet_version" in
  10.0.*) ;;
  *) fail 'The .NET 10 SDK is required.' ;;
esac

[ -z "$(git -C "$repository_root" status --porcelain)" ] || fail 'Release packaging requires a clean checkout.'
revision=$(git -C "$repository_root" rev-parse HEAD)
short_revision=$(printf '%s' "$revision" | cut -c1-12)
source_date_epoch=$(git -C "$repository_root" show -s --format=%ct HEAD)
built_at=$(git -C "$repository_root" show -s --format=%cI HEAD)
version=$(node -p "require('$repository_root/agent-host/package.json').version")
image_tag="localhost/pm-agent-worker:${version}-${short_revision}"

mkdir -p "$artifact_parent"
artifact_parent=$(CDPATH= cd -- "$artifact_parent" && pwd)
artifact_root=$artifact_parent/$artifact_name
staged_artifacts=$(mktemp -d "$artifact_parent/.${artifact_name}.staging.XXXXXX")
backup_artifacts=
work=$(mktemp -d)
cleanup() {
  rm -rf "$work" "$staged_artifacts"
  if [ -n "$backup_artifacts" ] && [ -e "$backup_artifacts" ]; then
    if [ ! -e "$artifact_root" ]; then
      mv "$backup_artifacts" "$artifact_root"
    else
      rm -rf "$backup_artifacts"
    fi
  fi
}
trap cleanup EXIT HUP INT TERM
bundle="$work/pm-agent-host"
image_context="$work/image"
mkdir -p "$bundle/bin" "$bundle/lib" "$bundle/share/systemd" "$bundle/share/examples" \
  "$image_context/agent-host" "$image_context/pm"

cd "$repository_root/agent-host"
socket npm ci
npm run validate
npm run build

cp package.json package-lock.json "$bundle/lib/"
socket npm ci --omit=dev --prefix "$bundle/lib"
mkdir -p "$bundle/lib/dist"
cp -R dist/src "$bundle/lib/dist/"
cp systemd/pm-agent-host.service "$bundle/share/systemd/"
cp systemd/host.env.example capabilities.example.json "$bundle/share/examples/"
cp README.md OPERATIONS.md "$bundle/share/"
cp release/install.sh "$bundle/install.sh"
chmod 0555 "$bundle/install.sh"

cp -R "$bundle/lib/dist" "$bundle/lib/node_modules" "$bundle/lib/package.json" \
  "$image_context/agent-host/"

cd "$repository_root"
"$dotnet_command" restore PM/PM.csproj --runtime linux-x64 -m:1
"$dotnet_command" publish PM/PM.csproj \
  --configuration Release \
  --runtime linux-x64 \
  --self-contained true \
  --no-restore \
  -m:1 \
  --output "$image_context/pm"
cp -R "$dotnet_root" "$image_context/dotnet"

podman build \
  --pull=never \
  --build-arg "SOURCE_REVISION=$revision" \
  --build-arg "PACKAGE_VERSION=$version" \
  --file "$script_dir/../container/Containerfile.production" \
  --tag "$image_tag" \
  "$image_context"
image_digest=$(podman image inspect "$image_tag" --format '{{.Digest}}')
case "$image_digest" in
  sha256:????????????????????????????????????????????????????????????????) ;;
  *) fail 'Podman did not produce a valid image digest.' ;;
esac
image_reference="localhost/pm-agent-worker@$image_digest"

node "$repository_root/agent-host/dist/src/release-tool.js" capabilities \
  "$repository_root/agent-host/container/capabilities.production.json" \
  "$image_reference" "$bundle/share/capabilities.json"
node "$repository_root/agent-host/dist/src/release-tool.js" release-info \
  "$version" "$revision" "$built_at" "$image_reference" "$bundle/lib/release-info.json"

cat > "$bundle/bin/pm-agent-host" <<'EOF'
#!/bin/sh
set -eu
script_dir=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
config_home=${XDG_CONFIG_HOME:-$HOME/.config}
environment_file=$config_home/pm-agent-host/host.env
if [ -r "$environment_file" ]; then
  set -a
  . "$environment_file"
  set +a
fi
exec node "$script_dir/../lib/dist/src/main.js" "$@"
EOF
chmod 0555 "$bundle/bin/pm-agent-host"

host_archive="$staged_artifacts/pm-agent-host-${version}-linux-x64.tar.gz"
image_archive="$staged_artifacts/pm-agent-worker-${version}-linux-x64.oci.tar"
capabilities="$staged_artifacts/capabilities.json"
release_info="$staged_artifacts/release-info.json"
installer="$staged_artifacts/install.sh"

cp "$bundle/share/capabilities.json" "$capabilities"
cp "$bundle/lib/release-info.json" "$release_info"
cp "$repository_root/agent-host/release/install.sh" "$installer"
chmod 0555 "$installer"
tar --sort=name --mtime="@$source_date_epoch" --owner=0 --group=0 --numeric-owner \
  -C "$work" -cf - pm-agent-host | gzip -n > "$host_archive"
podman save --format oci-archive --output "$image_archive" "$image_tag"

node "$repository_root/agent-host/dist/src/release-tool.js" artifact-manifest \
  "$release_info" "$staged_artifacts" "$host_archive" "$image_archive" \
  "$capabilities" "$release_info" "$installer"

(cd "$staged_artifacts" && sha256sum --check SHA256SUMS)

if [ -e "$artifact_root" ]; then
  backup_artifacts=$artifact_parent/.${artifact_name}.previous.$$
  mv "$artifact_root" "$backup_artifacts"
fi
if ! mv "$staged_artifacts" "$artifact_root"; then
  [ -z "$backup_artifacts" ] || mv "$backup_artifacts" "$artifact_root"
  fail 'Could not publish the completed release artifact directory.'
fi
staged_artifacts=
if [ -n "$backup_artifacts" ]; then
  rm -rf "$backup_artifacts"
  backup_artifacts=
fi

printf 'Release artifacts created in %s\nImage: %s\n' "$artifact_root" "$image_reference"
