import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { join } from 'node:path';
import test from 'node:test';
import {
  computeProfileRevision,
  computeSpecificationHash,
} from '../src/protocol/canonical-json.js';
import { ProtocolValidationError, parseRunRequest } from '../src/protocol/validation.js';
import { createRequest } from './helpers.js';

const fixturePath = join(process.cwd(), '..', 'contracts/agent-runs/v1/run-request.json');

test('protocol 1.0 fixture matches the .NET canonical hashes', () => {
  const fixture = JSON.parse(readFileSync(fixturePath, 'utf8')) as unknown;
  const request = parseRunRequest(fixture);

  assert.equal(computeSpecificationHash(request.specification), request.specificationHash);
  assert.equal(
    computeProfileRevision(request.specification.runtime.profile),
    request.specification.runtime.profile.revision,
  );
  assert.equal(request.specification.project.name, 'PM π');
});

test('protocol parsing rejects a changed immutable specification', () => {
  const fixture = JSON.parse(readFileSync(fixturePath, 'utf8')) as Record<string, unknown>;
  const specification = fixture['specification'] as Record<string, unknown>;
  const task = specification['task'] as Record<string, unknown>;
  task['title'] = 'Changed without updating the hash';

  assert.throws(
    () => parseRunRequest(fixture),
    (error: unknown) =>
      error instanceof ProtocolValidationError && error.code === 'specification_hash_mismatch',
  );
});

test('protocol parsing rejects task IDs that could escape the task directory', () => {
  const request = createRequest('run-task-path');
  request.specification.task.taskId = '../outside';
  request.specificationHash = computeSpecificationHash(request.specification);
  assert.throws(() => parseRunRequest(request), /path-safe/);
});
