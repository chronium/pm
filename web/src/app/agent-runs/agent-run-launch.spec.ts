import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';

import {
  acceptedRun,
  readyPreflight,
  runnerRegistration,
  runnerStatus,
} from './agent-runs.fixtures';
import { AgentRunLaunch } from './agent-run-launch';

const linkedProjects = {
  activeProjectId: 'project-current',
  members: [
    {
      projectId: 'project-current',
      name: 'Current project',
      alias: null,
      relationship: 'current',
      status: 'available',
      source: 'local',
      readable: true,
      writeTrusted: true,
    },
    {
      projectId: 'project-engine',
      name: 'Shared engine',
      alias: 'engine',
      relationship: 'sibling',
      status: 'available',
      source: 'local',
      readable: true,
      writeTrusted: false,
    },
    {
      projectId: 'project-missing',
      name: 'Unavailable game',
      alias: 'missing',
      relationship: 'sibling',
      status: 'unavailable',
      source: 'local',
      readable: false,
      writeTrusted: false,
    },
  ],
  warnings: [],
};

describe('AgentRunLaunch', () => {
  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [AgentRunLaunch],
      providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting()],
    }).compileComponents();
  });

  afterEach(() => {
    TestBed.inject(HttpTestingController).verify();
    TestBed.resetTestingModule();
  });

  async function render(status = runnerStatus) {
    const fixture = TestBed.createComponent(AgentRunLaunch);
    fixture.componentRef.setInput('taskId', 'AGENT-0010');
    fixture.componentRef.setInput('taskTitle', 'Angular runner launch');
    fixture.componentRef.setInput('open', true);
    fixture.detectChanges();
    const http = TestBed.inject(HttpTestingController);
    http.expectOne('/api/v1/runners').flush([runnerRegistration]);
    http.expectOne('/api/v1/project/links').flush(linkedProjects);
    http.expectOne('/api/v1/runners/runner-linux/status').flush(status);
    await fixture.whenStable();
    fixture.detectChanges();
    return { fixture, element: fixture.nativeElement as HTMLElement, http };
  }

  async function ready() {
    const rendered = await render();
    const { fixture, element, http } = rendered;
    expect((element.querySelector('select') as HTMLSelectElement).value).toBe('runner-linux');
    expect(element.textContent).toContain('Open network profile');
    const check = [...element.querySelectorAll<HTMLButtonElement>('button')].find(
      (button) => button.textContent?.trim() === 'Check readiness',
    )!;
    check.click();
    const request = http.expectOne('/api/v1/runs/preflight');
    expect(request.request.body).toEqual({
      taskId: 'AGENT-0010',
      runnerId: 'runner-linux',
      profileId: 'pm-development',
      providerId: 'codex',
      modelId: 'gpt-5.4',
      effortId: 'medium',
      linkedContexts: [],
    });
    request.flush(readyPreflight, { headers: { ETag: '"draft-r1"' } });
    await fixture.whenStable();
    fixture.detectChanges();
    return rendered;
  }

  it('uses advertised defaults and reviews the exact ready specification before Start', async () => {
    const { element } = await ready();
    expect(element.textContent).toContain('Ready to start.');
    expect(element.textContent).toContain('1234567890abcdef1234567890abcdef12345678');
    expect(element.textContent).toContain('task-r1');
    expect(element.textContent).toContain('6 CPU');
    expect(element.textContent).toContain('12 GiB');
    expect(element.textContent).toContain('npm run frontend:validate');
    const start = [...element.querySelectorAll<HTMLButtonElement>('button')].find(
      (button) => button.textContent?.trim() === 'Start run',
    )!;
    expect(start.disabled).toBe(false);
  });

  it('opts into readable linked wiki context as required and invalidates readiness changes', async () => {
    const { fixture, element, http } = await render();
    expect(element.textContent).toContain('Shared engine');
    expect(element.textContent).not.toContain('Unavailable game');

    const context = element.querySelector<HTMLInputElement>('.context-project input')!;
    expect(context.checked).toBe(false);
    context.click();
    fixture.detectChanges();

    const requirement = element.querySelector<HTMLSelectElement>('.context-requirement select')!;
    expect(requirement.disabled).toBe(false);
    expect(requirement.value).toBe('required');

    [...element.querySelectorAll<HTMLButtonElement>('button')]
      .find((button) => button.textContent?.trim() === 'Check readiness')!
      .click();
    const request = http.expectOne('/api/v1/runs/preflight');
    expect(request.request.body.linkedContexts).toEqual([
      { projectId: 'project-engine', requirement: 'required' },
    ]);
    request.flush(readyPreflight, { headers: { ETag: '"draft-r1"' } });
    await fixture.whenStable();
    fixture.detectChanges();
    expect(element.textContent).toContain('Immutable run specification');

    requirement.value = 'optional';
    requirement.dispatchEvent(new Event('change'));
    fixture.detectChanges();
    expect(element.textContent).not.toContain('Immutable run specification');
  });

  it('starts once with the strong preflight ETag and settles into an accepted state', async () => {
    const { fixture, element, http } = await ready();
    const started: string[] = [];
    fixture.componentInstance.runStarted.subscribe((result) => started.push(result.run.runId));
    const start = [...element.querySelectorAll<HTMLButtonElement>('button')].find(
      (button) => button.textContent?.trim() === 'Start run',
    )!;
    start.click();
    fixture.detectChanges();
    expect(start.disabled).toBe(true);
    const request = http.expectOne('/api/v1/runs/run-01K123/start');
    expect(request.request.headers.get('If-Match')).toBe('"draft-r1"');
    request.flush(acceptedRun, { status: 202, statusText: 'Accepted' });
    await fixture.whenStable();
    fixture.detectChanges();
    expect(started).toEqual(['run-01K123']);
    expect(element.textContent).toContain('Run accepted');
    expect(element.textContent).toContain('will continue if PM disconnects');
    expect(
      [...element.querySelectorAll<HTMLButtonElement>('button')].filter(
        (button) => button.textContent?.trim() === 'Start run',
      ),
    ).toHaveLength(0);
  });

  it('invalidates a persisted preflight when a capability selection changes', async () => {
    const { fixture, element } = await ready();
    const effort = [...element.querySelectorAll<HTMLSelectElement>('select')].find((select) =>
      select.parentElement?.textContent?.includes('Reasoning effort'),
    )!;
    effort.value = 'high';
    effort.dispatchEvent(new Event('change'));
    fixture.detectChanges();
    expect(element.textContent).not.toContain('Immutable run specification');
    expect(
      [...element.querySelectorAll<HTMLButtonElement>('button')].find(
        (button) => button.textContent?.trim() === 'Start run',
      )!.disabled,
    ).toBe(true);
  });

  it('shows an incompatible runner without inventing provider or model choices', async () => {
    const incompatible = {
      ...runnerStatus,
      capabilities: { ...runnerStatus.capabilities, agentProviders: [] },
    };
    const { element } = await render(incompatible);
    expect(element.textContent).toContain('does not advertise a compatible Codex provider');
    const check = [...element.querySelectorAll<HTMLButtonElement>('button')].find(
      (button) => button.textContent?.trim() === 'Check readiness',
    )!;
    expect(check.disabled).toBe(true);
  });

  it('clears a stale preflight and requires an explicit readiness recheck', async () => {
    const { fixture, element, http } = await ready();
    [...element.querySelectorAll<HTMLButtonElement>('button')]
      .find((button) => button.textContent?.trim() === 'Start run')!
      .click();
    http
      .expectOne('/api/v1/runs/run-01K123/start')
      .flush(
        { errorCode: 'preflight_stale', detail: 'The task revision changed.' },
        { status: 412, statusText: 'Precondition Failed' },
      );
    await fixture.whenStable();
    fixture.detectChanges();
    expect(element.textContent).toContain('preflight is stale');
    expect(element.textContent).not.toContain('Immutable run specification');
  });
});
