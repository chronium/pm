const INSERT_PROJECT_SQL = `
INSERT INTO projects(key_hash, project_id, recovery_key_hash)
VALUES(?1, ?2, ?3);
`;
const FIND_PROJECT_SQL = `
SELECT project_id
FROM projects
WHERE project_id = ?1;
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
const LIST_MEMBERS_SQL = `
SELECT user_id, display_name, public_key, role
FROM project_members
WHERE project_id = ?1
ORDER BY display_name COLLATE NOCASE, user_id;
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
const INSERT_INVITATION_SQL = `
INSERT INTO project_invitations(
    invitation_id, project_id, token_hash, role, created_by_user_id, created_at, expires_at)
VALUES(?1, ?2, ?3, ?4, ?5, ?6, ?7);
`;
const LIST_INVITATIONS_SQL = `
SELECT invitation_id, role, created_by_user_id, created_at, expires_at
FROM project_invitations
WHERE project_id = ?1
  AND consumed_at IS NULL
  AND revoked_at IS NULL
  AND expires_at > ?2
ORDER BY created_at DESC, invitation_id;
`;
const REVOKE_INVITATION_SQL = `
UPDATE project_invitations
SET revoked_at = ?3
WHERE project_id = ?1 AND invitation_id = ?2
  AND consumed_at IS NULL AND revoked_at IS NULL AND expires_at > ?3
RETURNING invitation_id;
`;
const CONSUME_INVITATION_SQL = `
UPDATE project_invitations
SET consumed_at = COALESCE(consumed_at, ?4),
    consumed_by_user_id = COALESCE(consumed_by_user_id, ?3)
WHERE project_id = ?1 AND token_hash = ?2 AND revoked_at IS NULL
  AND ((consumed_at IS NULL AND expires_at > ?4) OR consumed_by_user_id = ?3)
  AND NOT EXISTS (
      SELECT 1 FROM project_members
      WHERE project_id = ?1 AND user_id = ?3 AND public_key <> ?5
  )
RETURNING role;
`;
const INSERT_INVITED_MEMBER_SQL = `
INSERT INTO project_members(project_id, user_id, display_name, public_key, role)
SELECT project_id, ?3, ?4, ?5, role
FROM project_invitations
WHERE project_id = ?1 AND token_hash = ?2
  AND consumed_by_user_id = ?3 AND revoked_at IS NULL
ON CONFLICT(project_id, user_id) DO NOTHING;
`;
const UPDATE_MEMBER_ROLE_SQL = `
UPDATE project_members
SET role = ?3
WHERE project_id = ?1 AND user_id = ?2
  AND (
      role <> 'admin' OR ?3 = 'admin' OR EXISTS (
          SELECT 1 FROM project_members other
          WHERE other.project_id = ?1 AND other.role = 'admin' AND other.user_id <> ?2
      )
  )
RETURNING user_id, display_name, public_key, role;
`;
const DELETE_MEMBER_SQL = `
DELETE FROM project_members
WHERE project_id = ?1 AND user_id = ?2
  AND (
      role <> 'admin' OR EXISTS (
          SELECT 1 FROM project_members other
          WHERE other.project_id = ?1 AND other.role = 'admin' AND other.user_id <> ?2
      )
  )
RETURNING user_id;
`;

const AUTH_VERSION = "pm-auth-v1";
const MAX_CLOCK_SKEW_SECONDS = 300;
const INVITATION_LIFETIME_SECONDS = 24 * 60 * 60;

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

    if (request.method === "GET" && route.kind === "health") return new Response("ok");
    if (request.method === "POST" && route.kind === "projects")
      return await createProject(env.DB, request, url, body);
    if (request.method === "GET" && route.kind === "nextid")
      return await nextId(env.DB, request, url, body, route.projectId, route.track);
    if (request.method === "GET" && route.kind === "peekid")
      return await peekId(env.DB, request, url, body, route.projectId, route.track);
    if (request.method === "GET" && route.kind === "members")
      return await listMembers(env.DB, request, url, body, route.projectId);
    if (request.method === "GET" && route.kind === "invitations")
      return await listInvitations(env.DB, request, url, body, route.projectId);
    if (request.method === "POST" && route.kind === "invitations")
      return await createInvitation(env.DB, request, url, body, route.projectId);
    if (request.method === "DELETE" && route.kind === "invitation")
      return await revokeInvitation(env.DB, request, url, body, route.projectId, route.invitationId);
    if (request.method === "POST" && route.kind === "acceptInvitation")
      return await acceptInvitation(env, request, url, body, route.projectId);
    if (request.method === "PATCH" && route.kind === "member")
      return await updateMemberRole(env.DB, request, url, body, route.projectId, route.userId);
    if (request.method === "DELETE" && route.kind === "member")
      return await removeMember(env.DB, request, url, body, route.projectId, route.userId);

    return textResponse("not found", 404);
  } catch (error) {
    logInternalError(error);
    return jsonError("internal_error", "The request could not be completed.", 500);
  }
}

function parseRoute(pathname) {
  if (pathname === "/health") return { kind: "health" };
  if (pathname === "/projects") return { kind: "projects" };

  const parts = pathname.split("/").filter(Boolean);
  if (parts[0] !== "projects") return { kind: "unknown" };
  const projectId = safeDecodeURIComponent(parts[1]);
  if (projectId === null) return { kind: "unknown" };

  if (parts.length === 5 && parts[2] === "tracks" && (parts[4] === "nextid" || parts[4] === "peekid")) {
    const track = safeDecodeURIComponent(parts[3]);
    return track === null ? { kind: "unknown" } : { kind: parts[4], projectId, track };
  }
  if (parts.length === 3 && parts[2] === "members") return { kind: "members", projectId };
  if (parts.length === 3 && parts[2] === "invitations") return { kind: "invitations", projectId };
  if (parts.length === 4 && parts[2] === "invitations" && parts[3] === "accept")
    return { kind: "acceptInvitation", projectId };
  if (parts.length === 4 && parts[2] === "invitations") {
    const invitationId = safeDecodeURIComponent(parts[3]);
    return invitationId === null ? { kind: "unknown" } : { kind: "invitation", projectId, invitationId };
  }
  if (parts.length === 4 && parts[2] === "members") {
    const userId = safeDecodeURIComponent(parts[3]);
    return userId === null ? { kind: "unknown" } : { kind: "member", projectId, userId };
  }
  return { kind: "unknown" };
}

async function createProject(db, request, url, body) {
  const auth = await validateSignedJsonRequest(db, request, url, body, null);
  if (!auth.ok) return textResponse("unauthorized", 401);
  const payload = parseJson(body);
  const projectId = requireString(payload?.projectId);
  const displayName = requireString(payload?.displayName);
  const publicKey = requireString(payload?.publicKey);
  const recoveryKeyHash = requireString(payload?.recoveryKeyHash);
  if (!projectId || !displayName || !publicKey || !recoveryKeyHash) return textResponse("bad request", 400);
  if (auth.userId !== payload.userId || auth.publicKey !== publicKey) return textResponse("unauthorized", 401);

  await db.prepare(INSERT_PROJECT_SQL).bind(await sha512Hex(new TextEncoder().encode(`auth:${projectId}`)), projectId, recoveryKeyHash).run();
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
  return row ? jsonResponse({ id: row.id }) : textResponse("unauthorized", 401);
}

async function peekId(db, request, url, body, projectId, track) {
  const auth = await authorizeProjectMember(db, request, url, body, projectId);
  if (!auth.ok) return textResponse("unauthorized", 401);
  const results = await db.batch([
    db.prepare(INSERT_COUNTER_SQL).bind(projectId, track),
    db.prepare(PEEK_COUNTER_SQL).bind(projectId, track),
  ]);
  const row = firstRow(results[1]);
  return row ? jsonResponse({ id: row.id }) : textResponse("unauthorized", 401);
}

async function listMembers(db, request, url, body, projectId) {
  const auth = await requireMember(db, request, url, body, projectId);
  if (auth.response) return auth.response;
  const result = await db.prepare(LIST_MEMBERS_SQL).bind(projectId).run();
  return jsonResponse({
    currentUserId: auth.userId,
    currentRole: auth.role,
    members: (result.results ?? []).map(memberJson),
  });
}

async function listInvitations(db, request, url, body, projectId) {
  const auth = await requireAdmin(db, request, url, body, projectId);
  if (auth.response) return auth.response;
  const now = unixNow();
  const result = await db.prepare(LIST_INVITATIONS_SQL).bind(projectId, now).run();
  return jsonResponse({ invitations: (result.results ?? []).map(invitationJson) });
}

async function createInvitation(db, request, url, body, projectId) {
  const auth = await requireAdmin(db, request, url, body, projectId);
  if (auth.response) return auth.response;
  const payload = parseJson(body);
  const role = payload?.role ?? "user";
  if (!validRole(role)) return jsonError("invalid_role", "Role must be admin or user.", 400);

  const invitationId = randomSecret("pminv", 18);
  const token = randomSecret("pmi", 32);
  const now = unixNow();
  const expiresAt = now + INVITATION_LIFETIME_SECONDS;
  await db.prepare(INSERT_INVITATION_SQL)
    .bind(invitationId, projectId, await sha256Hex(new TextEncoder().encode(token)), role, auth.userId, now, expiresAt)
    .run();
  return jsonResponse({ invitation: invitationJson({
    invitation_id: invitationId,
    role,
    created_by_user_id: auth.userId,
    created_at: now,
    expires_at: expiresAt,
  }), token }, 201);
}

async function revokeInvitation(db, request, url, body, projectId, invitationId) {
  const auth = await requireAdmin(db, request, url, body, projectId);
  if (auth.response) return auth.response;
  const row = firstRow(await db.prepare(REVOKE_INVITATION_SQL).bind(projectId, invitationId, unixNow()).run());
  return row ? new Response(null, { status: 204 }) : jsonError("invitation_not_found", "The invitation is no longer active.", 404);
}

async function acceptInvitation(env, request, url, body, projectId) {
  const source = request.headers.get("CF-Connecting-IP") ?? "unknown";
  if (env.INVITATION_ACCEPT_RATE_LIMITER) {
    const limit = await env.INVITATION_ACCEPT_RATE_LIMITER.limit({ key: `${projectId}:${source}` });
    if (!limit.success) return jsonError("rate_limited", "Too many invitation attempts. Try again later.", 429);
  }

  const auth = await validateSignedJsonRequest(env.DB, request, url, body, null);
  const payload = parseJson(body);
  const token = requireString(payload?.token);
  const userId = requireString(payload?.userId);
  const displayName = requireString(payload?.displayName);
  const publicKey = requireString(payload?.publicKey);
  if (!auth.ok || !token || !token.startsWith("pmi_") || !userId || !displayName || !publicKey ||
      auth.userId !== userId || auth.publicKey !== publicKey) return invalidInvitation();

  const tokenHash = await sha256Hex(new TextEncoder().encode(token));
  const now = unixNow();
  const results = await env.DB.batch([
    env.DB.prepare(CONSUME_INVITATION_SQL).bind(projectId, tokenHash, userId, now, publicKey),
    env.DB.prepare(INSERT_INVITED_MEMBER_SQL).bind(projectId, tokenHash, userId, displayName, publicKey),
    env.DB.prepare(FIND_MEMBER_SQL).bind(projectId, userId),
  ]);
  const consumed = firstRow(results[0]);
  const member = firstRow(results[2]);
  if (!consumed || !member || member.public_key !== publicKey) return invalidInvitation();
  return jsonResponse({ member: memberJson({
    user_id: userId,
    display_name: displayName,
    public_key: publicKey,
    role: member.role,
  }) });
}

async function updateMemberRole(db, request, url, body, projectId, userId) {
  const auth = await requireAdmin(db, request, url, body, projectId);
  if (auth.response) return auth.response;
  const role = parseJson(body)?.role;
  if (!validRole(role)) return jsonError("invalid_role", "Role must be admin or user.", 400);
  const row = firstRow(await db.prepare(UPDATE_MEMBER_ROLE_SQL).bind(projectId, userId, role).run());
  if (row) return jsonResponse({ member: memberJson(row) });
  const target = firstRow(await db.prepare(FIND_MEMBER_SQL).bind(projectId, userId).run());
  return target
    ? jsonError("final_admin", "The final project admin cannot be demoted.", 409)
    : jsonError("member_not_found", "The project member was not found.", 404);
}

async function removeMember(db, request, url, body, projectId, userId) {
  const auth = await requireAdmin(db, request, url, body, projectId);
  if (auth.response) return auth.response;
  const row = firstRow(await db.prepare(DELETE_MEMBER_SQL).bind(projectId, userId).run());
  if (row) return new Response(null, { status: 204 });
  const target = firstRow(await db.prepare(FIND_MEMBER_SQL).bind(projectId, userId).run());
  return target
    ? jsonError("final_admin", "The final project admin cannot be removed.", 409)
    : jsonError("member_not_found", "The project member was not found.", 404);
}

async function requireMember(db, request, url, body, projectId) {
  const auth = await authorizeProjectMember(db, request, url, body, projectId);
  return auth.ok ? auth : { response: jsonError("unauthorized", "Authentication failed.", 401) };
}

async function requireAdmin(db, request, url, body, projectId) {
  const auth = await requireMember(db, request, url, body, projectId);
  if (auth.response) return auth;
  return auth.role === "admin" ? auth : { response: jsonError("admin_required", "Project admin access is required.", 403) };
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
  if (!userId || !timestamp || !nonce || !signature || (projectId === null && !publicKey)) return { ok: false };
  const timestampNumber = Number.parseInt(timestamp, 10);
  if (!Number.isFinite(timestampNumber) || Math.abs(unixNow() - timestampNumber) > MAX_CLOCK_SKEW_SECONDS) return { ok: false };
  try {
    await db.prepare(INSERT_NONCE_SQL).bind(userId, nonce, timestampNumber).run();
  } catch (error) {
    if (isConstraintError(error)) return { ok: false };
    throw error;
  }
  const signed = { ok: true, userId, timestamp, nonce, signature, publicKey };
  if (projectId !== null) return signed;
  return await verifySignature(request, url, body, signed, publicKey) ? signed : { ok: false };
}

async function verifySignature(request, url, body, signed, publicKeyBase64Url) {
  try {
    const key = await crypto.subtle.importKey("spki", base64UrlDecode(publicKeyBase64Url),
      { name: "ECDSA", namedCurve: "P-256" }, false, ["verify"]);
    const canonical = await canonicalRequest(request.method, url.pathname, signed.timestamp, signed.nonce, signed.userId, body);
    return await crypto.subtle.verify({ name: "ECDSA", hash: "SHA-256" }, key,
      base64UrlDecode(signed.signature), new TextEncoder().encode(canonical));
  } catch {
    return false;
  }
}

async function canonicalRequest(method, pathname, timestamp, nonce, userId, body) {
  const bodyHash = await sha256Hex(new TextEncoder().encode(body));
  return [AUTH_VERSION, method.toUpperCase(), pathname, timestamp, nonce, userId, bodyHash].join("\n");
}

function memberJson(row) {
  return { userId: row.user_id, displayName: row.display_name, publicKey: row.public_key, role: row.role };
}

function invitationJson(row) {
  return {
    invitationId: row.invitation_id,
    role: row.role,
    createdByUserId: row.created_by_user_id,
    createdAt: new Date(row.created_at * 1000).toISOString(),
    expiresAt: new Date(row.expires_at * 1000).toISOString(),
  };
}

function invalidInvitation() {
  return jsonError("invalid_invitation", "The invitation is invalid or no longer active.", 400);
}

function validRole(value) {
  return value === "admin" || value === "user";
}

function unixNow() {
  return Math.floor(Date.now() / 1000);
}

function randomSecret(prefix, byteCount) {
  const bytes = crypto.getRandomValues(new Uint8Array(byteCount));
  return `${prefix}_${base64UrlEncode(bytes)}`;
}

function firstRow(result) {
  return result?.results?.[0] ?? null;
}

function base64UrlEncode(bytes) {
  let binary = "";
  for (const byte of bytes) binary += String.fromCharCode(byte);
  return btoa(binary).replaceAll("+", "-").replaceAll("/", "_").replace(/=+$/, "");
}

function base64UrlDecode(value) {
  const normalized = value.replaceAll("-", "+").replaceAll("_", "/");
  const binary = atob(normalized.padEnd(normalized.length + ((4 - (normalized.length % 4)) % 4), "="));
  return Uint8Array.from(binary, character => character.charCodeAt(0));
}

async function sha256Hex(bytes) {
  const digest = await crypto.subtle.digest("SHA-256", bytes);
  return [...new Uint8Array(digest)].map(byte => byte.toString(16).padStart(2, "0")).join("");
}

async function sha512Hex(bytes) {
  const digest = await crypto.subtle.digest("SHA-512", bytes);
  return [...new Uint8Array(digest)].map(byte => byte.toString(16).padStart(2, "0")).join("");
}

function isConstraintError(error) {
  const message = String(error?.message ?? error).toLowerCase();
  return message.includes("constraint") || message.includes("unique");
}

function safeDecodeURIComponent(value) {
  if (typeof value !== "string") return null;
  try { return decodeURIComponent(value); } catch { return null; }
}

function parseJson(body) {
  try { return JSON.parse(body); } catch { return null; }
}

function requireString(value) {
  return typeof value === "string" && value.trim() !== "" ? value : null;
}

function logInternalError(error) {
  console.error("next-id-worker internal error", { name: typeof error?.name === "string" ? error.name : "Error" });
}

function jsonResponse(value, status = 200) {
  return new Response(JSON.stringify(value), { status, headers: { "content-type": "application/json; charset=utf-8" } });
}

function jsonError(errorCode, message, status) {
  return jsonResponse({ errorCode, message }, status);
}

function textResponse(value, status) {
  return new Response(value, { status });
}
