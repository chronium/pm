const INSERT_PROJECT_SQL = "INSERT INTO projects(key_hash) VALUES(?1);";
const INSERT_COUNTER_SQL = `
INSERT INTO project_counters(key_hash, track, next_id)
SELECT key_hash, ?2, 1
FROM projects
WHERE key_hash = ?1
ON CONFLICT(key_hash, track) DO NOTHING;
`;
const INCREMENT_COUNTER_SQL = `
UPDATE project_counters
SET next_id = next_id + 1
WHERE key_hash = ?1 AND track = ?2
RETURNING next_id - 1 AS id;
`;
const PEEK_COUNTER_SQL = `
SELECT next_id AS id
FROM project_counters
WHERE key_hash = ?1 AND track = ?2;
`;

export default {
  fetch(request, env) {
    return handleRequest(request, env);
  },
};

export async function handleRequest(request, env) {
  try {
    const url = new URL(request.url);
    const route = parseRoute(url.pathname);

    if (request.method === "GET" && route.kind === "health") {
      return new Response("ok");
    }

    if (request.method === "POST" && route.kind === "projects") {
      return await createProject(env.DB);
    }

    if (request.method === "GET" && route.kind === "nextid") {
      return await nextId(env.DB, route.key, route.track);
    }

    if (request.method === "GET" && route.kind === "peekid") {
      return await peekId(env.DB, route.key, route.track);
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

  const parts = pathname.split("/").filter(Boolean);
  if (
    parts.length === 5 &&
    parts[0] === "projects" &&
    parts[2] === "tracks" &&
    (parts[4] === "nextid" || parts[4] === "peekid")
  ) {
    const key = safeDecodeURIComponent(parts[1]);
    const track = safeDecodeURIComponent(parts[3]);
    if (track === null) return { kind: "unknown" };

    return { kind: parts[4], key, track };
  }

  return { kind: "unknown" };
}

async function createProject(db) {
  for (let attempt = 0; attempt < 2; attempt++) {
    const keyBytes = randomBytes(64);
    const key = base64UrlEncode(keyBytes);
    const keyHash = await sha512Hex(keyBytes);

    try {
      await db.prepare(INSERT_PROJECT_SQL).bind(keyHash).run();
      return jsonResponse({ key });
    } catch (error) {
      if (attempt === 0 && isConstraintError(error)) continue;
      throw error;
    }
  }

  throw new Error("project key generation collision");
}

async function nextId(db, key, track) {
  const keyHash = await keyHashFromProjectKey(key);
  if (keyHash === null) return textResponse("unauthorized", 401);

  const results = await db.batch([
    db.prepare(INSERT_COUNTER_SQL).bind(keyHash, track),
    db.prepare(INCREMENT_COUNTER_SQL).bind(keyHash, track),
  ]);
  const row = firstRow(results[1]);

  if (!row) return textResponse("unauthorized", 401);
  return jsonResponse({ id: row.id });
}

async function peekId(db, key, track) {
  const keyHash = await keyHashFromProjectKey(key);
  if (keyHash === null) return textResponse("unauthorized", 401);

  const results = await db.batch([
    db.prepare(INSERT_COUNTER_SQL).bind(keyHash, track),
    db.prepare(PEEK_COUNTER_SQL).bind(keyHash, track),
  ]);
  const row = firstRow(results[1]);

  if (!row) return textResponse("unauthorized", 401);
  return jsonResponse({ id: row.id });
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

function randomBytes(length) {
  const bytes = new Uint8Array(length);
  crypto.getRandomValues(bytes);
  return bytes;
}

function base64UrlEncode(bytes) {
  const binary = String.fromCharCode(...bytes);
  return btoa(binary).replaceAll("+", "-").replaceAll("/", "_").replaceAll("=", "");
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
