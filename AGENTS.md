## Dotnet CLI Usage

In the codex sandbox, run dotnet commands that build in single node mode and without restore.
Use `-m:1 --no-restore` for `dotnet build` and for any command that triggers a build, such as `dotnet test`.
If you do not do this, the builds may sit indefinitely and produce no output.
