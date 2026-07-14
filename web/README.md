# PM Web

`pm-web` is the standalone Angular 22 workspace for PM's replacement web client. This infrastructure slice intentionally contains only the routed root application; product routes and UI arrive in later milestones.

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
npm run check       # Strict development build
npm test            # One non-watch Vitest run
npm run test:watch  # Interactive Vitest watch mode
npm run build       # Production build
npm run storybook   # Component workshop on http://localhost:6006
npm run test:storybook  # Headless Chromium interaction and accessibility checks
npm run build-storybook # Static workshop build in ignored storybook-static/
npm run api:types       # Regenerate TypeScript contracts from the runtime OpenAPI document
npm run api:types:check # Fail when committed TypeScript contracts have drifted
```

Install the Chromium binary used by Storybook's browser tests after installing dependencies:

```sh
socket npx playwright install chromium
```

Storybook is a zoneless, development-only workshop and is not part of the .NET build. Reusable visual components should add or update collocated `*.stories.ts` coverage for their meaningful states. Routed containers and feature stores generally should not have stories; keep their behavior in unit or application-level tests instead.

The development proxy forwards `/api` to `http://127.0.0.1:51237`. The Angular workspace is not invoked by the repository's normal .NET build or test commands.

The type-generation commands build PM without restoring packages, start a temporary loopback web host, read `/openapi/v1.json`, and always stop the host. Generated contracts are committed only under `src/app/api/generated/`; API clients remain handwritten.

Run all commands that add, update, or remove dependencies through `socket npm`; do not use plain npm for dependency changes.
