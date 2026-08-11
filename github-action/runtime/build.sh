#!/bin/sh
set -eu

script_dir=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
repository_root=$(CDPATH= cd -- "$script_dir/../.." && pwd)
release_root=$repository_root/artifacts/release
artifact_root=$repository_root/artifacts/github-action
containerfile=$script_dir/Containerfile

fail() {
  printf '%s\n' "$1" >&2
  exit 1
}

sha256_file() {
  if command -v sha256sum >/dev/null 2>&1; then
    sha256sum "$1" | cut -d' ' -f1
  elif command -v shasum >/dev/null 2>&1; then
    shasum -a 256 "$1" | cut -d' ' -f1
  else
    fail 'sha256sum or shasum is required.'
  fi
}

command -v docker >/dev/null 2>&1 || fail 'Docker with Buildx is required.'
command -v jq >/dev/null 2>&1 || fail 'jq is required to verify PM release metadata.'
[ -f "$release_root/PM.dll" ] || fail 'Missing artifacts/release/PM.dll. Run npm run release from web/ first.'
[ -f "$release_root/PM.deps.json" ] || fail 'Missing artifacts/release/PM.deps.json.'
[ -f "$release_root/PM.runtimeconfig.json" ] || fail 'Missing artifacts/release/PM.runtimeconfig.json.'
[ -f "$release_root/pm-release.json" ] || fail 'Missing artifacts/release/pm-release.json. Rebuild the release from its canonical version source.'

pm_version=$(dotnet "$release_root/PM.dll" --version)
manifest_version=$(jq -er '
  select(.schemaVersion == 1) |
  .version |
  select(type == "string" and test("^(0|[1-9][0-9]*)\\.(0|[1-9][0-9]*)\\.(0|[1-9][0-9]*)$"))
' "$release_root/pm-release.json") || fail 'The packaged PM release manifest is invalid.'
[ "$pm_version" = "$manifest_version" ] ||
  fail "PM runtime version $pm_version conflicts with release manifest version $manifest_version."

source_revision=$(git -C "$repository_root" rev-parse HEAD)
source_created=$(git -C "$repository_root" show -s --format=%cI HEAD)
source_epoch=$(git -C "$repository_root" show -s --format=%ct HEAD)
source_dirty=false
if [ -n "$(git -C "$repository_root" status --porcelain --untracked-files=normal)" ]; then
  source_dirty=true
fi

server_architecture=$(docker version --format '{{.Server.Arch}}')
case "$server_architecture" in
  amd64 | x86_64) native_platform=linux/amd64 ;;
  arm64 | aarch64) native_platform=linux/arm64 ;;
  *) fail "Unsupported Docker server architecture: $server_architecture" ;;
esac

short_revision=$(printf '%s' "$source_revision" | cut -c1-12)
image_tag="pm-github-action-runtime:${pm_version}-${short_revision}"
mkdir -p "$artifact_root"
archive=$artifact_root/pm-github-action-runtime-${pm_version}.oci.tar
archive_staging=$artifact_root/.pm-github-action-runtime-${pm_version}.oci.tar.staging
metadata_staging=$artifact_root/.native-build-metadata.json.staging
metadata=$artifact_root/native-build-metadata.json
builder_name=pm-github-action-runtime-$$
builder_created=false
rm -f "$archive_staging" "$metadata_staging"
cleanup() {
  rm -f "$archive_staging" "$metadata_staging"
  if [ "$builder_created" = true ]; then
    docker buildx rm "$builder_name" >/dev/null 2>&1 || true
  fi
}
trap cleanup EXIT HUP INT TERM

docker buildx create --name "$builder_name" --driver docker-container >/dev/null
builder_created=true
docker buildx inspect --builder "$builder_name" --bootstrap >/dev/null

SOURCE_DATE_EPOCH=$source_epoch \
  docker buildx build \
    --builder "$builder_name" \
    --file "$containerfile" \
    --build-arg "SOURCE_REVISION=$source_revision" \
    --build-arg "SOURCE_CREATED=$source_created" \
    --build-arg "PM_VERSION=$pm_version" \
    --build-arg "SOURCE_DIRTY=$source_dirty" \
    --platform "$native_platform" \
    --provenance=false \
    --sbom=false \
    --output "type=docker,rewrite-timestamp=true" \
    --tag "$image_tag" \
    --metadata-file "$metadata_staging" \
    "$repository_root"
mv "$metadata_staging" "$metadata"

SOURCE_DATE_EPOCH=$source_epoch \
  docker buildx build \
    --builder "$builder_name" \
    --file "$containerfile" \
    --build-arg "SOURCE_REVISION=$source_revision" \
    --build-arg "SOURCE_CREATED=$source_created" \
    --build-arg "PM_VERSION=$pm_version" \
    --build-arg "SOURCE_DIRTY=$source_dirty" \
    --platform linux/amd64,linux/arm64 \
    --provenance=false \
    --sbom=false \
    --output "type=oci,dest=$archive_staging,rewrite-timestamp=true" \
    "$repository_root"
mv "$archive_staging" "$archive"
docker buildx rm "$builder_name" >/dev/null
builder_created=false
trap - EXIT HUP INT TERM

image_id=$(docker image inspect "$image_tag" --format '{{.Id}}')
archive_checksum=$(sha256_file "$archive")

printf 'Native image: %s\n' "$image_tag"
printf 'Native image ID: %s\n' "$image_id"
printf 'Native platform: %s\n' "$native_platform"
printf 'OCI archive: %s\n' "$archive"
printf 'OCI archive SHA-256: %s\n' "$archive_checksum"
printf 'Source revision: %s\n' "$source_revision"
printf 'Source dirty: %s\n' "$source_dirty"

if [ "$source_dirty" = true ]; then
  printf '%s\n' 'Note: local validation image records a dirty source tree; release publication requires a clean promoted revision.'
fi
