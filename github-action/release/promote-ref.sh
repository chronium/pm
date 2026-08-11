#!/bin/sh
set -eu

fail() {
  printf '%s\n' "$1" >&2
  exit 1
}

if [ "$#" -lt 4 ] || [ "$#" -gt 5 ]; then
  fail 'Usage: promote-ref.sh <repository> <ref> <target-sha> <immutable|channel|restore> [previous-sha]'
fi
repository=$1
ref=$2
target=$3
mode=$4
previous=${5:-}
printf '%s' "$ref" | grep -Eq '^(latest|v[0-9]+|v[0-9]+\.[0-9]+\.[0-9]+)$' || fail 'Unsupported Action ref.'
printf '%s' "$target" | grep -Eq '^[0-9a-f]{40}$' || fail 'Target must be a full Git commit SHA.'

lookup() {
  if result=$(gh api "repos/$repository/git/ref/tags/$ref" --jq '.object.sha' 2>/dev/null); then
    printf '%s\n' "$result"
  fi
}

current=$(lookup)
case "$mode" in
  immutable)
    if [ -n "$current" ] && [ "$current" != "$target" ]; then
      fail "Immutable ref $ref already identifies a different commit."
    fi
    if [ -z "$current" ]; then
      gh api --method POST "repos/$repository/git/refs" \
        -f "ref=refs/tags/$ref" -f "sha=$target" >/dev/null
    fi
    ;;
  channel)
    if [ -z "$current" ]; then
      gh api --method POST "repos/$repository/git/refs" \
        -f "ref=refs/tags/$ref" -f "sha=$target" >/dev/null
    elif [ "$current" != "$target" ]; then
      gh api --method PATCH "repos/$repository/git/refs/tags/$ref" \
        -f "sha=$target" -F force=true >/dev/null
    fi
    if [ -n "${GITHUB_OUTPUT:-}" ]; then
      printf 'previous-target=%s\n' "${current:-none}" >> "$GITHUB_OUTPUT"
    fi
    ;;
  restore)
    [ -n "$previous" ] || fail 'Restoring a channel requires its previous target or none.'
    if [ "$previous" = none ]; then
      if [ -n "$current" ]; then
        gh api --method DELETE "repos/$repository/git/refs/tags/$ref" >/dev/null
      fi
    else
      printf '%s' "$previous" | grep -Eq '^[0-9a-f]{40}$' || fail 'Previous target must be a full Git commit SHA or none.'
      if [ -z "$current" ]; then
        gh api --method POST "repos/$repository/git/refs" \
          -f "ref=refs/tags/$ref" -f "sha=$previous" >/dev/null
      elif [ "$current" != "$previous" ]; then
        gh api --method PATCH "repos/$repository/git/refs/tags/$ref" \
          -f "sha=$previous" -F force=true >/dev/null
      fi
    fi
    ;;
  *) fail 'Unknown ref promotion mode.' ;;
esac

if [ "$mode" != restore ]; then
  [ "$(lookup)" = "$target" ] || fail "Ref $ref did not resolve to the intended commit."
fi
