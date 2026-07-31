import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { of } from 'rxjs';

import { StaticSnapshotStore, type StaticSnapshot } from './static-snapshot.interceptor';
import { StaticProjectSwitcher } from './static-project-switcher';

describe('StaticProjectSwitcher', () => {
  it('orders linked projects and explains sites that are unavailable', () => {
    const snapshot = {
      project: {
        projectId: 'games',
        name: '<Games>',
        accent: 'teal',
        relationship: 'current',
        readOnly: true,
        revision: 'static',
      },
      linkedProjects: [
        {
          projectId: 'parent',
          name: 'Parent',
          alias: 'parent',
          relationship: 'parent',
          publicSiteUrl: 'https://example.test/parent/',
        },
        {
          projectId: 'sibling',
          name: 'Sibling',
          alias: null,
          relationship: 'sibling',
          publicSiteUrl: null,
        },
      ],
    } as StaticSnapshot;
    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        { provide: StaticSnapshotStore, useValue: { snapshot: of(snapshot) } },
      ],
    });

    const fixture = TestBed.createComponent(StaticProjectSwitcher);
    fixture.detectChanges();
    const details = fixture.nativeElement.querySelector('details');
    const summary = fixture.nativeElement.querySelector('summary');
    const caret = fixture.nativeElement.querySelector('.project-switcher-caret');
    const entries = fixture.nativeElement.querySelectorAll('.project-switcher-menu > *');
    expect(summary.getAttribute('role')).toBe('button');
    expect(summary.getAttribute('aria-label')).toBe('Switch project from <Games>');
    expect(caret.getAttribute('name')).toBe('cssChevronRight');
    expect(entries).toHaveLength(3);
    expect(entries[0].textContent).toContain('<Games>');
    expect(entries[1].getAttribute('href')).toBe('https://example.test/parent/');
    expect(entries[2].getAttribute('title')).toContain('does not publish');
    expect(fixture.nativeElement.innerHTML).toContain('&lt;Games&gt;');

    details.open = true;
    summary.dispatchEvent(new Event('pointerdown', { bubbles: true }));
    expect(details.open).toBe(true);
    document.body.dispatchEvent(new Event('pointerdown', { bubbles: true }));
    expect(details.open).toBe(false);
  });
});
