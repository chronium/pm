import assert from "node:assert/strict";
import { test } from "node:test";
import { handleRequest } from "../src/worker.js";

test("health returns ok", async () => {
  const response = await request(new FakeD1(), "GET", "/health");

  assert.equal(response.status, 200);
  assert.equal(await response.text(), "ok");
});

test("project creation returns a reusable secret key", async () => {
  const db = new FakeD1();
  const response = await request(db, "POST", "/projects");
  const body = await response.json();

  assert.equal(response.status, 200);
  assert.match(response.headers.get("content-type"), /^application\/json\b/);
  assert.match(body.key, /^[A-Za-z0-9_-]+$/);
  assert.equal(db.projects.size, 1);
});

test("nextid increments per track and peekid does not increment", async () => {
  const db = new FakeD1();
  const project = await request(db, "POST", "/projects").then((response) => response.json());

  assert.deepEqual(await json(db, "GET", `/projects/${project.key}/tracks/BUILD/nextid`), { id: 1 });
  assert.deepEqual(await json(db, "GET", `/projects/${project.key}/tracks/BUILD/peekid`), { id: 2 });
  assert.deepEqual(await json(db, "GET", `/projects/${project.key}/tracks/BUILD/nextid`), { id: 2 });
  assert.deepEqual(await json(db, "GET", `/projects/${project.key}/tracks/RENDER/nextid`), { id: 1 });
});

test("successful id responses are json", async () => {
  const db = new FakeD1();
  const project = await request(db, "POST", "/projects").then((response) => response.json());
  const response = await request(db, "GET", `/projects/${project.key}/tracks/BUILD/nextid`);

  assert.equal(response.status, 200);
  assert.match(response.headers.get("content-type"), /^application\/json\b/);
  assert.deepEqual(await response.json(), { id: 1 });
});

test("unknown project keys are unauthorized", async () => {
  const db = new FakeD1();
  const response = await request(db, "GET", "/projects/bm90LXJlZ2lzdGVyZWQ/tracks/BUILD/nextid");

  assert.equal(response.status, 401);
});

test("malformed project keys are unauthorized", async () => {
  const db = new FakeD1();
  const response = await request(db, "GET", "/projects/%/tracks/BUILD/nextid");

  assert.equal(response.status, 401);
});

test("unknown routes are not found", async () => {
  const response = await request(new FakeD1(), "GET", "/projects/abc");

  assert.equal(response.status, 404);
});

test("malformed track routes are not found", async () => {
  const response = await request(new FakeD1(), "GET", "/projects/abc/tracks/%/nextid");

  assert.equal(response.status, 404);
});

async function json(db, method, path) {
  const response = await request(db, method, path);
  assert.equal(response.status, 200);
  assert.match(response.headers.get("content-type"), /^application\/json\b/);
  return await response.json();
}

function request(db, method, path) {
  return handleRequest(new Request(`https://next-id.test${path}`, { method }), { DB: db });
}

class FakeD1 {
  projects = new Set();
  counters = new Map();

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
    if (this.#sql.includes("INSERT INTO projects")) {
      const [keyHash] = this.#bindings;
      if (this.#db.projects.has(keyHash)) throw new Error("UNIQUE constraint failed");
      this.#db.projects.add(keyHash);
      return { results: [] };
    }

    if (this.#sql.includes("INSERT INTO project_counters")) {
      const [keyHash, track] = this.#bindings;
      if (this.#db.projects.has(keyHash) && !this.#db.counters.has(counterKey(keyHash, track))) {
        this.#db.counters.set(counterKey(keyHash, track), 1);
      }
      return { results: [] };
    }

    if (this.#sql.includes("RETURNING next_id - 1 AS id")) {
      const [keyHash, track] = this.#bindings;
      const key = counterKey(keyHash, track);
      const nextId = this.#db.counters.get(key);
      if (nextId === undefined) return { results: [] };

      this.#db.counters.set(key, nextId + 1);
      return { results: [{ id: nextId }] };
    }

    if (this.#sql.includes("SELECT next_id AS id")) {
      const [keyHash, track] = this.#bindings;
      const nextId = this.#db.counters.get(counterKey(keyHash, track));
      return { results: nextId === undefined ? [] : [{ id: nextId }] };
    }

    throw new Error(`Unexpected SQL: ${this.#sql}`);
  }
}

function counterKey(keyHash, track) {
  return `${keyHash}:${track}`;
}
