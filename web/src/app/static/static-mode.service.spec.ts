import { HttpParams, HttpRequest } from '@angular/common/http';

import type { StaticSnapshot } from './static-snapshot.interceptor';
import { adaptGet, validateSnapshot } from './static-snapshot.interceptor';
import { isStaticDocument } from './static-mode.service';

const snapshot: StaticSnapshot = {
  schemaVersion: 5,
  generatedAt: '2026-07-27T12:30:00Z',
  projectId: 'static-pm',
  linkedProjects: [],
  project: {
    projectId: 'static-project',
    name: 'Static PM',
    accent: 'teal',
    relationship: 'current',
    readOnly: true,
    revision: 'static-snapshot',
  },
  settings: {
    projectName: 'Static PM',
    accent: 'teal',
    statuses: [
      { key: 'todo', name: 'To do' },
      { key: 'done', name: 'Done' },
    ],
    tracks: [
      { key: 'PM', name: 'PM' },
      { key: 'WEB', name: 'Web' },
    ],
    milestones: [
      {
        key: 'launch',
        title: 'Launch',
        priority: 'high',
        description: 'Deliver the launch release.',
        requiredActivationTriggers: [],
      },
    ],
    activationTriggers: [],
    priorityOptions: ['none', 'low', 'medium', 'high', 'urgent'],
    revision: 'static-snapshot',
  },
  activation: {
    activationTriggers: [],
    milestones: [
      {
        key: 'launch',
        title: 'Launch',
        description: 'Deliver the launch release.',
        priority: 'high',
        lifecycle: 'active',
        assignedTaskCount: 3,
        doneTaskCount: 1,
        requiredActivationTriggers: [],
        unmetActivationTriggers: [],
        delivery: null,
      },
    ],
    issues: [],
    revision: 'static-snapshot',
  },
  navigation: {
    remainingCount: 2,
    activationEligibleCount: 2,
    tracks: [
      { key: 'PM', name: 'PM', remainingCount: 1, activationEligibleCount: 1 },
      { key: 'WEB', name: 'Web', remainingCount: 1, activationEligibleCount: 1 },
    ],
    milestones: [
      {
        key: 'launch',
        name: 'Launch',
        remainingCount: 2,
        activationEligibleCount: 2,
        lifecycle: 'active',
        unmetActivationTriggers: [],
      },
    ],
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
        description: 'Deliver the launch release.',
        lifecycle: 'active',
        requiredActivationTriggers: [],
        unmetActivationTriggers: [],
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
      dependencies: {
        ready: false,
        dependsOn: ['WEB-0001'],
        waitingOn: ['WEB-0001'],
        missing: [],
        summary: 'waiting on WEB-0001',
      },
      activation: eligibleActivation(),
      createdAt: '2026-01-01T00:00:00Z',
      modifiedAt: '2026-01-02T00:00:00Z',
      description: 'Body contains release needle twice: needle.',
      revision: 'static-snapshot',
    },
    {
      id: 'PM-0002',
      title: 'Follow-up',
      track: 'PM',
      milestone: 'launch',
      priority: 'high',
      prioritySource: 'milestone',
      prioritySelection: 'inherit',
      state: 'done',
      dependencies: { ready: true, dependsOn: [], waitingOn: [], missing: [], summary: 'ready' },
      activation: eligibleActivation(),
      createdAt: '2026-01-01T00:00:00Z',
      modifiedAt: '2026-01-03T00:00:00Z',
      description: 'Needle once.',
      revision: 'static-snapshot',
    },
    {
      id: 'WEB-0001',
      title: 'Web delivery',
      track: 'WEB',
      milestone: 'launch',
      priority: 'high',
      prioritySource: 'milestone',
      prioritySelection: 'inherit',
      state: 'todo',
      dependencies: { ready: true, dependsOn: [], waitingOn: [], missing: [], summary: 'ready' },
      activation: eligibleActivation(),
      createdAt: '2026-01-01T00:00:00Z',
      modifiedAt: '2026-01-04T00:00:00Z',
      description: 'Needle in another track.',
      revision: 'static-snapshot',
    },
  ],
  wikiIndex: [
    { path: 'guide/start', title: 'Start', modifiedAt: '2026-01-02T00:00:00Z' },
    { path: 'guide/next', title: 'Next', modifiedAt: '2026-01-03T00:00:00Z' },
  ],
  wikiPages: [
    {
      path: 'guide/start',
      title: 'Start',
      createdAt: '2026-01-01T00:00:00Z',
      modifiedAt: '2026-01-02T00:00:00Z',
      body: '# Start',
      revision: 'static-snapshot',
    },
    {
      path: 'guide/next',
      title: 'Next needle',
      createdAt: '2026-01-01T00:00:00Z',
      modifiedAt: '2026-01-03T00:00:00Z',
      body: 'Needle needle in the body.',
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
    expect(() => validateSnapshot({ ...snapshot, schemaVersion: 3 })).toThrow(
      /Unsupported static snapshot schema version: 3/,
    );
    expect(() =>
      validateSnapshot({
        ...snapshot,
        linkedProjects: [
          {
            projectId: 'unsafe',
            name: 'Unsafe',
            alias: null,
            relationship: 'child',
            publicSiteUrl: 'javascript:alert(1)',
          },
        ],
      }),
    ).toThrow(/invalid linkedProjects/);
  });

  it('adapts project, task, wiki, and display settings GET contracts', () => {
    expect(adaptGet(snapshot, get('/api/v1/project'))).toEqual(snapshot.project);
    expect(adaptGet(snapshot, get('/api/v1/settings'))).toEqual(snapshot.settings);
    expect(adaptGet(snapshot, get('/api/v1/activation'))).toEqual(snapshot.activation);
    expect(adaptGet(snapshot, get('/api/v1/wiki/pages'))).toEqual(snapshot.wikiIndex);
    expect(adaptGet(snapshot, get('/api/v1/tasks/PM-0001'))).toMatchObject({
      id: 'PM-0001',
      description: 'Body contains release needle twice: needle.',
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

  it('searches snapshot tasks with backend-compatible ranking, predicates, and selection scope', () => {
    const scoped = adaptGet(
      snapshot,
      get(
        '/api/v1/tasks/search',
        new HttpParams().set('query', 'needle').set('track', 'PM').set('limit', '20'),
      ),
    ) as { id: string; matchCount: number | string; snippet: string }[];
    expect(scoped.map((result) => result.id)).toEqual(['PM-0001', 'PM-0002']);
    expect(Number(scoped[0]!.matchCount)).toBeGreaterThan(Number(scoped[1]!.matchCount));
    expect(scoped[0]!.snippet).toContain('Description:');

    const projectWide = adaptGet(
      snapshot,
      get('/api/v1/tasks/search', new HttpParams().set('query', 'in:all id:1').set('track', 'PM')),
    ) as { id: string }[];
    expect(projectWide.map((result) => result.id)).toEqual(['PM-0001', 'WEB-0001']);

    const filtersOnly = adaptGet(
      snapshot,
      get('/api/v1/tasks/search', new HttpParams().set('query', 'state:done track:PM')),
    ) as { id: string; snippet: string }[];
    expect(filtersOnly).toEqual([
      expect.objectContaining({ id: 'PM-0002', snippet: 'Needle once.' }),
    ]);
  });

  it('returns task syntax errors and searches wiki titles, paths, and bodies locally', () => {
    expect(() =>
      adaptGet(
        snapshot,
        get('/api/v1/tasks/search', new HttpParams().set('query', 'state: track:PM')),
      ),
    ).toThrow();

    const wiki = adaptGet(
      snapshot,
      get('/api/v1/wiki/search', new HttpParams().set('query', 'needle').set('limit', '1')),
    ) as { path: string; matchCount: number | string; snippet: string }[];
    expect(wiki).toEqual([
      expect.objectContaining({
        path: 'guide/next',
        matchCount: 3,
        snippet: expect.stringContaining('Needle needle'),
      }),
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
    activation: eligibleActivation(),
    descriptionPreview: id,
    modifiedAt: '2026-01-02T00:00:00Z',
  };
}

function eligibleActivation() {
  return {
    isEligible: true,
    milestoneLifecycle: 'active',
    requiredActivationTriggers: [],
    unmetActivationTriggers: [],
    summary: 'Eligible: milestone launch is active.',
  };
}
