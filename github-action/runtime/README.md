# PM GitHub Action runtime

This directory builds the Linux runtime used by the reusable PM GitHub Action.
The image launches the portable release payload through PM's private
`__github-action` host. That host accepts the four fixed positional inputs from
`action.template.yml`, validates them without shell evaluation, and invokes only
the approved PM commands.

The checked-in metadata is deliberately a promotion template rather than a
root `action.yml`. PM-0121 publishes the OCI image, substitutes its immutable
registry digest, and promotes the resulting root metadata with the release.

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

The runtime uses the exact digest-pinned .NET 10 ASP.NET Alpine image declared
in `Containerfile`. Alpine's invariant globalization mode is intentional: PM's
CI commands do not require culture-specific collation or formatting, so ICU and
time-zone packages are excluded from this minimal runtime.
