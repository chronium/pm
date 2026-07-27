# PM Next ID Worker

Cloudflare Worker backing PM's default next-ID service:

```text
https://pm-next-id.chronium.workers.dev
```

## API

- `GET /health` returns plain text `ok`.
- `POST /projects` creates an authenticated project and returns JSON: `{ "projectId": "..." }`.
- `POST /legacy-projects/claim` claims an older bearer-key project into the authenticated model.
- `GET /projects/{projectId}/tracks/{track}/nextid` returns JSON with the allocated ID: `{ "id": 1 }`.
- `GET /projects/{projectId}/tracks/{track}/peekid` returns JSON with the next ID without incrementing it: `{ "id": 2 }`.
- `GET /projects/{projectId}/members` lists members for any authenticated member.
- `GET|POST /projects/{projectId}/invitations` lists active invitations or creates a 24-hour invitation for an admin.
- `POST /projects/{projectId}/invitations/accept` accepts a single-use invitation with a joining identity.
- `DELETE /projects/{projectId}/invitations/{invitationId}` revokes a pending invitation.
- `PATCH|DELETE /projects/{projectId}/members/{userId}` changes a role or removes a member.

Project creation, legacy claim, and ID routes require PM signed-request headers. Unknown projects or invalid signatures return `401`. Unknown routes return `404`.

## Trust Model

This is a personal public utility, not a public SaaS API. PM project files are designed to be public project artifacts. Worker auth only protects hosted operations such as shared ID allocation.

PM stores the public Worker project identifier at `.pm/project_id.txt`. Local user identity and the signing private key live in OS user config, outside `.pm/`. Older `.pm/next_id.txt` bearer-key projects can be claimed into the new model; after claim, PM writes `.pm/project_id.txt`.

Invitation tokens contain 256 bits of randomness, are returned only at creation, and are stored in D1 only as SHA-256 hashes. Acceptance is limited to ten attempts per minute for each project and source. Invitations are role-bound, single-use, revocable, and expire after 24 hours. The Worker prevents concurrent demotion or removal of the final admin.

Recovery-key use, key rotation, organizations, Worker-level administration, and monitoring are intentionally deferred.

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
