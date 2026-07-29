#!/bin/sh
set -eu

script_dir=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
repository_root=$(CDPATH= cd -- "$script_dir/../.." && pwd)
image_name=${PM_AGENT_HOST_DEVELOPMENT_IMAGE:-localhost/pm-agent-development:agent-0008}
context=$(mktemp -d)
trap 'rm -rf "$context"' EXIT HUP INT TERM

if [ "$(uname -s)" != "Linux" ]; then
  printf '%s\n' 'The development worker image must be built on Linux.' >&2
  exit 1
fi

if command -v dotnet >/dev/null 2>&1; then
  dotnet_command=$(command -v dotnet)
elif [ -x "$HOME/.dotnet/dotnet" ]; then
  dotnet_command=$HOME/.dotnet/dotnet
else
  printf '%s\n' 'The .NET 10 SDK is required to publish the PM worker CLI.' >&2
  exit 1
fi

cd "$repository_root/agent-host"
socket npm ci
npm run build

mkdir -p "$context/agent-host" "$context/pm"
cp -R dist node_modules package.json "$context/agent-host/"

cd "$repository_root"
"$dotnet_command" restore PM/PM.csproj --runtime linux-x64 -m:1
"$dotnet_command" publish PM/PM.csproj \
  --configuration Release \
  --runtime linux-x64 \
  --self-contained true \
  --no-restore \
  -m:1 \
  --output "$context/pm"

podman build \
  --pull=never \
  --file "$script_dir/Containerfile.development" \
  --tag "$image_name" \
  "$context"

digest=$(podman image inspect "$image_name" --format '{{.Digest}}')
printf 'Built development-only worker image:\n%s@%s\n' "${image_name%:*}" "$digest"
