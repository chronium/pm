# PM Next ID Worker

Cloudflare Worker backing PM's default next-ID service:

```text
https://pm-next-id.apa-prod-monitor.workers.dev
```

## API

- `GET /health` returns plain text `ok`.
- `POST /projects` creates a project and returns JSON: `{ "key": "..." }`.
- `GET /projects/{key}/tracks/{track}/nextid` returns JSON with the allocated ID: `{ "id": 1 }`.
- `GET /projects/{key}/tracks/{track}/peekid` returns JSON with the next ID without incrementing it: `{ "id": 2 }`.

Unknown project keys return `401`. Unknown routes return `404`.

## Trust Model

This is a personal public utility, not a public SaaS API. It has no user authentication and no admin API in this slice. The project key returned by `POST /projects` authorizes ID allocation for that project, and PM stores it at `.dev-pm/next_id.txt`.

For ordinary PM projects, treat `.dev-pm/next_id.txt` as a secret. Anyone who can read it can allocate or peek IDs for that PM project. This repository intentionally publishes its own `.dev-pm/next_id.txt`; do not copy that policy to other projects without reviewing the impact. The Worker stores only a SHA-512 hash of the key, and error handling must avoid logging project keys or request paths.

Abuse controls such as rate limiting, key rotation, admin secrets, and monitoring are intentionally deferred.

## Development

All npm and npx flows in this repo must go through Socket-wrapped scripts where a package runner is needed.

```sh
npm test
npm run dev
npm run deploy
```

The scripts that invoke Wrangler are Socket-wrapped in `package.json`:

```sh
socket npx -- wrangler dev --local
socket npx -- wrangler deploy
```

## D1 Migrations

Run migrations through the package scripts:

```sh
npm run migrate:local
npm run migrate:remote
```

Those resolve to:

```sh
socket npx -- wrangler d1 migrations apply pm-next-id --local
socket npx -- wrangler d1 migrations apply pm-next-id --remote
```

Do not commit generated Worker artifacts such as `node_modules/`, `.wrangler/`, or local D1 database files.
