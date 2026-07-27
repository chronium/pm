import { HttpParams, HttpRequest } from '@angular/common/http';

import type { StaticSnapshot } from './static-snapshot.interceptor';
import { adaptGet, validateSnapshot } from './static-snapshot.interceptor';
import { isStaticDocument } from './static-mode.service';

const snapshot: StaticSnapshot = {
  schemaVersion: 1,
  generatedAt: '2026-07-27T12:30:00Z',
  project: { name: 'Static PM', revision: 'static-snapshot' },
  settings: {
    projectName: 'Static PM',
    statuses: [
      { key: 'todo', name: 'To do' },
      { key: 'done', name: 'Done' },
    ],
    tracks: [
      { key: 'PM', name: 'PM' },
      { key: 'WEB', name: 'Web' },
    ],
    milestones: [{ key: 'launch', title: 'Launch', priority: 'high' }],
    priorityOptions: ['none', 'low', 'medium', 'high', 'urgent'],
    revision: 'static-snapshot',
  },
  navigation: {
    remainingCount: 2,
    tracks: [
      { key: 'PM', name: 'PM', remainingCount: 1 },
      { key: 'WEB', name: 'Web', remainingCount: 1 },
    ],
    milestones: [{ key: 'launch', name: 'Launch', remainingCount: 2 }],
    revision: 'static-snapshot',
  },
  board: {
    projectName: 'Static PM',
    filters: { track: null, milestone: null, state: null },
    tracks: [
      { key: 'PM', name: 'PM', priority: 'none' },
      { key: 'WEB', name: 'Web', priority: 'none' },
    ],
    milestones: [{ key: 'launch', name: 'Launch', priority: 'high' }],
    states: [
      { key: 'todo', name: 'To do', priority: 'none' },
      { key: 'done', name: 'Done', priority: 'none' },
    ],
    milestoneGroups: [
      {
        key: 'launch',
        name: 'Launch',
        states: [
          {
            key: 'todo',
            name: 'To do',
            tasks: [taskSummary('PM-0001', 'PM'), taskSummary('WEB-0001', 'WEB')],
          },
          { key: 'done', name: 'Done', tasks: [taskSummary('WEB-0002', 'WEB', 'done')] },
        ],
      },
    ],
    revision: 'static-snapshot',
  },
  tasks: [
    {
      id: 'PM-0001',
      title: 'Foundation',
      track: 'PM',
      milestone: 'launch',
      priority: 'high',
      prioritySource: 'milestone',
      prioritySelection: 'inherit',
      state: 'todo',
      dependencies: { ready: true, dependsOn: [], waitingOn: [], missing: [], summary: 'ready' },
      createdAt: '2026-01-01T00:00:00Z',
      modifiedAt: '2026-01-02T00:00:00Z',
      description: 'Body',
      revision: 'static-snapshot',
    },
  ],
  wikiIndex: [{ path: 'guide/start', title: 'Start', modifiedAt: '2026-01-02T00:00:00Z' }],
  wikiPages: [
    {
      path: 'guide/start',
      title: 'Start',
      createdAt: '2026-01-01T00:00:00Z',
      modifiedAt: '2026-01-02T00:00:00Z',
      body: '# Start',
      revision: 'static-snapshot',
    },
  ],
};

describe('static snapshot mode', () => {
  it('detects generated static metadata', () => {
    const document = new DOMParser().parseFromString(
      '<meta name="pm-site-mode" content="static">',
      'text/html',
    );

    expect(isStaticDocument(document)).toBe(true);
    expect(isStaticDocument(new DOMParser().parseFromString('', 'text/html'))).toBe(false);
  });

  it('validates the supported schema and reports malformed or future snapshots', () => {
    expect(validateSnapshot(snapshot)).toBe(snapshot);
    expect(() => validateSnapshot({ ...snapshot, tasks: undefined })).toThrow(/collections/);
    expect(() => validateSnapshot({ ...snapshot, schemaVersion: 2 })).toThrow(
      /Unsupported static snapshot schema version: 2/,
    );
  });

  it('adapts project, task, wiki, and display settings GET contracts', () => {
    expect(adaptGet(snapshot, get('/api/v1/project'))).toEqual(snapshot.project);
    expect(adaptGet(snapshot, get('/api/v1/settings'))).toEqual(snapshot.settings);
    expect(adaptGet(snapshot, get('/api/v1/wiki/pages'))).toEqual(snapshot.wikiIndex);
    expect(adaptGet(snapshot, get('/api/v1/tasks/PM-0001'))).toMatchObject({
      id: 'PM-0001',
      description: 'Body',
      localMetadata: { filePath: '' },
    });
    expect(adaptGet(snapshot, get('/api/v1/wiki/pages/guide/start'))).toMatchObject({
      path: 'guide/start',
      body: '# Start',
      localMetadata: { filePath: '' },
    });
  });

  it('filters the complete board locally without changing snapshot ordering', () => {
    const request = get(
      '/api/v1/board',
      new HttpParams().set('track', 'WEB').set('milestone', 'launch').set('state', 'todo'),
    );

    const board = adaptGet(snapshot, request) as StaticSnapshot['board'];

    expect(board.filters).toEqual({ track: 'WEB', milestone: 'launch', state: 'todo' });
    expect(board.milestoneGroups).toHaveLength(1);
    expect(board.milestoneGroups[0]!.states).toHaveLength(1);
    expect(board.milestoneGroups[0]!.states[0]!.tasks.map((task) => task.id)).toEqual(['WEB-0001']);
    expect(snapshot.board.milestoneGroups[0]!.states[0]!.tasks.map((task) => task.id)).toEqual([
      'PM-0001',
      'WEB-0001',
    ]);
  });
});

function get(url: string, params = new HttpParams()): HttpRequest<unknown> {
  return new HttpRequest('GET', url, { params });
}

function taskSummary(id: string, track: string, state = 'todo') {
  return {
    id,
    title: id,
    track,
    milestone: 'launch',
    priority: 'high',
    prioritySource: 'milestone',
    state,
    dependencies: { ready: true, dependsOn: [], waitingOn: [], missing: [], summary: 'ready' },
    descriptionPreview: id,
    modifiedAt: '2026-01-02T00:00:00Z',
  };
}
