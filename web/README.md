# PM Web

`pm-web` is the standalone Angular 22 workspace for PM's replacement web client. It currently provides the mounted task board and routed task create/detail/edit dialogs, with the wiki and settings feature work continuing in later milestones.

Task dialog URLs (`/tasks/new` and `/tasks/:taskId`) preserve active board query filters. Reads use the generated API contracts and strong ETags; edit, state, and removal mutations always send the current `If-Match` value. Task descriptions use the locally bundled EasyMDE editor and a shared sanitized Markdown renderer.

## Prerequisites

- Node `26.5.0` (pinned by the repository `.node-version`)
- npm 11
- Socket CLI for dependency installation and changes

Install the lockfile through Socket:

```sh
socket npm install
```

## Commands

```sh
npm start           # Development server with the /api proxy
npm run format      # Format handwritten workspace files with Prettier
npm run format:check # Verify formatting without changing files
npm run check       # Strict development build
npm test            # One non-watch Vitest run
npm run test:watch  # Interactive Vitest watch mode
npm run build       # Production build
npm run storybook   # Component workshop on http://localhost:6006
npm run test:storybook  # Headless Chromium interaction and accessibility checks
npm run build-storybook # Static workshop build in ignored storybook-static/
npm run api:types       # Regenerate TypeScript contracts from the runtime OpenAPI document
npm run api:types:check # Fail when committed TypeScript contracts have drifted
npm run e2e              # Desktop and mobile Chromium workflows with disposable projects
npm run e2e:embedded     # Smoke-test the published embedded host
npm run frontend:validate # Complete frontend quality gate
npm run release           # Locked install, all gates, publish, and embedded smoke
```

Install the Chromium binary used by Storybook's browser tests after installing dependencies:

```sh
socket npx playwright install chromium
```

Storybook is a zoneless, development-only workshop and is not part of the .NET build. Reusable visual components should add or update collocated `*.stories.ts` coverage for their meaningful states. Routed containers and feature stores generally should not have stories; keep their behavior in unit or application-level tests instead.

The development proxy forwards `/api` to `http://127.0.0.1:51237`. Start PM in API-only mode in another terminal for local development:

```sh
dotnet PM/bin/Debug/net10.0/PM.dll web --api
cd web
npm start
```

`pm web --api` serves only `/api/v1` and `/openapi/{documentName}.json`, does not open a browser, and accepts `--port` when a different proxy target is needed. The Angular workspace is not invoked by the repository's normal .NET build or test commands.

Angular is the default for both `pm web` and `pm web --ui angular`. It requires assets embedded into the running assembly and fails before binding when they are absent; it never falls back silently. The previous server-rendered interface remains available for one stability release through the explicit `pm web --ui legacy` escape hatch.

```sh
cd web
npm run build
cd ..
dotnet publish PM/PM.csproj -p:EmbedAngularAssets=true
```

Run the DLL from the selected publish output directory. The standard release gate writes it to `artifacts/release/`, so that artifact starts with:

```sh
dotnet artifacts/release/PM.dll web
```

Embedding is opt-in, and ordinary .NET builds ignore the local, uncommitted `web/dist` directory.

Playwright starts either the API plus Angular dev server or the published embedded host, creates a disposable small or 180-task project, isolates identity storage, and uses dynamically assigned loopback ports plus a deterministic fake next-ID service. `PM_E2E_ID_PORT`, `PM_E2E_API_PORT`, and `PM_E2E_UI_PORT` can override those ports for troubleshooting. All children and fixtures are cleaned up on success, failure, and interruption. The embedded profile also rejects browser requests to non-loopback hosts. Install Chromium with `socket npx playwright install chromium` before running browser gates.

Socket findings stop the release install. Any risk acceptance must be an explicit manual action outside the release script. Dependency changes still require an explicit `socket npm install` flow and lockfile review.

The type-generation commands build PM without restoring packages, start a temporary loopback web host, read `/openapi/v1.json`, and always stop the host. Generated contracts are committed only under `src/app/api/generated/`; API clients remain handwritten.

Run all commands that add, update, or remove dependencies through `socket npm`; do not use plain npm for dependency changes.
