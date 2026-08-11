import assert from 'node:assert/strict';
import test from 'node:test';

import { channelsFor, parseTransition, renderPromotion, verifyPromotion } from './release.mjs';

const taskTransition = parseTransition(`schemaVersion: 1
at: 2026-08-11T09:02:38Z
kind: task
fromVersion: 1.0.2
toVersion: 1.0.3
source: PM-0121
`);
const template = 'runs:\n  image: docker://ghcr.io/chronium/pm@sha256:__PM_ACTION_IMAGE_DIGEST__\n';
const sourceRevision = 'a'.repeat(40);
const imageDigest = `sha256:${'b'.repeat(64)}`;

test('task and explicit major releases move only latest', () => {
  assert.deepEqual(channelsFor(taskTransition), ['latest']);
  assert.deepEqual(channelsFor(parseTransition(`schemaVersion: 1
at: 2026-08-11T09:02:38Z
kind: major
fromVersion: 1.4.7
toVersion: 2.0.0
source: null
reason: Compatibility boundary
`)), ['latest']);
});

test('milestone releases add the matching stable major channel', () => {
  assert.deepEqual(channelsFor(parseTransition(`schemaVersion: 1
at: 2026-08-11T09:02:38Z
kind: milestone
fromVersion: 1.0.9
toVersion: 1.1.0
source: public-beta
`)), ['latest', 'v1']);
});

test('milestone releases must reset patch', () => {
  assert.throws(() => channelsFor({ ...taskTransition, kind: 'milestone' }), /reset patch/);
});

test('promotion rendering is canonical and digest pinned', () => {
  const rendered = renderPromotion({
    template,
    version: '1.0.3',
    transition: taskTransition,
    sourceRevision,
    imageDigest,
  });
  assert.match(rendered.action, new RegExp(imageDigest));
  assert.doesNotMatch(rendered.action, /sha256:sha256:/);
  assert.doesNotMatch(rendered.action, /__PM_ACTION_IMAGE_DIGEST__/);
  assert.deepEqual(JSON.parse(rendered.current).channels, ['latest']);
});

test('promotion verification requires a direct signed-handoff shape', () => {
  const rendered = renderPromotion({
    template,
    version: '1.0.3',
    transition: taskTransition,
    sourceRevision,
    imageDigest,
  });
  assert.doesNotThrow(() => verifyPromotion({
    template,
    action: rendered.action,
    current: JSON.parse(rendered.current),
    version: '1.0.3',
    transition: taskTransition,
    parentRevision: sourceRevision,
    changedPaths: ['github-action/release/current.json', 'action.yml'],
  }));
  assert.throws(() => verifyPromotion({
    template,
    action: rendered.action,
    current: JSON.parse(rendered.current),
    version: '1.0.3',
    transition: taskTransition,
    parentRevision: 'c'.repeat(40),
    changedPaths: ['action.yml', 'github-action/release/current.json'],
  }), /direct child/);
  assert.throws(() => verifyPromotion({
    template,
    action: rendered.action,
    current: JSON.parse(rendered.current),
    version: '1.0.3',
    transition: taskTransition,
    parentRevision: sourceRevision,
    changedPaths: ['action.yml', 'README.md'],
  }), /may change only/);
});
