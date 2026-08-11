#!/bin/sh
set -eu

script_dir=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
repository_root=$(CDPATH= cd -- "$script_dir/../.." && pwd)
release_root=$repository_root/artifacts/release
artifact_root=$repository_root/artifacts/github-action
containerfile=$repository_root/github-action/runtime/Containerfile

fail() {
  printf '%s\n' "$1" >&2
  exit 1
}

if [ "$#" -ne 1 ]; then
  fail 'Usage: github-action/release/publish-candidate.sh ghcr.io/chronium/pm:candidate-<source-sha>'
fi
publish_ref=$1
printf '%s' "$publish_ref" | grep -Eq '^ghcr\.io/chronium/pm:candidate-[0-9a-f]{40}$' ||
  fail 'The publication ref must be a PM candidate tag with a full source revision.'

command -v docker >/dev/null 2>&1 || fail 'Docker with Buildx is required.'
command -v jq >/dev/null 2>&1 || fail 'jq is required.'
[ -z "$(git -C "$repository_root" status --porcelain --untracked-files=normal)" ] ||
  fail 'Refusing to publish an Action image from a dirty source tree.'

pm_version=$(dotnet "$release_root/PM.dll" --version)
source_revision=$(git -C "$repository_root" rev-parse HEAD)
source_created=$(git -C "$repository_root" show -s --format=%cI HEAD)
source_epoch=$(git -C "$repository_root" show -s --format=%ct HEAD)
expected_ref=ghcr.io/chronium/pm:candidate-$source_revision
[ "$publish_ref" = "$expected_ref" ] || fail 'Candidate ref does not match the current source revision.'
archive=$artifact_root/pm-github-action-runtime-${pm_version}.oci.tar
[ -f "$archive" ] || fail 'Missing the locally validated multi-platform OCI archive.'

layout=$(mktemp -d "$artifact_root/.candidate-layout.XXXXXX")
builder_name=pm-github-action-publish-$$
builder_created=false
metadata_staging=$artifact_root/.published-build-metadata.json.staging
cleanup() {
  rm -f "$metadata_staging"
  rm -rf "$layout"
  if [ "$builder_created" = true ]; then
    docker buildx rm "$builder_name" >/dev/null 2>&1 || true
  fi
}
trap cleanup EXIT HUP INT TERM

tar -xf "$archive" -C "$layout"
archive_digest=$(jq -er '.manifests[0].digest | select(test("^sha256:[0-9a-f]{64}$"))' \
  "$layout/index.json") || fail 'The validated OCI archive has no canonical index digest.'

existing_digest=$(docker buildx imagetools inspect "$publish_ref" 2>/dev/null \
  | awk '$1 == "Digest:" { print $2; exit }') || true
if [ -n "$existing_digest" ]; then
  [ "$existing_digest" = "$archive_digest" ] ||
    fail "Candidate ref $publish_ref already identifies a different image digest."
  published_digest=$existing_digest
else
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
      --build-arg SOURCE_DIRTY=false \
      --platform linux/amd64,linux/arm64 \
      --provenance=false \
      --sbom=false \
      --output type=registry,rewrite-timestamp=true \
      --tag "$publish_ref" \
      --metadata-file "$metadata_staging" \
      "$repository_root"
  published_digest=$(jq -er '."containerimage.digest" | select(test("^sha256:[0-9a-f]{64}$"))' \
    "$metadata_staging") || fail 'Buildx did not report a canonical registry digest.'
  [ "$published_digest" = "$archive_digest" ] ||
    fail 'Published registry digest differs from the locally validated OCI archive.'
fi

metadata=$artifact_root/published-build-metadata.json
printf '{"schemaVersion":1,"ref":"%s","digest":"%s","version":"%s","sourceRevision":"%s"}\n' \
  "$publish_ref" "$published_digest" "$pm_version" "$source_revision" > "$metadata"
if [ -n "${GITHUB_OUTPUT:-}" ]; then
  printf 'image-digest=%s\n' "$published_digest" >> "$GITHUB_OUTPUT"
fi
printf 'Published candidate: %s@%s\n' "$publish_ref" "$published_digest"
