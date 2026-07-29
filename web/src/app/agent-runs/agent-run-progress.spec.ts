import { TestBed } from '@angular/core/testing';

import { projectCheckpoints } from './agent-run-events';
import { AgentRunProgress } from './agent-run-progress';
import { runArtifacts, runInspection } from './agent-runs.fixtures';

describe('AgentRunProgress', () => {
  beforeEach(async () => {
    await TestBed.configureTestingModule({ imports: [AgentRunProgress] }).compileComponents();
  });

  afterEach(() => TestBed.resetTestingModule());

  it('renders lifecycle checkpoints, run context, and artifact metadata', () => {
    const fixture = TestBed.createComponent(AgentRunProgress);
    fixture.componentRef.setInput('inspection', runInspection);
    fixture.componentRef.setInput(
      'checkpoints',
      projectCheckpoints(
        new Set(['accepted', 'queued', 'preparing_workspace', 'starting_runtime', 'running']),
        'running',
        'Codex thread started',
      ),
    );
    fixture.componentRef.setInput('artifacts', runArtifacts);
    fixture.detectChanges();

    const element = fixture.nativeElement as HTMLElement;
    expect(element.querySelectorAll('.checkpoint-list li')).toHaveLength(7);
    expect(element.querySelector('[data-status="active"]')?.textContent).toContain(
      'Codex execution',
    );
    expect(element.textContent).toContain('runner-linux');
    expect(element.textContent).toContain('changes.patch');
    expect(element.textContent).toContain('4.0 KiB');
  });

  it('makes task-revision drift explicit without replacing run progress', () => {
    const fixture = TestBed.createComponent(AgentRunProgress);
    fixture.componentRef.setInput('inspection', {
      ...runInspection,
      taskChanged: true,
      currentTaskRevision: 'task-r3',
    });
    fixture.componentRef.setInput(
      'checkpoints',
      projectCheckpoints(new Set(['accepted', 'running']), 'running', null),
    );
    fixture.detectChanges();

    const element = fixture.nativeElement as HTMLElement;
    expect(element.querySelector('.drift-notice')?.getAttribute('role')).toBe('status');
    expect(element.textContent).toContain('Task changed after launch');
    expect(element.textContent).toContain('task-r1');
    expect(element.textContent).toContain('task-r3');
    expect(element.textContent).toContain('Codex execution');
  });
});
