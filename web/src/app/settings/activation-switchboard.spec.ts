import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Component } from '@angular/core';
import { provideRouter, Router } from '@angular/router';

import type { ActivationSwitchboardResponse } from './activation-api.service';
import { ActivationSwitchboard } from './activation-switchboard';

@Component({ template: '' })
class RouteTarget {}

const response: ActivationSwitchboardResponse = {
  revision: 'r1',
  issues: [
    {
      severity: 'warning',
      code: 'activation_reconciliation_required',
      message: 'automatic-entry has satisfied requirements but no activation record.',
    },
  ],
  milestones: [
    {
      key: 'current',
      title: 'Current release',
      description: '',
      priority: 'high',
      lifecycle: 'active',
      assignedTaskCount: 1,
      doneTaskCount: 0,
      requiredActivationTriggers: [],
      unmetActivationTriggers: [],
      delivery: null,
    },
    {
      key: 'later',
      title: 'Later',
      description: '',
      priority: 'none',
      lifecycle: 'active',
      assignedTaskCount: 1,
      doneTaskCount: 0,
      requiredActivationTriggers: [],
      unmetActivationTriggers: [],
      delivery: null,
    },
  ],
  activationTriggers: [
    {
      key: 'manual-entry',
      title: 'Manual entry',
      isActive: false,
      activation: null,
      satisfiedRequirementCount: 0,
      requirementCount: 0,
      requirementsSatisfied: false,
      isLatchedDespiteUnmetRequirements: false,
      requirements: [],
      consumingMilestones: ['beta'],
    },
    {
      key: 'beta-entry',
      title: 'Beta entry',
      isActive: true,
      activation: {
        at: '2026-08-07T06:00:00Z',
        mode: 'override',
        reason: 'Approved risk.',
        waivedRequirements: [{ kind: 'task', source: 'PM-0002' }],
      },
      satisfiedRequirementCount: 1,
      requirementCount: 2,
      requirementsSatisfied: false,
      isLatchedDespiteUnmetRequirements: true,
      requirements: [
        { kind: 'task', source: 'PM-0001', isSatisfied: true, wasWaivedAtActivation: false },
        { kind: 'task', source: 'PM-0002', isSatisfied: false, wasWaivedAtActivation: true },
      ],
      consumingMilestones: ['beta'],
    },
  ],
};

describe('ActivationSwitchboard', () => {
  beforeEach(async () =>
    TestBed.configureTestingModule({
      imports: [ActivationSwitchboard],
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
  afterEach(() => TestBed.inject(HttpTestingController).verify());

  async function render(readOnly = false, value = response, endpoint = '/api/v1/activation') {
    const fixture = TestBed.createComponent(ActivationSwitchboard);
    fixture.componentRef.setInput('readOnly', readOnly);
    fixture.detectChanges();
    const http = TestBed.inject(HttpTestingController);
    http.expectOne(endpoint).flush(value);
    await fixture.whenStable();
    fixture.detectChanges();
    return { fixture, element: fixture.nativeElement as HTMLElement, http };
  }

  it('renders status, requirement links, provenance, consumers, and contextual actions', async () => {
    const { fixture, element } = await render();
    expect(element.textContent).toContain('Manual activation required');
    expect(element.textContent).toContain('Active by override — 1 / 2');
    expect(element.textContent).toContain('Reconciliation required.');
    const beta = element.querySelectorAll('details')[1] as HTMLDetailsElement;
    beta.open = true;
    fixture.detectChanges();
    expect(beta.querySelector('a')?.getAttribute('href')).toBe('/tasks/PM-0001');
    expect(beta.textContent).toContain('Approved risk.');
    expect(beta.textContent).toContain('Waived at activation');
    expect(beta.textContent).toContain('Reset…');
    expect(beta.textContent).toContain('Redefine…');
  });

  it('keeps requirement and consumer links inside the selected linked project', async () => {
    await TestBed.inject(Router).navigateByUrl('/projects/child/tasks/settings');
    const { fixture, element } = await render(false, response, '/api/v1/projects/child/activation');
    const beta = element.querySelectorAll('details')[1] as HTMLDetailsElement;
    beta.open = true;
    fixture.detectChanges();

    expect(beta.querySelector('section[aria-label="Requirements"] a')?.getAttribute('href')).toBe(
      '/projects/child/tasks/PM-0001',
    );
    expect(
      beta.querySelector('section[aria-label="Consuming milestones"] a')?.getAttribute('href'),
    ).toBe('/projects/child/tasks?milestone=beta');
  });

  it('activates a manual-only trigger through the confirmation dialog', async () => {
    const { fixture, element, http } = await render();
    const manual = element.querySelector('details') as HTMLDetailsElement;
    manual.open = true;
    fixture.detectChanges();
    (manual.querySelector('.pm-button--primary') as HTMLButtonElement).click();
    fixture.detectChanges();
    const dialog = element.querySelector('.activation-dialog') as HTMLDialogElement;
    expect(dialog.open || dialog.hasAttribute('open')).toBe(true);
    expect(dialog.textContent).toContain('Activate Manual entry?');
    (dialog.querySelector('.pm-button--primary') as HTMLButtonElement).click();
    await Promise.resolve();
    const request = http.expectOne('/api/v1/activation/triggers/manual-entry/activate');
    expect(request.request.headers.get('If-Match')).toBe('"r1"');
    request.flush({
      changed: true,
      switchboard: {
        ...response,
        revision: 'r2',
        activationTriggers: [
          {
            ...response.activationTriggers[0]!,
            isActive: true,
            activation: {
              at: '2026-08-07T07:00:00Z',
              mode: 'manual',
              reason: null,
              waivedRequirements: [],
            },
          },
          response.activationTriggers[1]!,
        ],
      },
    });
    await fixture.whenStable();
    fixture.detectChanges();
    expect(element.textContent).toContain('Active manually');
  });

  it('creates a manual-only definition and reports the settings-level change', async () => {
    const { fixture, element, http } = await render();
    let changed = 0;
    fixture.componentInstance.definitionChanged.subscribe(() => (changed += 1));
    [...element.querySelectorAll<HTMLButtonElement>('button')]
      .find((button) => button.textContent?.includes('Add trigger'))!
      .click();
    fixture.detectChanges();
    const dialog = element.querySelector('.create-dialog') as HTMLDialogElement;
    expect(dialog.open || dialog.hasAttribute('open')).toBe(true);
    const kind = dialog.querySelector('select') as HTMLSelectElement;
    kind.value = 'milestone';
    kind.dispatchEvent(new Event('change'));
    fixture.detectChanges();
    const milestoneSearch = dialog.querySelector('input[type="search"]') as HTMLInputElement;
    milestoneSearch.focus();
    milestoneSearch.dispatchEvent(new Event('focus'));
    fixture.detectChanges();
    expect(dialog.textContent).toContain('Later');
    kind.value = 'task';
    kind.dispatchEvent(new Event('change'));
    const inputs = dialog.querySelectorAll<HTMLInputElement>('.identity-fields input');
    inputs[0]!.value = 'launch-authorized';
    inputs[0]!.dispatchEvent(new Event('input'));
    inputs[1]!.value = 'Launch authorized';
    inputs[1]!.dispatchEvent(new Event('input'));
    fixture.detectChanges();
    [...dialog.querySelectorAll<HTMLButtonElement>('button')]
      .find((button) => button.textContent?.includes('Create manual-only trigger'))!
      .click();
    await Promise.resolve();
    const request = http.expectOne('/api/v1/activation/triggers');
    expect(request.request.body).toEqual({
      key: 'launch-authorized',
      title: 'Launch authorized',
      requirements: [],
    });
    request.flush({
      changed: true,
      switchboard: {
        ...response,
        revision: 'r2',
        activationTriggers: [
          ...response.activationTriggers,
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
      },
    });
    await fixture.whenStable();
    fixture.detectChanges();
    expect(element.textContent).toContain('Launch authorized');
    expect(changed).toBe(1);
  });

  it('renames a trigger and refreshes settings-level definitions', async () => {
    const { fixture, element, http } = await render();
    let changed = 0;
    fixture.componentInstance.definitionChanged.subscribe(() => (changed += 1));
    const manual = element.querySelector('details') as HTMLDetailsElement;
    manual.open = true;
    fixture.detectChanges();
    [...manual.querySelectorAll<HTMLButtonElement>('button')]
      .find((button) => button.textContent?.includes('Rename'))!
      .click();
    fixture.detectChanges();
    const dialog = element.querySelector('.rename-dialog') as HTMLDialogElement;
    const input = dialog.querySelector('input') as HTMLInputElement;
    input.value = 'Owner approval';
    input.dispatchEvent(new Event('input'));
    fixture.detectChanges();
    [...dialog.querySelectorAll<HTMLButtonElement>('button')]
      .find((button) => button.textContent?.includes('Save title'))!
      .click();
    await Promise.resolve();
    const request = http.expectOne('/api/v1/activation/triggers/manual-entry/title');
    expect(request.request.body).toEqual({ title: 'Owner approval' });
    request.flush({
      changed: true,
      switchboard: {
        ...response,
        revision: 'r2',
        activationTriggers: [
          { ...response.activationTriggers[0]!, title: 'Owner approval' },
          response.activationTriggers[1]!,
        ],
      },
    });
    await fixture.whenStable();
    fixture.detectChanges();
    expect(element.textContent).toContain('Owner approval');
    expect(changed).toBe(1);
  });

  it('edits inactive requirements directly and guards removal while consumed', async () => {
    const { fixture, element, http } = await render();
    let changed = 0;
    fixture.componentInstance.definitionChanged.subscribe(() => (changed += 1));
    const manual = element.querySelector('details') as HTMLDetailsElement;
    manual.open = true;
    fixture.detectChanges();
    const remove = [...manual.querySelectorAll<HTMLButtonElement>('button')].find((button) =>
      button.textContent?.includes('Remove'),
    )!;
    expect(remove.disabled).toBe(true);
    expect(manual.textContent).toContain('Detach from beta before removing.');

    [...manual.querySelectorAll<HTMLButtonElement>('button')]
      .find((button) => button.textContent?.includes('Edit requirements'))!
      .click();
    fixture.detectChanges();
    const dialog = element.querySelector('.requirements-dialog') as HTMLDialogElement;
    const kind = dialog.querySelector('select') as HTMLSelectElement;
    kind.value = 'milestone';
    kind.dispatchEvent(new Event('change'));
    fixture.detectChanges();
    const search = dialog.querySelector('input[type="search"]') as HTMLInputElement;
    search.focus();
    search.dispatchEvent(new Event('focus'));
    fixture.detectChanges();
    (dialog.querySelector('[role="option"]') as HTMLButtonElement).click();
    fixture.detectChanges();
    [...dialog.querySelectorAll<HTMLButtonElement>('button')]
      .find((button) => button.textContent?.includes('Save requirements'))!
      .click();
    await Promise.resolve();
    const request = http.expectOne('/api/v1/activation/triggers/manual-entry/requirements');
    expect(request.request.body).toEqual({
      requirements: [{ kind: 'milestone', source: 'current' }],
    });
    request.flush({ changed: true, switchboard: { ...response, revision: 'r2' } });
    await fixture.whenStable();
    expect(changed).toBe(1);
  });

  it('warns before removing active provenance and keeps service guard errors in the dialog', async () => {
    const unconsumed = {
      ...response,
      activationTriggers: [
        response.activationTriggers[0]!,
        { ...response.activationTriggers[1]!, consumingMilestones: [] },
      ],
    };
    const { fixture, element, http } = await render(false, unconsumed);
    let changed = 0;
    fixture.componentInstance.definitionChanged.subscribe(() => (changed += 1));
    const beta = element.querySelectorAll('details')[1] as HTMLDetailsElement;
    beta.open = true;
    fixture.detectChanges();
    [...beta.querySelectorAll<HTMLButtonElement>('button')]
      .find((button) => button.textContent?.includes('Remove'))!
      .click();
    fixture.detectChanges();
    const dialog = [
      ...element.querySelectorAll<HTMLDialogElement>('pm-confirm-dialog dialog'),
    ].find((candidate) => candidate.textContent?.includes('Remove Beta entry?'))!;
    expect(dialog.textContent).toContain('activation provenance');
    [...dialog.querySelectorAll<HTMLButtonElement>('button')]
      .find((button) => button.textContent?.includes('Remove trigger'))!
      .click();
    await Promise.resolve();
    http.expectOne('/api/v1/activation/triggers/beta-entry').flush(
      {
        title: 'Trigger in use',
        detail: 'Activation trigger beta-entry is required by milestone(s): beta.',
        errorCode: 'activation_trigger_in_use',
      },
      { status: 409, statusText: 'Conflict' },
    );
    await fixture.whenStable();
    fixture.detectChanges();
    expect(dialog.open || dialog.hasAttribute('open')).toBe(true);
    expect(dialog.querySelector('[role="alert"]')?.textContent).toContain('required by milestone');
    [...dialog.querySelectorAll<HTMLButtonElement>('button')]
      .find((button) => button.textContent?.includes('Remove trigger'))!
      .click();
    await Promise.resolve();
    http.expectOne('/api/v1/activation/triggers/beta-entry').flush({
      changed: true,
      switchboard: {
        ...unconsumed,
        revision: 'r2',
        activationTriggers: [unconsumed.activationTriggers[0]!],
      },
    });
    await fixture.whenStable();
    fixture.detectChanges();
    expect(changed).toBe(1);
    expect(element.textContent).not.toContain('Beta entry');
  });

  it('keeps inspection but hides every lifecycle control when read-only', async () => {
    const { element } = await render(true);
    expect(element.textContent).toContain('Controls are hidden');
    expect(element.querySelectorAll('.trigger-actions button')).toHaveLength(0);
    expect(element.querySelector('.section-heading button')).toBeNull();
  });
});
