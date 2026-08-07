import { TestBed } from '@angular/core/testing';
import { of } from 'rxjs';

import { StaticModeService } from '../static/static-mode.service';
import type { StaticSnapshot } from '../static/static-snapshot.interceptor';
import { StaticSnapshotStore } from '../static/static-snapshot.interceptor';
import { ProjectContextService } from './project-context.service';
import {
  formatCanonicalProjectReference,
  parseCanonicalProjectReference,
  ProjectLinksService,
} from './project-links.service';

const snapshot = {
  schemaVersion: 5,
  generatedAt: '2026-07-31T00:00:00Z',
  projectId: 'games',
  linkedProjects: [
    {
      projectId: 'royale',
      name: 'Royale',
      alias: 'royale',
      relationship: 'child',
      publicSiteUrl: 'https://example.test/projects/royale/?view=public#old',
    },
    {
      projectId: 'starfall',
      name: 'Starfall',
      alias: 'starfall',
      relationship: 'child',
      publicSiteUrl: null,
    },
  ],
} as StaticSnapshot;

const liveFamily = {
  activeProjectId: 'games',
  members: [
    {
      projectId: 'games',
      name: 'Games',
      alias: null,
      relationship: 'current',
      status: 'available',
      source: 'current',
      readable: true,
      writeTrusted: true,
    },
    {
      projectId: 'royale',
      name: 'Royale',
      alias: 'royale',
      relationship: 'child',
      status: 'available',
      source: 'path-hint',
      readable: true,
      writeTrusted: false,
    },
    {
      projectId: 'missing',
      name: 'Missing',
      alias: 'missing',
      relationship: 'child',
      status: 'missing',
      source: 'unresolved',
      readable: false,
      writeTrusted: false,
    },
  ],
  warnings: [
    {
      code: 'linked_project_missing',
      message: 'Missing could not be resolved.',
      declaringProjectId: 'games',
      targetProjectId: 'missing',
      alias: 'missing',
      status: 'missing',
      repairCommand: 'git submodule update --init -- missing',
    },
  ],
};

describe('ProjectLinksService', () => {
  it('parses and formats canonical task and nested wiki references', () => {
    expect(parseCanonicalProjectReference('pm://project/royale/task/GAME-001')).toEqual({
      projectId: 'royale',
      resource: 'task',
      value: 'GAME-001',
    });
    const reference = {
      projectId: 'royale',
      resource: 'wiki' as const,
      value: 'design/combat model',
    };
    expect(formatCanonicalProjectReference(reference)).toBe(
      'pm://project/royale/wiki/design/combat%20model',
    );
    expect(parseCanonicalProjectReference(formatCanonicalProjectReference(reference))).toEqual(
      reference,
    );
    expect(parseCanonicalProjectReference('pm://project/royale/task/one/two')).toBeNull();
  });

  it('resolves current, linked, missing, and malformed references in live mode', async () => {
    const enableLinkedProjectFamily = configureLive();
    const links = TestBed.inject(ProjectLinksService);
    expect(links.resolve('pm://project/games/wiki/shared/engine')).toEqual({
      kind: 'available',
      href: '/wiki/shared/engine',
      local: true,
    });
    expect(links.resolve('pm://project/royale/task/GAME-001')).toEqual({
      kind: 'available',
      href: '/projects/royale/tasks/GAME-001',
      local: true,
    });
    expect(links.resolve('pm://project/missing/wiki/guide')).toEqual({
      kind: 'unavailable',
      reason: 'Missing could not be resolved. Repair with: git submodule update --init -- missing',
    });
    expect(links.resolve('pm://broken')).toEqual({
      kind: 'unavailable',
      reason: 'This project reference is malformed.',
    });
    await Promise.resolve();
    expect(enableLinkedProjectFamily).toHaveBeenCalledOnce();
  });

  it('preserves static local and separately published routes', () => {
    TestBed.configureTestingModule({
      providers: [
        { provide: StaticModeService, useValue: { enabled: true } },
        { provide: StaticSnapshotStore, useValue: { snapshot: of(snapshot) } },
        { provide: ProjectContextService, useValue: {} },
      ],
    });
    const links = TestBed.inject(ProjectLinksService);
    expect(links.resolve('pm://project/games/wiki/shared/engine')).toEqual({
      kind: 'available',
      href: '#/wiki/shared/engine',
      local: true,
    });
    expect(links.resolve('pm://project/royale/wiki/design/combat%20model')).toEqual({
      kind: 'available',
      href: 'https://example.test/projects/royale/?view=public#/wiki/design/combat%20model',
      local: false,
    });
    expect(links.resolve('pm://project/starfall/task/GAME-001')).toEqual({
      kind: 'unavailable',
      reason: 'Starfall does not publish a static site URL.',
    });
  });
});

function configureLive() {
  const enableLinkedProjectFamily = vi.fn();
  TestBed.configureTestingModule({
    providers: [
      { provide: StaticModeService, useValue: { enabled: false } },
      { provide: StaticSnapshotStore, useValue: { snapshot: of(null) } },
      {
        provide: ProjectContextService,
        useValue: {
          enableLinkedProjectFamily,
          family: {
            hasValue: () => true,
            value: () => liveFamily,
            error: () => null,
          },
        },
      },
    ],
  });
  return enableLinkedProjectFamily;
}
