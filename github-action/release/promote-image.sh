#!/bin/sh
set -eu

fail() {
  printf '%s\n' "$1" >&2
  exit 1
}

if [ "$#" -ne 2 ]; then
  fail 'Usage: promote-image.sh <vMAJOR.MINOR.PATCH> <sha256:digest>'
fi
version_ref=$1
digest=$2
printf '%s' "$version_ref" | grep -Eq '^v(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)$' ||
  fail 'OCI version ref must be canonical vMAJOR.MINOR.PATCH.'
printf '%s' "$digest" | grep -Eq '^sha256:[0-9a-f]{64}$' || fail 'OCI digest must be canonical.'

image=ghcr.io/chronium/pm
target=$image:$version_ref
current=$(docker buildx imagetools inspect "$target" 2>/dev/null \
  | awk '$1 == "Digest:" { print $2; exit }') || true
if [ -n "$current" ] && [ "$current" != "$digest" ]; then
  fail "Immutable OCI ref $target already identifies a different digest."
fi
if [ -z "$current" ]; then
  docker buildx imagetools create --tag "$target" "$image@$digest" >/dev/null
fi
resolved=$(docker buildx imagetools inspect "$target" \
  | awk '$1 == "Digest:" { print $2; exit }')
[ "$resolved" = "$digest" ] || fail 'Immutable OCI version did not resolve to the promoted digest.'
printf 'Promoted OCI version: %s@%s\n' "$target" "$digest"
