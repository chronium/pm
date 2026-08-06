import { HttpErrorResponse, provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';

import { SettingsApiService, type SettingsResponse } from './settings-api.service';

const settings: SettingsResponse = {
  projectName: 'Atlas',
  accent: 'teal',
  statuses: [{ key: 'todo', name: 'To do' }],
  tracks: [{ key: 'PM', name: 'Product' }],
  milestones: [
    {
      key: 'm one',
      title: 'First',
      priority: 'high',
      description: '',
      requiredActivationTriggers: [],
    },
  ],
  activationTriggers: [],
  priorityOptions: ['none', 'high'],
  revision: 'revision-1',
};

describe('SettingsApiService', () => {
  beforeEach(() =>
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    }),
  );
  afterEach(() => TestBed.inject(HttpTestingController).verify());

  it('uses typed payloads, encoded routes, client identity, and the exact strong settings ETag', () => {
    const api = TestBed.inject(SettingsApiService);
    api.setAccent({ accent: 'purple' }, settings.revision).subscribe();
    api.createStatus({ key: 'blocked', name: 'Blocked' }, settings.revision).subscribe();
    api.renameStatus('waiting/review', { name: 'Waiting' }, settings.revision).subscribe();
    api.removeStatus('done now', settings.revision).subscribe();
    api.createTrack({ key: 'WEB', name: 'Web' }, settings.revision).subscribe();
    api.renameTrack('BUILD/UI', { name: 'Interface' }, settings.revision).subscribe();
    api.removeTrack('OPS now', settings.revision).subscribe();
    api
      .createMilestone({ key: 'm2', title: 'Second', priority: 'high' }, settings.revision)
      .subscribe();
    api.renameMilestone('m/one', { title: 'Launch' }, settings.revision).subscribe();
    api.setMilestonePriority('m one', { priority: 'none' }, settings.revision).subscribe();
    api
      .setMilestoneDescription('m one', { description: 'A deliverable.' }, settings.revision)
      .subscribe();
    api.removeMilestone('m#old', settings.revision).subscribe();

    const expected = [
      ['PUT', '/api/v1/settings/accent', { accent: 'purple' }],
      ['POST', '/api/v1/settings/statuses', { key: 'blocked', name: 'Blocked' }],
      ['PUT', '/api/v1/settings/statuses/waiting%2Freview', { name: 'Waiting' }],
      ['DELETE', '/api/v1/settings/statuses/done%20now', null],
      ['POST', '/api/v1/settings/tracks', { key: 'WEB', name: 'Web' }],
      ['PUT', '/api/v1/settings/tracks/BUILD%2FUI', { name: 'Interface' }],
      ['DELETE', '/api/v1/settings/tracks/OPS%20now', null],
      ['POST', '/api/v1/settings/milestones', { key: 'm2', title: 'Second', priority: 'high' }],
      ['PUT', '/api/v1/settings/milestones/m%2Fone', { title: 'Launch' }],
      ['PUT', '/api/v1/settings/milestones/m%20one/priority', { priority: 'none' }],
      ['PUT', '/api/v1/settings/milestones/m%20one/description', { description: 'A deliverable.' }],
      ['DELETE', '/api/v1/settings/milestones/m%23old', null],
    ] as const;
    const http = TestBed.inject(HttpTestingController);
    for (const [method, url, body] of expected) {
      const request = http.expectOne(url);
      expect(request.request.method).toBe(method);
      expect(request.request.body).toEqual(body);
      expect(request.request.headers.get('X-PM-Client')).toBe('angular-web');
      expect(request.request.headers.get('If-Match')).toBe('"revision-1"');
      expect(request.request.headers.get('If-Match')).not.toBe('*');
      request.flush({ ...settings, revision: 'revision-2' }, { headers: { ETag: '"revision-2"' } });
    }
  });

  it('uses activation revisions for gate preview and apply operations', () => {
    const api = TestBed.inject(SettingsApiService);
    api.readSettings().subscribe();
    api.readActivation().subscribe();
    api.previewMilestoneRequiredTriggers('m/one', ['beta entry'], 'activation-r1').subscribe();
    api
      .setMilestoneRequiredTriggers('m/one', ['beta entry'], 'preview-r1', true, 'activation-r1')
      .subscribe();

    const http = TestBed.inject(HttpTestingController);
    const settingsRequest = http.expectOne('/api/v1/settings');
    expect(settingsRequest.request.headers.has('If-Match')).toBe(false);
    settingsRequest.flush(settings);
    const activationRequest = http.expectOne('/api/v1/activation');
    expect(activationRequest.request.headers.has('If-Match')).toBe(false);
    activationRequest.flush({ revision: 'activation-r1', triggers: [], milestones: [] });

    const preview = http.expectOne(
      '/api/v1/activation/milestones/m%2Fone/required-triggers-preview',
    );
    expect(preview.request.method).toBe('POST');
    expect(preview.request.body).toEqual({ triggerKeys: ['beta entry'] });
    expect(preview.request.headers.get('If-Match')).toBe('"activation-r1"');
    preview.flush({});

    const apply = http.expectOne('/api/v1/activation/milestones/m%2Fone/required-triggers');
    expect(apply.request.method).toBe('PUT');
    expect(apply.request.body).toEqual({
      triggerKeys: ['beta entry'],
      previewRevision: 'preview-r1',
      allowDeactivation: true,
    });
    expect(apply.request.headers.get('If-Match')).toBe('"activation-r1"');
    apply.flush({});
  });

  it('maps structured service failures and stale conflicts', () => {
    const api = TestBed.inject(SettingsApiService);
    expect(
      api.error(
        new HttpErrorResponse({
          status: 409,
          error: { title: 'Blocked', detail: 'Status is in use.', errorCode: 'status_in_use' },
        }),
        'Fallback',
      ),
    ).toEqual({
      status: 409,
      message: 'Status is in use.',
      conflict: false,
      code: 'status_in_use',
    });
    expect(
      api.error(
        new HttpErrorResponse({
          status: 412,
          error: { title: 'Stale', errorCode: 'precondition_failed' },
        }),
        'Fallback',
      ).conflict,
    ).toBe(true);
    expect(api.error(new HttpErrorResponse({ status: 0 }), 'Fallback').message).toContain(
      'could not be reached',
    );
  });
});
