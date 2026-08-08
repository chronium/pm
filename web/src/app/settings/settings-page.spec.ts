import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Component } from '@angular/core';
import { By } from '@angular/platform-browser';
import { provideRouter, Router } from '@angular/router';

import { MarkdownEditor } from '../markdown/markdown-editor';
import type { SettingsResponse, ValidationResponse } from './settings-api.service';
import { SettingsPage } from './settings-page';

@Component({ template: '' })
class RouteTarget {}

const settings: SettingsResponse = {
  projectName: 'Atlas',
  accent: 'teal',
  statuses: [
    { key: 'todo', name: 'To do' },
    { key: 'done', name: 'Done' },
  ],
  tracks: [{ key: 'PM', name: 'Product' }],
  milestones: [
    {
      key: 'long/milestone',
      title: 'A very long milestone title that wraps without obscuring its controls',
      priority: 'high',
      description: 'Deliver a usable release with documented acceptance evidence.',
      requiredActivationTriggers: [],
    },
  ],
  activationTriggers: [
    {
      key: 'beta-entry',
      title: 'Beta entry criteria',
      requirements: [{ kind: 'task', source: 'PM-0001' }],
    },
  ],
  priorityOptions: ['none', 'medium', 'high'],
  revision: 'r1',
};
const validation: ValidationResponse = {
  valid: false,
  issues: [
    {
      severity: 'error',
      code: 'task_state_missing',
      message: 'A long validation message that remains fully available.',
      path: '.pm/tasks/PM-0001.md',
      taskId: 'PM-0001',
      wikiPath: null,
      state: 'missing',
      projectId: null,
      projectAlias: null,
    },
  ],
};
const identity = {
  userId: 'usr_local',
  displayName: 'Chronium',
  publicKey: 'public-key',
  fingerprint: 'ab'.repeat(32),
};
const membership = {
  projectId: 'project-1',
  currentUserId: identity.userId,
  currentRole: 'admin',
  authenticated: true,
  members: [{ ...identity, role: 'admin', isLocal: true }],
};

Object.defineProperty(Range.prototype, 'getBoundingClientRect', {
  configurable: true,
  value: () => new DOMRect(),
});
Object.defineProperty(Range.prototype, 'getClientRects', {
  configurable: true,
  value: () => [],
});

describe('SettingsPage', () => {
  beforeEach(async () =>
    TestBed.configureTestingModule({
      imports: [SettingsPage],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([
          { path: 'tasks/settings', component: RouteTarget },
          { path: 'projects/:projectId/tasks/settings', component: RouteTarget },
        ]),
      ],
    }).compileComponents(),
  );
  afterEach(() => {
    TestBed.inject(HttpTestingController).verify();
    TestBed.resetTestingModule();
  });

  async function render() {
    const fixture = TestBed.createComponent(SettingsPage);
    fixture.detectChanges();
    const http = TestBed.inject(HttpTestingController);
    http.expectOne('/api/v1/settings').flush(settings);
    http.expectOne('/api/v1/validation').flush(validation);
    http.expectOne('/api/v1/project').flush({
      projectId: 'project-1',
      name: settings.projectName,
      accent: settings.accent,
      relationship: 'current',
      readOnly: false,
      revision: 'project-r1',
    });
    await fixture.whenStable();
    fixture.detectChanges();
    http.expectOne('/api/v1/project/identity').flush(identity);
    http.expectOne('/api/v1/project/members').flush(membership);
    http.expectOne('/api/v1/project/invitations').flush({ invitations: [] });
    http.expectOne('/api/v1/runners').flush([]);
    await fixture.whenStable();
    fixture.detectChanges();
    return { fixture, element: fixture.nativeElement as HTMLElement, http };
  }

  async function renderLinked(readOnly: boolean) {
    await TestBed.inject(Router).navigateByUrl('/projects/child/tasks/settings');
    const fixture = TestBed.createComponent(SettingsPage);
    fixture.detectChanges();
    const http = TestBed.inject(HttpTestingController);
    http.expectOne('/api/v1/projects/child/settings').flush({
      ...settings,
      projectName: 'Child project',
      revision: 'child-settings-r1',
    });
    http.expectOne('/api/v1/projects/child/project').flush({
      projectId: 'child',
      name: 'Child project',
      accent: 'teal',
      relationship: 'child',
      readOnly,
      revision: 'child-project-r1',
    });
    await fixture.whenStable();
    fixture.detectChanges();
    return { fixture, element: fixture.nativeElement as HTMLElement, http };
  }

  it('renders dense read-first sections, structured health context, and long values', async () => {
    const { element } = await render();
    expect(
      [...element.querySelectorAll('section h2')].map((heading) => heading.textContent),
    ).toEqual([
      'Project health',
      'Appearance',
      'Project members',
      'Agent runners',
      'Statuses',
      'Milestones',
      'Tracks',
    ]);
    expect(element.textContent).toContain('task_state_missing');
    expect(element.textContent).toContain('.pm/tasks/PM-0001.md');
    expect(element.textContent).toContain('A very long milestone title');
    expect(element.querySelector('.settings-navigation')?.textContent).toContain('Linked projects');
    expect(element.querySelectorAll('.settings-row input')).toHaveLength(0);
    expect(element.textContent).toContain('documented acceptance evidence');
    expect(element.querySelector('.milestone-row button')?.textContent).toContain(
      'Edit deliverable',
    );
  });

  it('loads the activation switchboard only when its settings section is selected', async () => {
    const { fixture, element, http } = await render();
    http.expectNone('/api/v1/activation');

    [...element.querySelectorAll<HTMLButtonElement>('.settings-navigation button')]
      .find((button) => button.textContent?.trim() === 'Activation')!
      .click();
    fixture.detectChanges();
    http.expectOne('/api/v1/activation').flush({
      revision: 'activation-r1',
      activationTriggers: [
        {
          key: 'beta-entry',
          title: 'Beta entry criteria',
          isActive: false,
          activation: null,
          satisfiedRequirementCount: 0,
          requirementCount: 1,
          requirementsSatisfied: false,
          isLatchedDespiteUnmetRequirements: false,
          requirements: [
            {
              kind: 'task',
              source: 'PM-0001',
              isSatisfied: false,
              wasWaivedAtActivation: false,
            },
          ],
          consumingMilestones: ['public-beta'],
        },
      ],
      milestones: [],
      issues: [],
    });
    await fixture.whenStable();
    fixture.detectChanges();

    expect(element.querySelector('pm-activation-switchboard')).not.toBeNull();
    expect(element.textContent).toContain('Pending — 0 / 1');
  });

  it('saves a project-wide accent from General settings', async () => {
    document.documentElement.dataset['accent'] = 'teal';
    const { fixture, element, http } = await render();
    const purple = [...element.querySelectorAll('pm-accent-picker button')].find(
      (button) => button.textContent?.trim() === 'Purple',
    ) as HTMLButtonElement;

    purple.click();
    fixture.detectChanges();
    await Promise.resolve();
    const request = http.expectOne('/api/v1/settings/accent');
    expect(request.request.method).toBe('PUT');
    expect(request.request.headers.get('If-Match')).toBe('"r1"');
    request.flush({ ...settings, accent: 'purple', revision: 'r2' });
    await fixture.whenStable();
    fixture.detectChanges();
    http.expectOne('/api/v1/validation').flush(validation);

    expect(document.documentElement.dataset['accent']).toBe('purple');
    expect(purple.getAttribute('aria-pressed')).toBe('true');
  });

  it('validates create forms, cancels without mutation, and creates a status with retained input on failure', async () => {
    const { fixture, element, http } = await render();
    (
      element.querySelector(
        '.settings-section[aria-labelledby="statuses-heading"] button',
      ) as HTMLButtonElement
    ).click();
    fixture.detectChanges();
    const form = element.querySelector('form.create-form') as HTMLFormElement;
    form.dispatchEvent(new Event('submit'));
    fixture.detectChanges();
    expect(element.textContent).toContain('Key is required.');
    const inputs = form.querySelectorAll('input');
    inputs[0]!.value = 'blocked';
    inputs[0]!.dispatchEvent(new Event('input'));
    inputs[1]!.value = 'Blocked';
    inputs[1]!.dispatchEvent(new Event('input'));
    form.dispatchEvent(new Event('submit'));
    await Promise.resolve();
    fixture.detectChanges();
    http.expectOne('/api/v1/settings/statuses').flush(
      {
        title: 'Duplicate',
        detail: 'Status blocked already exists.',
        errorCode: 'duplicate_status',
      },
      { status: 409, statusText: 'Conflict' },
    );
    await fixture.whenStable();
    fixture.detectChanges();
    expect((element.querySelector('#status-key') as HTMLInputElement).value).toBe('blocked');
    expect(element.textContent).toContain('Status blocked already exists.');
    await vi.waitFor(() => {
      fixture.detectChanges();
      expect((form.querySelector('button[type="button"]') as HTMLButtonElement).disabled).toBe(
        false,
      );
    });
    (form.querySelector('button[type="button"]') as HTMLButtonElement).click();
    fixture.detectChanges();
    expect(element.querySelector('form.create-form')).toBeNull();
  });

  it('opens the deliverable editor immediately after creating a milestone', async () => {
    const { fixture, element, http } = await render();
    const section = element.querySelector(
      '.settings-section[aria-labelledby="milestones-heading"]',
    ) as HTMLElement;
    (section.querySelector('.section-heading button') as HTMLButtonElement).click();
    fixture.detectChanges();
    const form = section.querySelector('form.milestone-create') as HTMLFormElement;
    const key = form.querySelector('#milestone-key') as HTMLInputElement;
    const title = form.querySelector('#milestone-title') as HTMLInputElement;
    key.value = 'launch';
    key.dispatchEvent(new Event('input'));
    title.value = 'Launch';
    title.dispatchEvent(new Event('input'));
    form.dispatchEvent(new Event('submit'));
    await Promise.resolve();

    const request = http.expectOne('/api/v1/settings/milestones');
    expect(request.request.body).toEqual({ key: 'launch', title: 'Launch', priority: 'none' });
    request.flush({
      ...settings,
      milestones: [
        ...settings.milestones,
        {
          key: 'launch',
          title: 'Launch',
          priority: 'none',
          description: '',
          requiredActivationTriggers: [],
        },
      ],
      revision: 'r2',
    });
    await Promise.resolve();
    TestBed.flushEffects();
    http.expectOne('/api/v1/validation').flush(validation);
    await fixture.whenStable();
    fixture.detectChanges();

    const dialog = element.querySelector('.task-dialog') as HTMLDialogElement;
    expect(dialog.getAttribute('aria-label')).toBe('Milestone deliverable');
    expect(dialog.textContent).toContain('Launch');
  });

  it('opens the deliverable editor and saves title, priority, and description independently', async () => {
    const { fixture, element, http } = await render();
    (element.querySelector('.milestone-row .pm-button--secondary') as HTMLButtonElement).click();
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
    const dialog = element.querySelector('.task-dialog') as HTMLDialogElement;
    expect(dialog.open || dialog.hasAttribute('open')).toBe(true);
    expect(dialog.querySelector('.deliverable-key')?.textContent).toContain('long/milestone');
    expect(dialog.querySelector('.title-value')?.textContent).toContain('A very long milestone');

    (
      dialog.querySelector('button[aria-label="Edit milestone title"]') as HTMLButtonElement
    ).click();
    fixture.detectChanges();
    const title = dialog.querySelector('#deliverable-title') as HTMLInputElement;
    title.value = 'Launch';
    title.dispatchEvent(new Event('input'));
    fixture.detectChanges();
    [...dialog.querySelectorAll<HTMLButtonElement>('button')]
      .find((button) => button.textContent?.trim() === 'Save')!
      .click();
    await Promise.resolve();
    const titleRequest = http.expectOne('/api/v1/settings/milestones/long%2Fmilestone');
    expect(titleRequest.request.body).toEqual({ title: 'Launch' });
    titleRequest.flush({
      ...settings,
      milestones: [{ ...settings.milestones[0]!, title: 'Launch' }],
      revision: 'r2',
    });
    await Promise.resolve();
    TestBed.flushEffects();
    http.expectOne('/api/v1/validation').flush(validation);
    await fixture.whenStable();
    fixture.detectChanges();

    (
      dialog.querySelector('button[aria-label="Edit milestone priority"]') as HTMLButtonElement
    ).click();
    fixture.detectChanges();
    const select = dialog.querySelector('#deliverable-priority') as HTMLSelectElement;
    select.value = 'medium';
    select.dispatchEvent(new Event('input'));
    fixture.detectChanges();
    [...select.parentElement!.querySelectorAll<HTMLButtonElement>('button')]
      .find((button) => button.textContent?.trim() === 'Save')!
      .click();
    await Promise.resolve();
    const priority = http.expectOne('/api/v1/settings/milestones/long%2Fmilestone/priority');
    expect(priority.request.body).toEqual({ priority: 'medium' });
    expect(priority.request.headers.get('If-Match')).toBe('"r2"');
    priority.flush({
      ...settings,
      milestones: [{ ...settings.milestones[0]!, title: 'Launch', priority: 'medium' }],
      revision: 'r3',
    });
    await Promise.resolve();
    TestBed.flushEffects();
    http.expectOne('/api/v1/validation').flush(validation);
    await fixture.whenStable();
    fixture.detectChanges();

    (
      dialog.querySelector('button[aria-label="Edit deliverable description"]') as HTMLButtonElement
    ).click();
    fixture.detectChanges();
    const markdown = fixture.debugElement.query(By.directive(MarkdownEditor))
      .componentInstance as MarkdownEditor;
    markdown.value.set('Outcome: ship the beta.\n\nEvidence: acceptance recording.');
    fixture.detectChanges();
    const saveDescription = [...dialog.querySelectorAll<HTMLButtonElement>('button')].find(
      (button) => button.textContent?.trim() === 'Save description',
    )!;
    expect(saveDescription.disabled).toBe(false);
    saveDescription.click();
    await Promise.resolve();
    const description = http.expectOne('/api/v1/settings/milestones/long%2Fmilestone/description');
    expect(description.request.body).toEqual({
      description: 'Outcome: ship the beta.\n\nEvidence: acceptance recording.',
    });
    expect(description.request.headers.get('If-Match')).toBe('"r3"');
    description.flush({
      ...settings,
      milestones: [
        {
          ...settings.milestones[0]!,
          title: 'Launch',
          priority: 'medium',
          description: 'Outcome: ship the beta.\n\nEvidence: acceptance recording.',
        },
      ],
      revision: 'r4',
    });
    await Promise.resolve();
    TestBed.flushEffects();
    http.expectOne('/api/v1/validation').flush(validation);
  });

  it('previews gate eligibility loss before applying the reviewed activation change', async () => {
    const { fixture, element, http } = await render();
    (element.querySelector('.milestone-row .pm-button--secondary') as HTMLButtonElement).click();
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
    const dialog = element.querySelector('.task-dialog') as HTMLDialogElement;
    const gate = dialog.querySelector('.trigger-option input') as HTMLInputElement;
    gate.click();
    fixture.detectChanges();
    [...dialog.querySelectorAll<HTMLButtonElement>('button')]
      .find((button) => button.textContent?.includes('Review changes'))!
      .click();
    await Promise.resolve();
    http.expectOne('/api/v1/activation').flush({
      revision: 'activation-r1',
      activationTriggers: [],
      milestones: [],
      issues: [],
    });
    await Promise.resolve();
    http
      .expectOne('/api/v1/activation/milestones/long%2Fmilestone/required-triggers-preview')
      .flush(
        {
          milestoneKey: 'long/milestone',
          previewRevision: 'preview-r1',
          currentTriggerKeys: [],
          proposedTriggerKeys: ['beta-entry'],
          before: 'active',
          after: 'inactive',
          currentlyEligibleTaskIds: ['PM-0001'],
          taskIdsLosingEligibility: ['PM-0001'],
          requiresConfirmation: true,
        },
        { headers: { ETag: '"activation-r1"' } },
      );
    await fixture.whenStable();
    fixture.detectChanges();
    expect(dialog.textContent).toContain('PM-0001');
    const apply = [...dialog.querySelectorAll<HTMLButtonElement>('button')].find(
      (button) => button.textContent?.trim() === 'Apply',
    )!;
    apply.click();
    await Promise.resolve();
    const applyRequest = http.expectOne(
      '/api/v1/activation/milestones/long%2Fmilestone/required-triggers',
    );
    expect(applyRequest.request.body).toEqual({
      triggerKeys: ['beta-entry'],
      previewRevision: 'preview-r1',
      allowDeactivation: true,
    });
    applyRequest.flush({ revision: 'activation-r2' });
    await Promise.resolve();
    http.expectOne('/api/v1/settings').flush({
      ...settings,
      milestones: [{ ...settings.milestones[0]!, requiredActivationTriggers: ['beta-entry'] }],
      revision: 'r2',
    });
    await Promise.resolve();
    TestBed.flushEffects();
    http.expectOne('/api/v1/validation').flush(validation);
    await fixture.whenStable();
    fixture.detectChanges();
    expect(dialog.textContent).not.toContain('Eligibility impact');
  });

  it('confirms removal, shows row restrictions, and disables actions during a pending mutation', async () => {
    const { fixture, element, http } = await render();
    (element.querySelector('button[aria-label="Remove status"]') as HTMLButtonElement).click();
    fixture.detectChanges();
    const removalDialog = [...element.querySelectorAll('pm-confirm-dialog')].find((dialog) =>
      dialog.textContent?.includes('todo'),
    )!;
    expect(removalDialog.textContent).toContain('todo');
    const confirm = removalDialog.querySelector('.pm-button--danger') as HTMLButtonElement;
    confirm.click();
    await Promise.resolve();
    fixture.detectChanges();
    expect(
      [...element.querySelectorAll<HTMLButtonElement>('.settings-row button')].every(
        (button) => button.disabled,
      ),
    ).toBe(true);
    http
      .expectOne('/api/v1/settings/statuses/todo')
      .flush(
        { title: 'In use', detail: 'Status todo is used by PM-0001.', errorCode: 'status_in_use' },
        { status: 409, statusText: 'Conflict' },
      );
    await fixture.whenStable();
    fixture.detectChanges();
    expect(element.querySelector('.settings-row .row-error')?.textContent).toContain(
      'used by PM-0001',
    );
  });

  it('fetches latest after 412 and supports review then draft restoration', async () => {
    const { fixture, element, http } = await render();
    (element.querySelector('button[aria-label="Edit status name"]') as HTMLButtonElement).click();
    fixture.detectChanges();
    const input = element.querySelector('.settings-row input') as HTMLInputElement;
    input.value = 'Ready';
    input.dispatchEvent(new Event('input'));
    (input.closest('form') as HTMLFormElement).dispatchEvent(new Event('submit'));
    await Promise.resolve();
    http.expectOne('/api/v1/settings/statuses/todo').flush(
      {
        title: 'Stale',
        detail: 'Project settings changed in another client.',
        errorCode: 'precondition_failed',
      },
      { status: 412, statusText: 'Precondition Failed' },
    );
    await Promise.resolve();
    await Promise.resolve();
    TestBed.tick();
    http.expectOne('/api/v1/settings').flush({
      ...settings,
      statuses: [{ key: 'todo', name: 'Server value' }, settings.statuses[1]!],
      revision: 'r9',
    });
    await fixture.whenStable();
    fixture.detectChanges();
    expect((element.querySelector('.settings-row input') as HTMLInputElement).value).toBe('Ready');
    expect(element.textContent).toContain('Review latest');
    (
      element.querySelector('pm-external-change-banner .pm-button--primary') as HTMLButtonElement
    ).click();
    fixture.detectChanges();
    expect(element.querySelector('.settings-row input')).toBeNull();
    expect(element.textContent).toContain('Server value');
    (
      element.querySelector('pm-external-change-banner .pm-button--secondary') as HTMLButtonElement
    ).click();
    fixture.detectChanges();
    expect((element.querySelector('.settings-row input') as HTMLInputElement).value).toBe('Ready');
  });

  it('shows linked project-owned settings without host controls or host health', async () => {
    const { fixture, element, http } = await renderLinked(true);
    const navigation = element.querySelector('.settings-navigation')!;

    expect(element.textContent).toContain('Child project');
    expect(element.querySelector('.project-context-notice')?.textContent).toContain('write trust');
    expect(navigation.textContent).not.toContain('Linked projects');
    expect(navigation.textContent).not.toContain('Members');
    expect(navigation.textContent).not.toContain('Agent runners');
    expect(element.textContent).toContain('Project health is not available from the host project.');
    expect(element.querySelector('pm-project-members')).toBeNull();
    expect(element.querySelector('pm-agent-runners')).toBeNull();
    expect(
      element.querySelector('button[aria-label="Edit status name"]')?.hasAttribute('disabled'),
    ).toBe(true);
    expect(element.querySelector('.milestone-row .pm-button--secondary')?.textContent).toContain(
      'View deliverable',
    );
    http.expectNone('/api/v1/validation');
    http.expectNone('/api/v1/project/identity');
    http.expectNone('/api/v1/project/members');
    http.expectNone('/api/v1/runners');

    [...navigation.querySelectorAll<HTMLButtonElement>('button')]
      .find((button) => button.textContent?.trim() === 'Activation')!
      .click();
    fixture.detectChanges();
    http.expectOne('/api/v1/projects/child/activation').flush({
      revision: 'child-activation-r1',
      activationTriggers: [],
      milestones: [],
      issues: [],
    });
    await fixture.whenStable();
    fixture.detectChanges();
    expect(element.textContent).toContain('Controls are hidden in this read-only project.');
    expect(element.textContent).not.toContain('Add trigger');
  });

  it('sends trusted linked-project edits only to the selected project', async () => {
    const { fixture, element, http } = await renderLinked(false);
    expect(element.querySelector('.project-context-notice')?.textContent).toContain(
      'selected project only',
    );
    const purple = [...element.querySelectorAll('pm-accent-picker button')].find(
      (button) => button.textContent?.trim() === 'Purple',
    ) as HTMLButtonElement;
    expect(purple.disabled).toBe(false);
    purple.click();
    fixture.detectChanges();
    await Promise.resolve();

    const request = http.expectOne('/api/v1/projects/child/settings/accent');
    expect(request.request.headers.get('If-Match')).toBe('"child-settings-r1"');
    request.flush({
      ...settings,
      projectName: 'Child project',
      accent: 'purple',
      revision: 'child-settings-r2',
    });
    await fixture.whenStable();
    fixture.detectChanges();
    http.expectNone('/api/v1/validation');
    http.expectNone('/api/v1/settings/accent');

    [...element.querySelectorAll<HTMLButtonElement>('.settings-navigation button')]
      .find((button) => button.textContent?.trim() === 'Activation')!
      .click();
    fixture.detectChanges();
    http.expectOne('/api/v1/projects/child/activation').flush({
      revision: 'child-activation-r1',
      activationTriggers: [
        {
          key: 'launch-authorized',
          title: 'Launch authorized',
          isActive: false,
          activation: null,
          satisfiedRequirementCount: 0,
          requirementCount: 0,
          requirementsSatisfied: false,
          isLatchedDespiteUnmetRequirements: false,
          requirements: [],
          consumingMilestones: [],
        },
      ],
      milestones: [],
      issues: [],
    });
    await fixture.whenStable();
    fixture.detectChanges();
    const trigger = element.querySelector(
      'pm-activation-switchboard details',
    ) as HTMLDetailsElement;
    trigger.open = true;
    fixture.detectChanges();
    (trigger.querySelector('.pm-button--primary') as HTMLButtonElement).click();
    fixture.detectChanges();
    (
      element.querySelector(
        'pm-activation-trigger-action-dialog .pm-button--primary',
      ) as HTMLButtonElement
    ).click();
    await Promise.resolve();
    const activation = http.expectOne(
      '/api/v1/projects/child/activation/triggers/launch-authorized/activate',
    );
    expect(activation.request.headers.get('If-Match')).toBe('"child-activation-r1"');
    activation.flush({
      changed: true,
      switchboard: {
        revision: 'child-activation-r2',
        activationTriggers: [],
        milestones: [],
        issues: [],
      },
    });
  });
});
