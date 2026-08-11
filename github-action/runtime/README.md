# PM GitHub Action runtime

This directory builds the Linux runtime used by the reusable PM GitHub Action.
The image launches the portable release payload through PM's private
`__github-action` host. That host accepts the four fixed positional inputs from
`action.template.yml`, validates them without shell evaluation, and invokes only
the approved PM commands.

The checked-in `action.template.yml` remains the source contract. The Action
release workflow publishes a source-addressed OCI candidate and emits a
promotion artifact containing a digest-pinned root `action.yml` and canonical
`github-action/release/current.json`. The artifact is applied locally and
committed with the repository owner's signing key; CI never generates a commit.

Run the complete PM release gate first so `artifacts/release` contains the
framework-dependent CLI with embedded Angular assets:

```sh
cd web
npm run release
cd ..
github-action/runtime/build.sh
github-action/runtime/smoke.sh
```

`build.sh` loads a native Action image for local validation and writes a deterministic
Linux amd64/arm64 OCI archive beneath the ignored `artifacts/github-action`
directory. It reports the native image ID and archive SHA-256 as local identity
evidence. Neither value is presented as the registry manifest digest; PM-0121
owns publication and records the authoritative promoted digest.

On a candidate workflow run, download the named promotion artifact and apply it
from the clean candidate revision:

```sh
github-action/release/apply-promotion.sh path/to/artifact
git add action.yml github-action/release/current.json
git commit -S -m "PM: Promote Action vMAJOR.MINOR.PATCH"
git push origin main
```

The follow-up workflow verifies the commit signature and version-neutral diff,
promotes immutable OCI and Action refs, and gates `latest` and eligible stable-major
channels through the pinned public `chronium/pm-action-smoke` workflow.

The runtime uses the exact digest-pinned .NET 10 ASP.NET Alpine image declared
in `Containerfile`. Alpine's invariant globalization mode is intentional: PM's
CI commands do not require culture-specific collation or formatting, so ICU and
time-zone packages are excluded from this minimal runtime.
