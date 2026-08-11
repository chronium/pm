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

sha256_stdin() {
  if command -v sha256sum >/dev/null 2>&1; then
    sha256sum | cut -d' ' -f1
  elif command -v shasum >/dev/null 2>&1; then
    shasum -a 256 | cut -d' ' -f1
  else
    fail 'sha256sum or shasum is required.'
  fi
}

tree_digest() {
  (cd "$1" && tar -cf - .) | sha256_stdin
}

command -v docker >/dev/null 2>&1 || fail 'Docker is required.'
command -v jq >/dev/null 2>&1 || fail 'jq is required to inspect the OCI archive.'
[ -f "$release_root/PM.dll" ] || fail 'Missing artifacts/release/PM.dll. Run npm run release from web/ first.'
[ -f "$release_root/pm-release.json" ] || fail 'Missing artifacts/release/pm-release.json.'

pm_version=$(dotnet "$release_root/PM.dll" --version)
manifest_version=$(jq -er 'select(.schemaVersion == 1) | .version' "$release_root/pm-release.json") ||
  fail 'The packaged PM release manifest is invalid.'
[ "$manifest_version" = "$pm_version" ] || fail 'The release manifest and runtime report different PM versions.'
source_revision=$(git -C "$repository_root" rev-parse HEAD)
short_revision=$(printf '%s' "$source_revision" | cut -c1-12)
image_tag="pm-github-action-runtime:${pm_version}-${short_revision}"
archive=$artifact_root/pm-github-action-runtime-${pm_version}.oci.tar
[ -f "$archive" ] || fail "Missing $archive. Run github-action/runtime/build.sh first."

work=$(mktemp -d "${TMPDIR:-/tmp}/pm-action-runtime-smoke.XXXXXX")
cleanup() {
  docker run --rm \
    --volume "$work:/cleanup" \
    --entrypoint /bin/sh \
    "$image_tag" -c 'chmod -R a+rwX /cleanup' >/dev/null 2>&1 || true
  chmod -R u+w "$work" 2>/dev/null || true
  rm -rf "$work"
}
trap cleanup EXIT HUP INT TERM

workspace=$work/workspace
valid_project=$workspace/family/chronofall
child_project=$workspace/family/starfall
invalid_project=$workspace/invalid
output_root=$workspace/output
mkdir -p "$valid_project" "$child_project" "$invalid_project" "$output_root"
cp -R "$repository_root/.pm" "$valid_project/.pm"
cp -R "$repository_root/.pm" "$child_project/.pm"
cp -R "$repository_root/.pm" "$invalid_project/.pm"
printf '%s\n' 'action-parent' > "$valid_project/.pm/project_id.txt"
printf '%s\n' 'action-child' > "$child_project/.pm/project_id.txt"
printf '%s\n' \
  'version: 1' \
  'children:' \
  '  - projectId: action-child' \
  '    alias: starfall' \
  '    repositoryUrl: https://example.test/starfall.git' \
  '    pathHint: ../starfall' \
  '    publicSiteUrl: https://example.test/family/starfall/?source=action#old' \
  > "$valid_project/.pm/linked_projects.yaml"
printf '%s\n' \
  'version: 1' \
  'parent:' \
  '  projectId: action-parent' \
  '  alias: chronofall' \
  '  repositoryUrl: https://example.test/chronofall.git' \
  '  pathHint: ../chronofall' \
  '  publicSiteUrl: https://example.test/family/chronofall/' \
  > "$child_project/.pm/linked_projects.yaml"
invalid_task=$(find "$invalid_project/.pm/tasks" -type f -name '*.md' | LC_ALL=C sort | head -n 1)
[ -n "$invalid_task" ] || fail 'The source project has no task file for invalid-project coverage.'
mv "$invalid_task" "$invalid_project/.pm/tasks/invalid-task-file.md"

github_output=$workspace/.github-output
github_summary=$workspace/.github-summary

run_action() {
  docker run --rm \
    --volume "$workspace:/github/workspace" \
    --env GITHUB_WORKSPACE=/github/workspace \
    --env GITHUB_OUTPUT=/github/workspace/.github-output \
    --env GITHUB_STEP_SUMMARY=/github/workspace/.github-summary \
    "$image_tag" "$@"
}

: > "$github_output"
: > "$github_summary"
actual_version=$(run_action version . ignored false)
[ "$actual_version" = "$pm_version" ] || fail "Expected PM $pm_version, received $actual_version."
grep -Fx "pm-version=$pm_version" "$github_output" >/dev/null || fail 'Version did not publish pm-version.'
grep -Fx 'site-path=' "$github_output" >/dev/null || fail 'Version did not publish an empty site-path.'
grep -F 'PM `version` completed' "$github_summary" >/dev/null || fail 'Version summary is missing.'

: > "$github_output"
: > "$github_summary"
doctor_output=$(run_action doctor family/chronofall ignored false)
printf '%s\n' "$doctor_output" | grep -F 'Project validation passed.' >/dev/null ||
  fail 'The packaged runtime did not validate the disposable project.'
grep -Fx "pm-version=$pm_version" "$github_output" >/dev/null || fail 'Doctor did not publish pm-version.'
grep -Fx 'site-path=' "$github_output" >/dev/null || fail 'Doctor did not publish an empty site-path.'

mkdir -p "$valid_project/src/nested"
: > "$github_output"
: > "$github_summary"
run_action doctor family/chronofall/src/nested ignored false >/dev/null ||
  fail 'The Action did not discover a PM project above a nested working directory.'

invalid_doctor_log=$work/invalid-doctor.log
: > "$github_output"
: > "$github_summary"
if run_action doctor invalid ignored false >"$invalid_doctor_log" 2>&1; then
  fail 'Doctor unexpectedly accepted an invalid project.'
fi
grep -F 'task_filename_mismatch' "$invalid_doctor_log" >/dev/null ||
  fail 'Doctor did not report the expected invalid task-file diagnostic.'
[ ! -s "$github_output" ] || fail 'Failed doctor published successful Action outputs.'
grep -F 'failed with exit code' "$github_summary" >/dev/null || fail 'Failed doctor summary is missing.'

injection_log=$work/injection.log
if run_action 'doctor; touch /github/workspace/owned' family/chronofall ignored false >"$injection_log" 2>&1; then
  fail 'The Action accepted a command-shaped injection input.'
fi
[ ! -e "$workspace/owned" ] || fail 'An Action input was evaluated as shell code.'
grep -F 'command must be exactly' "$injection_log" >/dev/null || fail 'Unknown command diagnostic is missing.'

traversal_log=$work/traversal.log
if run_action doctor ../outside ignored false >"$traversal_log" 2>&1; then
  fail 'The Action accepted working-directory traversal.'
fi
grep -F 'parent traversal' "$traversal_log" >/dev/null || fail 'Traversal diagnostic is missing.'

ln -s ../../outside "$workspace/escaped"
symlink_log=$work/symlink.log
if run_action doctor escaped ignored false >"$symlink_log" 2>&1; then
  fail 'The Action accepted a working-directory symlink escape.'
fi
grep -E 'symbolic link|outside GITHUB_WORKSPACE' "$symlink_log" >/dev/null ||
  fail 'Symlink escape diagnostic is missing.'

: > "$github_output"
: > "$github_summary"
parent_state_before=$(tree_digest "$valid_project/.pm")
child_state_before=$(tree_digest "$child_project/.pm")
run_action site-build family/chronofall/src/nested output/chronofall false
[ -f "$output_root/chronofall/index.html" ] || fail 'Static export did not persist index.html on the host.'
[ -f "$output_root/chronofall/pm-snapshot.json" ] || fail 'Static export did not persist pm-snapshot.json on the host.'
grep -Fx "pm-version=$pm_version" "$github_output" >/dev/null || fail 'Site build did not publish pm-version.'
grep -Fx 'site-path=output/chronofall' "$github_output" >/dev/null || fail 'Site build published the wrong site-path.'
grep -F 'Site output: `output/chronofall`.' "$github_summary" >/dev/null || fail 'Site build summary is missing.'

jq -e '.projectId == "action-parent"' "$output_root/chronofall/pm-snapshot.json" >/dev/null ||
  fail 'Parent static snapshot did not preserve its project identity.'
jq -e '.overview.status == "ready"' "$output_root/chronofall/pm-snapshot.json" >/dev/null ||
  fail 'Parent static snapshot did not preserve its configured Overview.'
jq -e '.project.accent == .settings.accent' "$output_root/chronofall/pm-snapshot.json" >/dev/null ||
  fail 'Parent static snapshot did not preserve its project accent.'
jq -e '.tasks | length > 0' "$output_root/chronofall/pm-snapshot.json" >/dev/null ||
  fail 'Parent static snapshot did not preserve tasks.'
jq -e '.wikiPages | length > 0' "$output_root/chronofall/pm-snapshot.json" >/dev/null ||
  fail 'Parent static snapshot did not preserve wiki content.'
jq -e '.linkedProjects[] | select(.projectId == "action-child") | .publicSiteUrl == "https://example.test/family/starfall/?source=action#old"' \
  "$output_root/chronofall/pm-snapshot.json" >/dev/null ||
  fail 'Parent static snapshot did not preserve linked-project publication metadata.'
grep -F '<base href="./">' "$output_root/chronofall/index.html" >/dev/null ||
  fail 'Static export did not retain relative asset routing.'

: > "$github_output"
: > "$github_summary"
run_action site-build family/starfall output/starfall false
grep -Fx 'site-path=output/starfall' "$github_output" >/dev/null ||
  fail 'Child site build published the wrong site-path.'
jq -e '.projectId == "action-child"' "$output_root/starfall/pm-snapshot.json" >/dev/null ||
  fail 'Child static snapshot did not preserve its project identity.'
jq -e '.linkedProjects[] | select(.projectId == "action-parent") | .publicSiteUrl == "https://example.test/family/chronofall/"' \
  "$output_root/starfall/pm-snapshot.json" >/dev/null ||
  fail 'Child static snapshot did not preserve its parent publication metadata.'

child_output_before=$(tree_digest "$output_root/starfall")
: > "$github_output"
: > "$github_summary"
run_action site-build family/chronofall output/chronofall true >/dev/null
[ "$(tree_digest "$output_root/starfall")" = "$child_output_before" ] ||
  fail 'Rebuilding one family site changed another project output.'
[ "$(tree_digest "$valid_project/.pm")" = "$parent_state_before" ] ||
  fail 'Site export mutated the parent PM project state.'
[ "$(tree_digest "$child_project/.pm")" = "$child_state_before" ] ||
  fail 'Site export mutated the child PM project state.'

local_output=$output_root/local-chronofall
(
  cd "$valid_project"
  dotnet "$release_root/PM.dll" site build --output "$local_output" --force >/dev/null
)
find "$output_root/chronofall" -type f | sed "s#^$output_root/chronofall/##" | LC_ALL=C sort > "$work/action-site-files.txt"
find "$local_output" -type f | sed "s#^$local_output/##" | LC_ALL=C sort > "$work/local-site-files.txt"
diff -u "$work/local-site-files.txt" "$work/action-site-files.txt" ||
  fail 'Action and local site builds produced different file inventories.'
while IFS= read -r relative; do
  case "$relative" in
    index.html | pm-snapshot.json) ;;
    *) cmp "$local_output/$relative" "$output_root/chronofall/$relative" ||
      fail "Action and local site builds differ at $relative." ;;
  esac
done < "$work/action-site-files.txt"
sed -E 's#<meta name="pm-site-generated-at" content="[^"]+">##' \
  "$local_output/index.html" > "$work/local-index.html"
sed -E 's#<meta name="pm-site-generated-at" content="[^"]+">##' \
  "$output_root/chronofall/index.html" > "$work/action-index.html"
cmp "$work/local-index.html" "$work/action-index.html" ||
  fail 'Action and local site index documents differ after timestamp normalization.'
jq 'del(.generatedAt)' "$local_output/pm-snapshot.json" > "$work/local-snapshot.json"
jq 'del(.generatedAt)' "$output_root/chronofall/pm-snapshot.json" > "$work/action-snapshot.json"
cmp "$work/local-snapshot.json" "$work/action-snapshot.json" ||
  fail 'Action and local snapshots differ after timestamp normalization.'

unsafe_output_log=$work/unsafe-output.log
if run_action site-build family/chronofall family/chronofall/.pm/site false >"$unsafe_output_log" 2>&1; then
  fail 'The Action accepted a site output beneath .pm.'
fi
grep -F '.pm or its descendants' "$unsafe_output_log" >/dev/null ||
  fail 'Unsafe output diagnostic is missing.'

expected_files=$work/expected-files.txt
actual_files=$work/actual-files.txt
find "$release_root" -type f ! -name PM ! -name '*.pdb' \
  | sed "s#^$release_root/##" | LC_ALL=C sort > "$expected_files"
docker run --rm --entrypoint /bin/sh "$image_tag" -c \
  'find /opt/pm -type f | sed "s#^/opt/pm/##" | sort' > "$actual_files"
diff -u "$expected_files" "$actual_files" || fail 'Container payload differs from the portable release layout.'

docker run --rm --entrypoint /bin/sh "$image_tag" -c '
  test -f /etc/ssl/certs/ca-certificates.crt
  test -f /opt/pm/pm-release.json
  test ! -e /opt/pm/PM
  test -z "$(find /opt/pm -name "*.pdb" -print -quit)"
  test -z "$(find /opt/pm -perm -222 -print -quit)"
  test ! -d /usr/share/dotnet/sdk
  ! command -v git >/dev/null 2>&1
  ! command -v node >/dev/null 2>&1
  ! command -v npm >/dev/null 2>&1
'
container_manifest_version=$(docker run --rm --entrypoint /bin/sh "$image_tag" -c \
  'sed -n '\''s/.*"version":"\([^"]*\)".*/\1/p'\'' /opt/pm/pm-release.json')
[ "$container_manifest_version" = "$pm_version" ] || fail 'The container release manifest reports the wrong PM version.'

config=$(docker image inspect "$image_tag" --format '{{json .Config}}')
[ "$(printf '%s' "$config" | jq -r '.User')" = '0:0' ] || fail 'Runtime image must use root for Docker Action compatibility.'
[ "$(printf '%s' "$config" | jq -r '.WorkingDir')" = '/github/workspace' ] || fail 'Unexpected runtime working directory.'
[ "$(printf '%s' "$config" | jq -c '.Entrypoint')" = '["dotnet","/opt/pm/PM.dll","__github-action"]' ] || fail 'Unexpected runtime entrypoint.'
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
