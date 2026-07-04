const INSERT_PROJECT_SQL = `
INSERT INTO projects(key_hash, project_id, recovery_key_hash)
VALUES(?1, ?2, ?3);
`;
const FIND_PROJECT_SQL = `
SELECT project_id
FROM projects
WHERE project_id = ?1;
`;
const FIND_LEGACY_PROJECT_SQL = `
SELECT key_hash, project_id
FROM projects
WHERE key_hash = ?1;
`;
const CLAIM_LEGACY_PROJECT_SQL = `
UPDATE projects
SET project_id = ?2, recovery_key_hash = ?3
WHERE key_hash = ?1 AND project_id IS NULL
RETURNING project_id;
`;
const COPY_LEGACY_COUNTERS_SQL = `
INSERT INTO project_counters(project_id, track, next_id)
SELECT ?2, track, next_id
FROM legacy_project_counters
WHERE key_hash = ?1
ON CONFLICT(project_id, track) DO NOTHING;
`;
const INSERT_MEMBER_SQL = `
INSERT INTO project_members(project_id, user_id, display_name, public_key, role)
VALUES(?1, ?2, ?3, ?4, ?5)
ON CONFLICT(project_id, user_id) DO UPDATE SET
    display_name = excluded.display_name,
    public_key = excluded.public_key,
    role = excluded.role;
`;
const FIND_MEMBER_SQL = `
SELECT public_key, role
FROM project_members
WHERE project_id = ?1 AND user_id = ?2;
`;
const COUNT_MEMBERS_SQL = `
SELECT COUNT(*) AS count
FROM project_members
WHERE project_id = ?1;
`;
const INSERT_NONCE_SQL = `
INSERT INTO request_nonces(user_id, nonce, timestamp)
VALUES(?1, ?2, ?3);
`;
const INSERT_COUNTER_SQL = `
INSERT INTO project_counters(project_id, track, next_id)
SELECT project_id, ?2, 1
FROM projects
WHERE project_id = ?1
ON CONFLICT(project_id, track) DO NOTHING;
`;
const INCREMENT_COUNTER_SQL = `
UPDATE project_counters
SET next_id = next_id + 1
WHERE project_id = ?1 AND track = ?2
RETURNING next_id - 1 AS id;
`;
const PEEK_COUNTER_SQL = `
SELECT next_id AS id
FROM project_counters
WHERE project_id = ?1 AND track = ?2;
`;

const AUTH_VERSION = "pm-auth-v1";
const MAX_CLOCK_SKEW_SECONDS = 300;

export default {
  fetch(request, env) {
    return handleRequest(request, env);
  },
};

export async function handleRequest(request, env) {
  try {
    const url = new URL(request.url);
    const route = parseRoute(url.pathname);
    const body = await request.text();

    if (request.method === "GET" && route.kind === "health") {
      return new Response("ok");
    }

    if (request.method === "POST" && route.kind === "projects") {
      return await createProject(env.DB, request, url, body);
    }

    if (request.method === "POST" && route.kind === "legacyClaim") {
      return await claimLegacyProject(env.DB, request, url, body);
    }

    if (request.method === "GET" && route.kind === "nextid") {
      return await nextId(env.DB, request, url, body, route.projectId, route.track);
    }

    if (request.method === "GET" && route.kind === "peekid") {
      return await peekId(env.DB, request, url, body, route.projectId, route.track);
    }

    return textResponse("not found", 404);
  } catch (error) {
    logInternalError(error);
    return textResponse("internal server error", 500);
  }
}

function parseRoute(pathname) {
  if (pathname === "/health") return { kind: "health" };
  if (pathname === "/projects") return { kind: "projects" };
  if (pathname === "/legacy-projects/claim") return { kind: "legacyClaim" };

  const parts = pathname.split("/").filter(Boolean);
  if (
    parts.length === 5 &&
    parts[0] === "projects" &&
    parts[2] === "tracks" &&
    (parts[4] === "nextid" || parts[4] === "peekid")
  ) {
    const projectId = safeDecodeURIComponent(parts[1]);
    const track = safeDecodeURIComponent(parts[3]);
    if (projectId === null || track === null) return { kind: "unknown" };

    return { kind: parts[4], projectId, track };
  }

  return { kind: "unknown" };
}

async function createProject(db, request, url, body) {
  const auth = await validateSignedJsonRequest(db, request, url, body, null);
  if (!auth.ok) return textResponse("unauthorized", 401);

  const payload = parseJson(body);
  if (!payload) return textResponse("bad request", 400);

  const projectId = requireString(payload.projectId);
  const displayName = requireString(payload.displayName);
  const publicKey = requireString(payload.publicKey);
  const recoveryKeyHash = requireString(payload.recoveryKeyHash);
  if (!projectId || !displayName || !publicKey || !recoveryKeyHash) return textResponse("bad request", 400);

  if (auth.userId !== payload.userId || auth.publicKey !== publicKey) return textResponse("unauthorized", 401);

  await db.prepare(INSERT_PROJECT_SQL).bind(await sha512Hex(new TextEncoder().encode(`auth:${projectId}`)), projectId, recoveryKeyHash).run();
  await db.prepare(INSERT_MEMBER_SQL).bind(projectId, auth.userId, displayName, publicKey, "admin").run();
  return jsonResponse({ projectId });
}

async function claimLegacyProject(db, request, url, body) {
  const auth = await validateSignedJsonRequest(db, request, url, body, null);
  if (!auth.ok) return textResponse("unauthorized", 401);

  const payload = parseJson(body);
  if (!payload) return textResponse("bad request", 400);

  const projectId = requireString(payload.projectId);
  const legacyKey = requireString(payload.legacyKey);
  const displayName = requireString(payload.displayName);
  const publicKey = requireString(payload.publicKey);
  const recoveryKeyHash = requireString(payload.recoveryKeyHash);
  if (!projectId || !legacyKey || !displayName || !publicKey || !recoveryKeyHash) return textResponse("bad request", 400);
  if (auth.userId !== payload.userId || auth.publicKey !== publicKey) return textResponse("unauthorized", 401);

  const keyHash = await keyHashFromProjectKey(legacyKey);
  if (keyHash === null) return textResponse("unauthorized", 401);

  const legacy = firstRow(await db.prepare(FIND_LEGACY_PROJECT_SQL).bind(keyHash).run());
  if (!legacy) return textResponse("unauthorized", 401);
  if (legacy.project_id) {
    const members = firstRow(await db.prepare(COUNT_MEMBERS_SQL).bind(legacy.project_id).run());
    if ((members?.count ?? 0) > 0) return textResponse("unauthorized", 401);

    await db.prepare(INSERT_MEMBER_SQL).bind(legacy.project_id, auth.userId, displayName, publicKey, "admin").run();
    return jsonResponse({ projectId: legacy.project_id });
  }

  const claim = firstRow(await db.prepare(CLAIM_LEGACY_PROJECT_SQL).bind(keyHash, projectId, recoveryKeyHash).run());
  if (!claim) return textResponse("unauthorized", 401);
  await db.prepare(COPY_LEGACY_COUNTERS_SQL).bind(keyHash, projectId).run();

  await db.prepare(INSERT_MEMBER_SQL).bind(projectId, auth.userId, displayName, publicKey, "admin").run();
  return jsonResponse({ projectId });
}

async function nextId(db, request, url, body, projectId, track) {
  const auth = await authorizeProjectMember(db, request, url, body, projectId);
  if (!auth.ok) return textResponse("unauthorized", 401);

  const results = await db.batch([
    db.prepare(INSERT_COUNTER_SQL).bind(projectId, track),
    db.prepare(INCREMENT_COUNTER_SQL).bind(projectId, track),
  ]);
  const row = firstRow(results[1]);

  if (!row) return textResponse("unauthorized", 401);
  return jsonResponse({ id: row.id });
}

async function peekId(db, request, url, body, projectId, track) {
  const auth = await authorizeProjectMember(db, request, url, body, projectId);
  if (!auth.ok) return textResponse("unauthorized", 401);

  const results = await db.batch([
    db.prepare(INSERT_COUNTER_SQL).bind(projectId, track),
    db.prepare(PEEK_COUNTER_SQL).bind(projectId, track),
  ]);
  const row = firstRow(results[1]);

  if (!row) return textResponse("unauthorized", 401);
  return jsonResponse({ id: row.id });
}

async function authorizeProjectMember(db, request, url, body, projectId) {
  const project = firstRow(await db.prepare(FIND_PROJECT_SQL).bind(projectId).run());
  if (!project) return { ok: false };

  const signed = await validateSignedJsonRequest(db, request, url, body, projectId);
  if (!signed.ok) return signed;

  const member = firstRow(await db.prepare(FIND_MEMBER_SQL).bind(projectId, signed.userId).run());
  if (!member) return { ok: false };

  const verified = await verifySignature(request, url, body, signed, member.public_key);
  return verified ? { ok: true, userId: signed.userId, role: member.role } : { ok: false };
}

async function validateSignedJsonRequest(db, request, url, body, projectId) {
  const userId = request.headers.get("PM-User-Id");
  const timestamp = request.headers.get("PM-Timestamp");
  const nonce = request.headers.get("PM-Nonce");
  const signature = request.headers.get("PM-Signature");
  const publicKey = request.headers.get("PM-Public-Key");

  if (!userId || !timestamp || !nonce || !signature) return { ok: false };
  if (projectId === null && !publicKey) return { ok: false };

  const timestampNumber = Number.parseInt(timestamp, 10);
  if (!Number.isFinite(timestampNumber)) return { ok: false };

  const now = Math.floor(Date.now() / 1000);
  if (Math.abs(now - timestampNumber) > MAX_CLOCK_SKEW_SECONDS) return { ok: false };

  try {
    await db.prepare(INSERT_NONCE_SQL).bind(userId, nonce, timestampNumber).run();
  } catch (error) {
    if (isConstraintError(error)) return { ok: false };
    throw error;
  }

  const signed = { ok: true, userId, timestamp, nonce, signature, publicKey };
  if (projectId === null) {
    const verified = await verifySignature(request, url, body, signed, publicKey);
    return verified ? signed : { ok: false };
  }

  return signed;
}

async function verifySignature(request, url, body, signed, publicKeyBase64Url) {
  try {
    const publicKeyBytes = base64UrlDecode(publicKeyBase64Url);
    const signatureBytes = base64UrlDecode(signed.signature);
    const key = await crypto.subtle.importKey(
      "spki",
      publicKeyBytes,
      { name: "ECDSA", namedCurve: "P-256" },
      false,
      ["verify"],
    );

    const canonical = await canonicalRequest(request.method, url.pathname, signed.timestamp, signed.nonce, signed.userId, body);
    return await crypto.subtle.verify(
      { name: "ECDSA", hash: "SHA-256" },
      key,
      signatureBytes,
      new TextEncoder().encode(canonical),
    );
  } catch {
    return false;
  }
}

async function canonicalRequest(method, pathname, timestamp, nonce, userId, body) {
  const bodyHash = await sha256Hex(new TextEncoder().encode(body));
  return [AUTH_VERSION, method.toUpperCase(), pathname, timestamp, nonce, userId, bodyHash].join("\n");
}

function firstRow(result) {
  return result?.results?.[0] ?? null;
}

async function keyHashFromProjectKey(key) {
  try {
    return await sha512Hex(base64UrlDecode(key));
  } catch {
    return null;
  }
}

function base64UrlDecode(value) {
  const normalized = value.replaceAll("-", "+").replaceAll("_", "/");
  const padded = normalized.padEnd(normalized.length + ((4 - (normalized.length % 4)) % 4), "=");
  const binary = atob(padded);
  const bytes = new Uint8Array(binary.length);

  for (let i = 0; i < binary.length; i++) {
    bytes[i] = binary.charCodeAt(i);
  }

  return bytes;
}

async function sha256Hex(bytes) {
  const digest = await crypto.subtle.digest("SHA-256", bytes);
  return [...new Uint8Array(digest)].map((byte) => byte.toString(16).padStart(2, "0")).join("");
}

async function sha512Hex(bytes) {
  const digest = await crypto.subtle.digest("SHA-512", bytes);
  return [...new Uint8Array(digest)].map((byte) => byte.toString(16).padStart(2, "0")).join("");
}

function isConstraintError(error) {
  const message = String(error?.message ?? error).toLowerCase();
  return message.includes("constraint") || message.includes("unique");
}

function safeDecodeURIComponent(value) {
  try {
    return decodeURIComponent(value);
  } catch {
    return null;
  }
}

function parseJson(body) {
  try {
    return JSON.parse(body);
  } catch {
    return null;
  }
}

function requireString(value) {
  return typeof value === "string" && value.trim() !== "" ? value : null;
}

function logInternalError(error) {
  const name = typeof error?.name === "string" ? error.name : "Error";
  const message = typeof error?.message === "string" ? error.message : "unknown error";
  console.error("next-id-worker internal error", { name, message });
}

function jsonResponse(value, status = 200) {
  return new Response(JSON.stringify(value), {
    status,
    headers: { "content-type": "application/json; charset=utf-8" },
  });
}

function textResponse(value, status) {
  return new Response(value, { status });
}
