import { TestBed } from '@angular/core/testing';

import { ProjectHealth } from './project-health';

describe('ProjectHealth', () => {
  it('renders degraded linked projects as valid warnings with project context', async () => {
    await TestBed.configureTestingModule({ imports: [ProjectHealth] }).compileComponents();
    const fixture = TestBed.createComponent(ProjectHealth);
    fixture.componentRef.setInput('validation', {
      valid: true,
      issues: [
        {
          severity: 'warning',
          code: 'linked_project_missing',
          message: 'Linked project prj_gameplay (gameplay) is missing.',
          path: null,
          taskId: null,
          wikiPath: null,
          state: null,
          projectId: 'prj_gameplay',
          projectAlias: 'gameplay',
        },
      ],
    });
    fixture.detectChanges();

    const element = fixture.nativeElement as HTMLElement;
    expect(element.textContent).toContain('Project validation passed with 1 warning(s).');
    expect(element.textContent).toContain('linked_project_missing');
    expect(element.textContent).toContain('prj_gameplay');
    expect(element.textContent).toContain('gameplay');
    expect(element.textContent).not.toContain('Project validation found');
    expect(element.querySelector('.issue-severity--warning')).not.toBeNull();
  });
});
