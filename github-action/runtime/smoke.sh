#!/bin/sh
set -eu

script_dir=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
repository_root=$(CDPATH= cd -- "$script_dir/../.." && pwd)
release_root=$repository_root/artifacts/release
artifact_root=$repository_root/artifacts/github-action

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

command -v docker >/dev/null 2>&1 || fail 'Docker is required.'
command -v jq >/dev/null 2>&1 || fail 'jq is required to inspect the OCI archive.'
[ -f "$release_root/PM.dll" ] || fail 'Missing artifacts/release/PM.dll. Run npm run release from web/ first.'

pm_version=$(dotnet "$release_root/PM.dll" --version)
source_revision=$(git -C "$repository_root" rev-parse HEAD)
short_revision=$(printf '%s' "$source_revision" | cut -c1-12)
image_tag="pm-github-action-runtime:${pm_version}-${short_revision}"
archive=$artifact_root/pm-github-action-runtime-${pm_version}.oci.tar
[ -f "$archive" ] || fail "Missing $archive. Run github-action/runtime/build.sh first."

work=$(mktemp -d "${TMPDIR:-/tmp}/pm-action-runtime-smoke.XXXXXX")
cleanup() {
  chmod -R u+w "$work" 2>/dev/null || true
  rm -rf "$work"
}
trap cleanup EXIT HUP INT TERM

workspace=$work/workspace
valid_project=$workspace/project
invalid_project=$workspace/invalid
output_root=$workspace/output
mkdir -p "$valid_project" "$invalid_project" "$output_root"
cp -R "$repository_root/.pm" "$valid_project/.pm"
cp -R "$repository_root/.pm" "$invalid_project/.pm"
invalid_task=$(find "$invalid_project/.pm/tasks" -type f -name '*.md' | LC_ALL=C sort | head -n 1)
[ -n "$invalid_task" ] || fail 'The source project has no task file for invalid-project coverage.'
mv "$invalid_task" "$invalid_project/.pm/tasks/invalid-task-file.md"

actual_version=$(docker run --rm "$image_tag" --version)
[ "$actual_version" = "$pm_version" ] || fail "Expected PM $pm_version, received $actual_version."

doctor_output=$(docker run --rm \
  --volume "$valid_project:/github/workspace/project:ro" \
  --workdir /github/workspace/project \
  "$image_tag" doctor)
printf '%s\n' "$doctor_output" | grep -F 'Project validation passed.' >/dev/null ||
  fail 'The packaged runtime did not validate the disposable project.'

invalid_doctor_log=$work/invalid-doctor.log
if docker run --rm \
  --volume "$invalid_project:/github/workspace/project:ro" \
  --workdir /github/workspace/project \
  "$image_tag" doctor >"$invalid_doctor_log" 2>&1; then
  fail 'Doctor unexpectedly accepted an invalid project.'
fi
grep -F 'task_filename_mismatch' "$invalid_doctor_log" >/dev/null ||
  fail 'Doctor did not report the expected invalid task-file diagnostic.'

invalid_log=$work/invalid-site.log
if docker run --rm \
  --volume "$invalid_project:/github/workspace/project:ro" \
  --volume "$output_root:/github/workspace/output" \
  --workdir /github/workspace/project \
  "$image_tag" site build --output ../output/invalid >"$invalid_log" 2>&1; then
  fail 'Static export unexpectedly accepted an invalid project.'
fi
[ ! -e "$output_root/invalid" ] || fail 'Failed validation left a static output directory behind.'
grep -F 'Project validation failed' "$invalid_log" >/dev/null ||
  fail 'Invalid-project diagnostics did not explain the validation failure.'

docker run --rm \
  --volume "$valid_project:/github/workspace/project:ro" \
  --volume "$output_root:/github/workspace/output" \
  --workdir /github/workspace/project \
  "$image_tag" site build --output ../output/site
[ -f "$output_root/site/index.html" ] || fail 'Static export did not persist index.html on the host.'
[ -f "$output_root/site/pm-snapshot.json" ] || fail 'Static export did not persist pm-snapshot.json on the host.'

expected_files=$work/expected-files.txt
actual_files=$work/actual-files.txt
find "$release_root" -type f ! -name PM ! -name '*.pdb' \
  | sed "s#^$release_root/##" | LC_ALL=C sort > "$expected_files"
docker run --rm --entrypoint /bin/sh "$image_tag" -c \
  'find /opt/pm -type f | sed "s#^/opt/pm/##" | sort' > "$actual_files"
diff -u "$expected_files" "$actual_files" || fail 'Container payload differs from the portable release layout.'

docker run --rm --entrypoint /bin/sh "$image_tag" -c '
  test -f /etc/ssl/certs/ca-certificates.crt
  test ! -e /opt/pm/PM
  test -z "$(find /opt/pm -name "*.pdb" -print -quit)"
  test -z "$(find /opt/pm -perm -222 -print -quit)"
  test ! -d /usr/share/dotnet/sdk
  ! command -v git >/dev/null 2>&1
  ! command -v node >/dev/null 2>&1
  ! command -v npm >/dev/null 2>&1
'

config=$(docker image inspect "$image_tag" --format '{{json .Config}}')
[ "$(printf '%s' "$config" | jq -r '.User')" = '0:0' ] || fail 'Runtime image must use root for Docker Action compatibility.'
[ "$(printf '%s' "$config" | jq -r '.WorkingDir')" = '/github/workspace' ] || fail 'Unexpected runtime working directory.'
[ "$(printf '%s' "$config" | jq -c '.Entrypoint')" = '["dotnet","/opt/pm/PM.dll"]' ] || fail 'Unexpected runtime entrypoint.'
[ "$(printf '%s' "$config" | jq -r '.Labels["org.opencontainers.image.version"]')" = "$pm_version" ] || fail 'Missing PM version label.'
[ "$(printf '%s' "$config" | jq -r '.Labels["org.opencontainers.image.revision"]')" = "$source_revision" ] || fail 'Missing source revision label.'
[ "$(printf '%s' "$config" | jq -r '.Labels["org.opencontainers.image.licenses"]')" = 'MIT' ] || fail 'Missing license label.'

oci_layout=$work/oci-layout
mkdir -p "$oci_layout"
tar -xf "$archive" -C "$oci_layout"
top_digest=$(jq -r '.manifests[0].digest' "$oci_layout/index.json")
case "$top_digest" in
  sha256:*) ;;
  *) fail 'OCI archive index does not reference a SHA-256 manifest.' ;;
esac
manifest_path=$oci_layout/blobs/sha256/${top_digest#sha256:}
[ -f "$manifest_path" ] || fail 'OCI archive manifest blob is missing.'
platforms=$(jq -r '.manifests[] | select(.platform.os == "linux") | "\(.platform.os)/\(.platform.architecture)"' "$manifest_path" | LC_ALL=C sort)
expected_platforms=$(printf '%s\n' linux/amd64 linux/arm64 | LC_ALL=C sort)
[ "$platforms" = "$expected_platforms" ] || fail "Unexpected OCI platforms:\n$platforms"

image_id=$(docker image inspect "$image_tag" --format '{{.Id}}')
archive_checksum=$(sha256_file "$archive")
printf 'PM GitHub Action runtime smoke passed.\n'
printf 'Native image ID: %s\n' "$image_id"
printf 'OCI archive SHA-256: %s\n' "$archive_checksum"
printf 'OCI platforms:\n%s\n' "$platforms"
