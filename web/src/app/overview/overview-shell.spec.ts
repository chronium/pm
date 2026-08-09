import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';

import { OverviewHero } from './overview-hero';
import { OverviewShell } from './overview-shell';

describe('Overview prototype', () => {
  function renderHero({
    projectName = 'Project Model',
    title = 'PM',
    description = 'Local project management for software work.',
  }: {
    projectName?: string;
    title?: string;
    description?: string | null;
  } = {}) {
    TestBed.configureTestingModule({ providers: [provideRouter([])] });
    const fixture = TestBed.createComponent(OverviewHero);
    fixture.componentRef.setInput('projectName', projectName);
    fixture.componentRef.setInput('title', title);
    fixture.componentRef.setInput('description', description);
    fixture.componentRef.setInput('tasksUrl', '/tasks');
    fixture.componentRef.setInput('wikiUrl', '/wiki');
    fixture.detectChanges();
    return fixture.nativeElement as HTMLElement;
  }

  it('keeps the operational project identity when the presentation title differs', () => {
    const element = renderHero();

    expect(element.querySelector('h1')?.textContent).toBe('PM');
    expect(element.querySelector('.overview-project-context')?.textContent).toContain(
      'Project Model',
    );
    expect(element.querySelector('.overview-description')?.textContent).toContain(
      'Local project management',
    );
  });

  it('avoids duplicate identity and absent-description placeholders for fallback content', () => {
    const element = renderHero({
      projectName: 'Project Model',
      title: 'Project Model',
      description: null,
    });

    expect(element.querySelector('.overview-project-context')).toBeNull();
    expect(element.querySelector('.overview-description')).toBeNull();
  });

  it('provides semantic Tasks and Wiki destinations', () => {
    const element = renderHero();
    const links = [...element.querySelectorAll<HTMLAnchorElement>('.overview-actions a')];

    expect(element.querySelector('nav')?.getAttribute('aria-label')).toBe('Project destinations');
    expect(links.map((link) => link.textContent?.trim())).toEqual([
      'View tasks',
      'Read documentation',
    ]);
    expect(links.map((link) => link.getAttribute('href'))).toEqual(['/tasks', '/wiki']);
  });
});

@Component({
  imports: [OverviewShell],
  template: '<pm-overview-shell><p data-testid="projected">Next section</p></pm-overview-shell>',
})
class OverviewShellHost {}

describe('OverviewShell', () => {
  it('projects subsequent Overview sections into the shared surface', () => {
    const fixture = TestBed.createComponent(OverviewShellHost);
    fixture.detectChanges();
    const element = fixture.nativeElement as HTMLElement;

    expect(element.querySelector('main')).not.toBeNull();
    expect(element.querySelector('[data-testid="projected"]')?.textContent).toBe('Next section');
  });
});
