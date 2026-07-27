import assert from "node:assert/strict";
import { webcrypto } from "node:crypto";
import { test } from "node:test";
import { handleRequest } from "../src/worker.js";

const AUTH_VERSION = "pm-auth-v1";

test("health returns ok", async () => {
  const response = await request(new FakeD1(), "GET", "/health");

  assert.equal(response.status, 200);
  assert.equal(await response.text(), "ok");
});

test("project creation stores creator as admin", async () => {
  const db = new FakeD1();
  const identity = await createIdentity();
  const response = await signedJson(db, identity, "POST", "/projects", {
    projectId: "project-1",
    userId: identity.userId,
    displayName: "Chronium",
    publicKey: identity.publicKey,
    recoveryKeyHash: "recovery-hash",
  });

  assert.equal(response.status, 200);
  assert.deepEqual(await response.json(), { projectId: "project-1" });
  assert.equal(db.projects.get("project-1").recoveryKeyHash, "recovery-hash");
  assert.equal(db.members.get(memberKey("project-1", identity.userId)).role, "admin");
});

test("nextid increments per track and peekid does not increment", async () => {
  const db = new FakeD1();
  const identity = await createProject(db, "project-1");

  assert.deepEqual(await json(db, identity, "GET", "/projects/project-1/tracks/BUILD/nextid"), { id: 1 });
  assert.deepEqual(await json(db, identity, "GET", "/projects/project-1/tracks/BUILD/peekid"), { id: 2 });
  assert.deepEqual(await json(db, identity, "GET", "/projects/project-1/tracks/BUILD/nextid"), { id: 2 });
  assert.deepEqual(await json(db, identity, "GET", "/projects/project-1/tracks/RENDER/nextid"), { id: 1 });
});

test("unknown project ids are unauthorized", async () => {
  const db = new FakeD1();
  const identity = await createIdentity();
  const response = await signedJson(db, identity, "GET", "/projects/missing/tracks/BUILD/nextid");

  assert.equal(response.status, 401);
});

test("non-members are unauthorized", async () => {
  const db = new FakeD1();
  await createProject(db, "project-1");
  const stranger = await createIdentity("stranger");
  const response = await signedJson(db, stranger, "GET", "/projects/project-1/tracks/BUILD/nextid");

  assert.equal(response.status, 401);
});

test("replayed nonces are unauthorized", async () => {
  const db = new FakeD1();
  const identity = await createProject(db, "project-1");
  const nonce = "fixed-nonce";

  assert.equal((await signedJson(db, identity, "GET", "/projects/project-1/tracks/BUILD/nextid", undefined, { nonce })).status, 200);
  assert.equal((await signedJson(db, identity, "GET", "/projects/project-1/tracks/BUILD/nextid", undefined, { nonce })).status, 401);
});

test("bad signatures are unauthorized", async () => {
  const db = new FakeD1();
  const identity = await createProject(db, "project-1");
  const other = await createIdentity("other");
  const response = await signedJson(db, other, "GET", "/projects/project-1/tracks/BUILD/nextid", undefined, {
    userId: identity.userId,
  });

  assert.equal(response.status, 401);
});

test("legacy claim preserves counters", async () => {
  const db = new FakeD1();
  const legacyKey = base64UrlEncode(webcrypto.getRandomValues(new Uint8Array(64)));
  const keyHash = await sha512Hex(base64UrlDecode(legacyKey));
  db.legacyProjects.set(keyHash, { keyHash, project_id: null });
  db.legacyCounters.set(counterKey(keyHash, "BUILD"), 8);

  const identity = await createIdentity();
  const response = await signedJson(db, identity, "POST", "/legacy-projects/claim", {
    projectId: "claimed-project",
    legacyKey,
    userId: identity.userId,
    displayName: "Chronium",
    publicKey: identity.publicKey,
    recoveryKeyHash: "recovery-hash",
  });

  assert.equal(response.status, 200);
  assert.deepEqual(await response.json(), { projectId: "claimed-project" });
  assert.deepEqual(await json(db, identity, "GET", "/projects/claimed-project/tracks/BUILD/peekid"), { id: 8 });
});

test("unknown legacy keys cannot be claimed", async () => {
  const db = new FakeD1();
  const identity = await createIdentity();
  const response = await signedJson(db, identity, "POST", "/legacy-projects/claim", {
    projectId: "claimed-project",
    legacyKey: "bm90LXJlZ2lzdGVyZWQ",
    userId: identity.userId,
    displayName: "Chronium",
    publicKey: identity.publicKey,
    recoveryKeyHash: "recovery-hash",
  });

  assert.equal(response.status, 401);
});

test("already claimed legacy keys with no members can recover the first admin", async () => {
  const db = new FakeD1();
  const legacyKey = base64UrlEncode(webcrypto.getRandomValues(new Uint8Array(64)));
  const keyHash = await sha512Hex(base64UrlDecode(legacyKey));
  db.legacyProjects.set(keyHash, { keyHash, project_id: "claimed-project" });
  db.projects.set("claimed-project", { projectId: "claimed-project", recoveryKeyHash: "recovery-hash" });

  const identity = await createIdentity();
  const response = await signedJson(db, identity, "POST", "/legacy-projects/claim", {
    projectId: "other-project",
    legacyKey,
    userId: identity.userId,
    displayName: "Chronium",
    publicKey: identity.publicKey,
    recoveryKeyHash: "recovery-hash",
  });

  assert.equal(response.status, 200);
  assert.deepEqual(await response.json(), { projectId: "claimed-project" });
  assert.equal(db.members.has(memberKey("claimed-project", identity.userId)), true);
});

test("already claimed legacy keys with members cannot add another admin", async () => {
  const db = new FakeD1();
  const legacyKey = base64UrlEncode(webcrypto.getRandomValues(new Uint8Array(64)));
  const keyHash = await sha512Hex(base64UrlDecode(legacyKey));
  db.legacyProjects.set(keyHash, { keyHash, project_id: "claimed-project" });
  db.projects.set("claimed-project", { projectId: "claimed-project", recoveryKeyHash: "recovery-hash" });
  db.members.set(memberKey("claimed-project", "existing-user"), {
    project_id: "claimed-project",
    user_id: "existing-user",
    display_name: "Existing",
    public_key: "public-key",
    role: "admin",
  });

  const identity = await createIdentity();
  const response = await signedJson(db, identity, "POST", "/legacy-projects/claim", {
    projectId: "other-project",
    legacyKey,
    userId: identity.userId,
    displayName: "Chronium",
    publicKey: identity.publicKey,
    recoveryKeyHash: "recovery-hash",
  });

  assert.equal(response.status, 401);
  assert.equal(db.members.has(memberKey("claimed-project", identity.userId)), false);
});

test("unknown routes are not found", async () => {
  const response = await request(new FakeD1(), "GET", "/projects/abc");

  assert.equal(response.status, 404);
});

test("malformed track routes are not found", async () => {
  const response = await request(new FakeD1(), "GET", "/projects/abc/tracks/%/nextid");

  assert.equal(response.status, 404);
});

test("members can list members but only admins can manage invitations", async () => {
  const db = new FakeD1();
  const admin = await createProject(db, "project-1");
  const user = await createIdentity("user-2");
  db.members.set(memberKey("project-1", user.userId), member("project-1", user, "User", "user"));

  const listed = await json(db, user, "GET", "/projects/project-1/members");
  assert.equal(listed.currentUserId, "user-2");
  assert.equal(listed.currentRole, "user");
  assert.equal(listed.members.length, 2);

  const denied = await signedJson(db, user, "POST", "/projects/project-1/invitations", { role: "user" });
  assert.equal(denied.status, 403);
  assert.equal((await denied.json()).errorCode, "admin_required");
  assert.equal((await signedJson(db, admin, "POST", "/projects/project-1/invitations", { role: "invalid" })).status, 400);
});

test("invitations store only hashes, expire after 24 hours, and list without secrets", async () => {
  const db = new FakeD1();
  const admin = await createProject(db, "project-1");
  const response = await signedJson(db, admin, "POST", "/projects/project-1/invitations", { role: "admin" });
  assert.equal(response.status, 201);
  const created = await response.json();
  assert.match(created.token, /^pmi_[A-Za-z0-9_-]{43}$/);
  assert.equal(created.invitation.role, "admin");
  assert.equal(Date.parse(created.invitation.expiresAt) - Date.parse(created.invitation.createdAt), 86_400_000);

  const stored = [...db.invitations.values()][0];
  assert.notEqual(stored.token_hash, created.token);
  assert.equal(stored.token_hash, await sha256Hex(new TextEncoder().encode(created.token)));
  const listed = await json(db, admin, "GET", "/projects/project-1/invitations");
  assert.equal(listed.invitations.length, 1);
  assert.equal(JSON.stringify(listed).includes("token"), false);
});

test("invitation acceptance is signed, single-use, cross-project safe, and idempotent for one identity", async () => {
  const db = new FakeD1();
  const admin = await createProject(db, "project-1");
  await createProject(db, "project-2");
  const created = await (await signedJson(db, admin, "POST", "/projects/project-1/invitations", { role: "user" })).json();
  const joining = await createIdentity("joining-user");
  const payload = { token: created.token, userId: joining.userId, displayName: "Linux user", publicKey: joining.publicKey };

  assert.equal((await signedJson(db, joining, "POST", "/projects/project-2/invitations/accept", payload)).status, 400);
  const accepted = await signedJson(db, joining, "POST", "/projects/project-1/invitations/accept", payload);
  assert.equal(accepted.status, 200);
  assert.equal((await accepted.json()).member.role, "user");
  assert.equal((await signedJson(db, joining, "POST", "/projects/project-1/invitations/accept", payload)).status, 200);

  const other = await createIdentity("other-user");
  const replay = await signedJson(db, other, "POST", "/projects/project-1/invitations/accept", {
    ...payload, userId: other.userId, publicKey: other.publicKey,
  });
  assert.equal(replay.status, 400);
  assert.equal((await replay.json()).errorCode, "invalid_invitation");
});

test("revoked and expired invitations cannot be accepted", async () => {
  const db = new FakeD1();
  const admin = await createProject(db, "project-1");
  const created = await (await signedJson(db, admin, "POST", "/projects/project-1/invitations", { role: "user" })).json();
  assert.equal((await signedJson(db, admin, "DELETE", `/projects/project-1/invitations/${created.invitation.invitationId}`)).status, 204);
  const joining = await createIdentity("joining-user");
  const payload = { token: created.token, userId: joining.userId, displayName: "Joiner", publicKey: joining.publicKey };
  assert.equal((await signedJson(db, joining, "POST", "/projects/project-1/invitations/accept", payload)).status, 400);

  const expired = await (await signedJson(db, admin, "POST", "/projects/project-1/invitations", { role: "user" })).json();
  db.invitations.get(expired.invitation.invitationId).expires_at = Math.floor(Date.now() / 1000) - 1;
  assert.equal((await signedJson(db, joining, "POST", "/projects/project-1/invitations/accept", { ...payload, token: expired.token })).status, 400);
});

test("role updates and removals protect the final admin", async () => {
  const db = new FakeD1();
  const admin = await createProject(db, "project-1");
  assert.equal((await signedJson(db, admin, "PATCH", `/projects/project-1/members/${admin.userId}`, { role: "user" })).status, 409);
  assert.equal((await signedJson(db, admin, "DELETE", `/projects/project-1/members/${admin.userId}`)).status, 409);

  const second = await createIdentity("admin-2");
  db.members.set(memberKey("project-1", second.userId), member("project-1", second, "Second", "admin"));
  assert.equal((await signedJson(db, admin, "PATCH", `/projects/project-1/members/${second.userId}`, { role: "user" })).status, 200);
  assert.equal((await signedJson(db, admin, "DELETE", `/projects/project-1/members/${second.userId}`)).status, 204);
});

test("invitation acceptance is rate limited per project and source", async () => {
  const db = new FakeD1();
  const joining = await createIdentity("joining-user");
  const limiter = { limit: async () => ({ success: false }) };
  const response = await signedJson(db, joining, "POST", "/projects/project-1/invitations/accept", {
    token: "pmi_invalid", userId: joining.userId, displayName: "Joiner", publicKey: joining.publicKey,
  }, { env: { INVITATION_ACCEPT_RATE_LIMITER: limiter } });
  assert.equal(response.status, 429);
  assert.equal((await response.json()).errorCode, "rate_limited");
});

async function createProject(db, projectId) {
  const identity = await createIdentity();
  const response = await signedJson(db, identity, "POST", "/projects", {
    projectId,
    userId: identity.userId,
    displayName: "Chronium",
    publicKey: identity.publicKey,
    recoveryKeyHash: "recovery-hash",
  });
  assert.equal(response.status, 200);
  return identity;
}

async function json(db, identity, method, path) {
  const response = await signedJson(db, identity, method, path);
  assert.equal(response.status, 200);
  assert.match(response.headers.get("content-type"), /^application\/json\b/);
  return await response.json();
}

async function signedJson(db, identity, method, path, body, options = {}) {
  const bodyText = body === undefined ? "" : JSON.stringify(body);
  const timestamp = String(options.timestamp ?? Math.floor(Date.now() / 1000));
  const nonce = options.nonce ?? crypto.randomUUID();
  const userId = options.userId ?? identity.userId;
  const canonical = await canonicalRequest(method, path, timestamp, nonce, userId, bodyText);
  const signature = await webcrypto.subtle.sign(
    { name: "ECDSA", hash: "SHA-256" },
    identity.privateKey,
    new TextEncoder().encode(canonical),
  );

  return request(db, method, path, {
    body: bodyText || undefined,
    headers: {
      "content-type": "application/json",
      "PM-User-Id": userId,
      "PM-Timestamp": timestamp,
      "PM-Nonce": nonce,
      "PM-Signature": base64UrlEncode(new Uint8Array(signature)),
      "PM-Public-Key": identity.publicKey,
    },
  }, options.env);
}

function request(db, method, path, init = {}, env = {}) {
  return handleRequest(new Request(`https://next-id.test${path}`, { method, ...init }), { DB: db, ...env });
}

async function createIdentity(userId = "user-1") {
  const keys = await webcrypto.subtle.generateKey(
    { name: "ECDSA", namedCurve: "P-256" },
    true,
    ["sign", "verify"],
  );
  const publicKey = await webcrypto.subtle.exportKey("spki", keys.publicKey);
  return {
    userId,
    privateKey: keys.privateKey,
    publicKey: base64UrlEncode(new Uint8Array(publicKey)),
  };
}

async function canonicalRequest(method, pathname, timestamp, nonce, userId, body) {
  const bodyHash = await sha256Hex(new TextEncoder().encode(body));
  return [AUTH_VERSION, method.toUpperCase(), pathname, timestamp, nonce, userId, bodyHash].join("\n");
}

class FakeD1 {
  projects = new Map();
  legacyProjects = new Map();
  members = new Map();
  nonces = new Set();
  counters = new Map();
  legacyCounters = new Map();
  invitations = new Map();

  prepare(sql) {
    return new FakeStatement(this, sql);
  }

  async batch(statements) {
    const results = [];
    for (const statement of statements) {
      results.push(await statement.run());
    }
    return results;
  }
}

class FakeStatement {
  #db;
  #sql;
  #bindings = [];

  constructor(db, sql) {
    this.#db = db;
    this.#sql = sql;
  }

  bind(...bindings) {
    this.#bindings = bindings;
    return this;
  }

  async run() {
    if (this.#sql.includes("INSERT INTO projects(key_hash, project_id")) {
      const [, projectId, recoveryKeyHash] = this.#bindings;
      if (this.#db.projects.has(projectId)) throw new Error("UNIQUE constraint failed");
      this.#db.projects.set(projectId, { projectId, recoveryKeyHash });
      return { results: [] };
    }

    if (this.#sql.includes("SELECT key_hash, project_id")) {
      const [keyHash] = this.#bindings;
      const project = this.#db.legacyProjects.get(keyHash);
      return { results: project ? [project] : [] };
    }

    if (this.#sql.includes("UPDATE projects")) {
      const [keyHash, projectId, recoveryKeyHash] = this.#bindings;
      const legacy = this.#db.legacyProjects.get(keyHash);
      if (!legacy || legacy.project_id) return { results: [] };
      legacy.project_id = projectId;
      this.#db.projects.set(projectId, { projectId, recoveryKeyHash });
      return { results: [{ project_id: projectId }] };
    }

    if (this.#sql.includes("FROM legacy_project_counters")) {
      const [keyHash, projectId] = this.#bindings;
      for (const [key, nextId] of this.#db.legacyCounters) {
        if (!key.startsWith(`${keyHash}:`)) continue;
        const track = key.slice(keyHash.length + 1);
        const newKey = counterKey(projectId, track);
        if (!this.#db.counters.has(newKey)) this.#db.counters.set(newKey, nextId);
      }
      return { results: [] };
    }

    if (this.#sql.includes("SELECT user_id, display_name, public_key, role")) {
      const [projectId] = this.#bindings;
      return { results: [...this.#db.members.values()]
        .filter(value => value.project_id === projectId)
        .sort((left, right) => left.display_name.localeCompare(right.display_name)) };
    }

    if (this.#sql.includes("INSERT INTO project_members") && this.#sql.includes("FROM project_invitations")) {
      const [projectId, tokenHash, userId, displayName, publicKey] = this.#bindings;
      const invitation = [...this.#db.invitations.values()].find(value =>
        value.project_id === projectId && value.token_hash === tokenHash &&
        value.consumed_by_user_id === userId && value.revoked_at == null);
      const key = memberKey(projectId, userId);
      if (invitation && !this.#db.members.has(key))
        this.#db.members.set(key, { project_id: projectId, user_id: userId, display_name: displayName, public_key: publicKey, role: invitation.role });
      return { results: [] };
    }

    if (this.#sql.includes("INSERT INTO project_members")) {
      const [projectId, userId, displayName, publicKey, role] = this.#bindings;
      const key = memberKey(projectId, userId);
      this.#db.members.set(key, { project_id: projectId, user_id: userId, display_name: displayName, public_key: publicKey, role });
      return { results: [] };
    }

    if (this.#sql.includes("SELECT public_key, role")) {
      const [projectId, userId] = this.#bindings;
      const member = this.#db.members.get(memberKey(projectId, userId));
      return { results: member ? [member] : [] };
    }

    if (this.#sql.includes("COUNT(*) AS count")) {
      const [projectId] = this.#bindings;
      const count = [...this.#db.members.values()].filter(member => member.project_id === projectId).length;
      return { results: [{ count }] };
    }

    if (this.#sql.includes("INSERT INTO request_nonces")) {
      const [userId, nonce] = this.#bindings;
      const key = `${userId}:${nonce}`;
      if (this.#db.nonces.has(key)) throw new Error("UNIQUE constraint failed");
      this.#db.nonces.add(key);
      return { results: [] };
    }

    if (this.#sql.includes("INSERT INTO project_invitations")) {
      const [invitationId, projectId, tokenHash, role, createdBy, createdAt, expiresAt] = this.#bindings;
      this.#db.invitations.set(invitationId, {
        invitation_id: invitationId, project_id: projectId, token_hash: tokenHash, role,
        created_by_user_id: createdBy, created_at: createdAt, expires_at: expiresAt,
        consumed_at: null, consumed_by_user_id: null, revoked_at: null,
      });
      return { results: [] };
    }

    if (this.#sql.includes("SELECT invitation_id, role, created_by_user_id")) {
      const [projectId, now] = this.#bindings;
      return { results: [...this.#db.invitations.values()].filter(value =>
        value.project_id === projectId && value.consumed_at == null && value.revoked_at == null && value.expires_at > now) };
    }

    if (this.#sql.includes("SET revoked_at")) {
      const [projectId, invitationId, now] = this.#bindings;
      const value = this.#db.invitations.get(invitationId);
      if (!value || value.project_id !== projectId || value.consumed_at != null || value.revoked_at != null || value.expires_at <= now)
        return { results: [] };
      value.revoked_at = now;
      return { results: [{ invitation_id: invitationId }] };
    }

    if (this.#sql.includes("SET consumed_at")) {
      const [projectId, tokenHash, userId, now, publicKey] = this.#bindings;
      const value = [...this.#db.invitations.values()].find(invitation =>
        invitation.project_id === projectId && invitation.token_hash === tokenHash);
      const existing = this.#db.members.get(memberKey(projectId, userId));
      if (!value || value.revoked_at != null || (existing && existing.public_key !== publicKey) ||
          !((value.consumed_at == null && value.expires_at > now) || value.consumed_by_user_id === userId))
        return { results: [] };
      value.consumed_at ??= now;
      value.consumed_by_user_id ??= userId;
      return { results: [{ role: value.role }] };
    }

    if (this.#sql.includes("SET role = ?3")) {
      const [projectId, userId, role] = this.#bindings;
      const value = this.#db.members.get(memberKey(projectId, userId));
      const otherAdmin = [...this.#db.members.values()].some(member =>
        member.project_id === projectId && member.user_id !== userId && member.role === "admin");
      if (!value || (value.role === "admin" && role !== "admin" && !otherAdmin)) return { results: [] };
      value.role = role;
      return { results: [value] };
    }

    if (this.#sql.includes("DELETE FROM project_members")) {
      const [projectId, userId] = this.#bindings;
      const key = memberKey(projectId, userId);
      const value = this.#db.members.get(key);
      const otherAdmin = [...this.#db.members.values()].some(member =>
        member.project_id === projectId && member.user_id !== userId && member.role === "admin");
      if (!value || (value.role === "admin" && !otherAdmin)) return { results: [] };
      this.#db.members.delete(key);
      return { results: [{ user_id: userId }] };
    }

    if (this.#sql.includes("INSERT INTO project_counters")) {
      const [projectId, track] = this.#bindings;
      if (this.#db.projects.has(projectId) && !this.#db.counters.has(counterKey(projectId, track))) {
        this.#db.counters.set(counterKey(projectId, track), 1);
      }
      return { results: [] };
    }

    if (this.#sql.includes("SELECT project_id") && this.#sql.includes("WHERE project_id")) {
      const [projectId] = this.#bindings;
      const project = this.#db.projects.get(projectId);
      return { results: project ? [project] : [] };
    }

    if (this.#sql.includes("RETURNING next_id - 1 AS id")) {
      const [projectId, track] = this.#bindings;
      const key = counterKey(projectId, track);
      const nextId = this.#db.counters.get(key);
      if (nextId === undefined) return { results: [] };

      this.#db.counters.set(key, nextId + 1);
      return { results: [{ id: nextId }] };
    }

    if (this.#sql.includes("SELECT next_id AS id")) {
      const [projectId, track] = this.#bindings;
      const nextId = this.#db.counters.get(counterKey(projectId, track));
      return { results: nextId === undefined ? [] : [{ id: nextId }] };
    }

    throw new Error(`Unexpected SQL: ${this.#sql}`);
  }
}

function memberKey(projectId, userId) {
  return `${projectId}:${userId}`;
}

function counterKey(projectId, track) {
  return `${projectId}:${track}`;
}

function member(projectId, identity, displayName, role) {
  return { project_id: projectId, user_id: identity.userId, display_name: displayName, public_key: identity.publicKey, role };
}

function base64UrlEncode(bytes) {
  return Buffer.from(bytes).toString("base64url");
}

function base64UrlDecode(value) {
  return new Uint8Array(Buffer.from(value, "base64url"));
}

async function sha256Hex(bytes) {
  const digest = await webcrypto.subtle.digest("SHA-256", bytes);
  return [...new Uint8Array(digest)].map((byte) => byte.toString(16).padStart(2, "0")).join("");
}

async function sha512Hex(bytes) {
  const digest = await webcrypto.subtle.digest("SHA-512", bytes);
  return [...new Uint8Array(digest)].map((byte) => byte.toString(16).padStart(2, "0")).join("");
}
