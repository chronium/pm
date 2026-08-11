#!/bin/sh
set -eu

script_dir=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
repository_root=$(CDPATH= cd -- "$script_dir/../.." && pwd)

if [ "$#" -ne 1 ]; then
  printf '%s\n' 'Usage: github-action/release/apply-promotion.sh <promotion-artifact-directory>' >&2
  exit 1
fi

artifact_dir=$(CDPATH= cd -- "$1" && pwd)
[ -f "$artifact_dir/action.yml" ] || { printf '%s\n' 'Promotion artifact has no action.yml.' >&2; exit 1; }
[ -f "$artifact_dir/github-action/release/current.json" ] || {
  printf '%s\n' 'Promotion artifact has no current release metadata.' >&2
  exit 1
}
[ -z "$(git -C "$repository_root" status --porcelain --untracked-files=normal)" ] || {
  printf '%s\n' 'The PM worktree must be clean before applying a promotion.' >&2
  exit 1
}

source_revision=$(node -e 'console.log(JSON.parse(require("fs").readFileSync(process.argv[1], "utf8")).sourceRevision)' \
  "$artifact_dir/github-action/release/current.json")
[ "$(git -C "$repository_root" rev-parse HEAD)" = "$source_revision" ] || {
  printf '%s\n' 'The promotion artifact does not target the current PM revision.' >&2
  exit 1
}

node "$repository_root/github-action/release/release.mjs" render \
  "$artifact_dir/.verification" "$source_revision" \
  "$(node -e 'console.log(JSON.parse(require("fs").readFileSync(process.argv[1], "utf8")).imageDigest)' \
    "$artifact_dir/github-action/release/current.json")"
cmp "$artifact_dir/.verification/action.yml" "$artifact_dir/action.yml"
cmp "$artifact_dir/.verification/github-action/release/current.json" \
  "$artifact_dir/github-action/release/current.json"

cp "$artifact_dir/action.yml" "$repository_root/action.yml"
mkdir -p "$repository_root/github-action/release"
cp "$artifact_dir/github-action/release/current.json" "$repository_root/github-action/release/current.json"
git -C "$repository_root" diff --check

printf '%s\n' 'Applied the verified Action promotion. Review it, then create a signed version-neutral commit.'
