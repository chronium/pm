# PM GitHub Action runtime

This directory builds the raw Linux runtime used by the reusable PM GitHub
Action. The image launches the portable release payload through
`dotnet /opt/pm/PM.dll`; command allowlisting, Action input validation, and
GitHub output handling belong to PM-0119's dispatcher.

Run the complete PM release gate first so `artifacts/release` contains the
framework-dependent CLI with embedded Angular assets:

```sh
cd web
npm run release
cd ..
github-action/runtime/build.sh
github-action/runtime/smoke.sh
```

`build.sh` loads a native image for local validation and writes a deterministic
Linux amd64/arm64 OCI archive beneath the ignored `artifacts/github-action`
directory. It reports the native image ID and archive SHA-256 as local identity
evidence. Neither value is presented as the registry manifest digest; PM-0121
owns publication and records the authoritative promoted digest.

The runtime uses the exact digest-pinned .NET 10 ASP.NET Alpine image declared
in `Containerfile`. Alpine's invariant globalization mode is intentional: PM's
CI commands do not require culture-specific collation or formatting, so ICU and
time-zone packages are excluded from this minimal runtime.
