import { TestBed } from '@angular/core/testing';
import { of } from 'rxjs';

import { StaticModeService } from './static-mode.service';
import type { StaticSnapshot } from './static-snapshot.interceptor';
import { StaticSnapshotStore } from './static-snapshot.interceptor';
import {
  parseCanonicalProjectReference,
  StaticProjectLinksService,
} from './static-project-links.service';

const snapshot = {
  schemaVersion: 3,
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

describe('StaticProjectLinksService', () => {
  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        { provide: StaticModeService, useValue: { enabled: true } },
        { provide: StaticSnapshotStore, useValue: { snapshot: of(snapshot) } },
      ],
    });
  });

  it('parses canonical task and nested wiki references', () => {
    expect(parseCanonicalProjectReference('pm://project/royale/task/GAME-001')).toEqual({
      projectId: 'royale',
      resource: 'task',
      value: 'GAME-001',
    });
    expect(parseCanonicalProjectReference('pm://project/royale/wiki/design/combat')).toEqual({
      projectId: 'royale',
      resource: 'wiki',
      value: 'design/combat',
    });
    expect(parseCanonicalProjectReference('pm://project/royale/task/one/two')).toBeNull();
  });

  it('uses local hash routes for the current project', () => {
    expect(
      TestBed.inject(StaticProjectLinksService).resolve('pm://project/games/wiki/shared/engine'),
    ).toEqual({ kind: 'available', href: '#/wiki/shared/engine', local: true });
  });

  it('preserves the published path and query while replacing its fragment', () => {
    expect(
      TestBed.inject(StaticProjectLinksService).resolve(
        'pm://project/royale/wiki/design/combat%20model',
      ),
    ).toEqual({
      kind: 'available',
      href: 'https://example.test/projects/royale/?view=public#/wiki/design/combat%20model',
      local: false,
    });
  });

  it('reports unavailable, unknown, and malformed project references', () => {
    const links = TestBed.inject(StaticProjectLinksService);
    expect(links.resolve('pm://project/starfall/task/GAME-001')).toEqual({
      kind: 'unavailable',
      reason: 'Starfall does not publish a static site URL.',
    });
    expect(links.resolve('pm://project/missing/task/GAME-001')).toEqual({
      kind: 'unavailable',
      reason: 'Project missing is not linked.',
    });
    expect(links.resolve('pm://broken')).toEqual({
      kind: 'unavailable',
      reason: 'This project reference is malformed.',
    });
    expect(links.resolve('pm://project/royale/wiki/../secret')).toEqual({
      kind: 'unavailable',
      reason: 'This project reference is malformed.',
    });
    expect(links.resolve('https://example.test')).toEqual({ kind: 'not-project-link' });
  });
});
