import { TestBed } from '@angular/core/testing';
import { Router } from '@angular/router';
import { of, throwError } from 'rxjs';

import type { StaticSnapshot } from './static-snapshot.interceptor';
import { StaticSnapshotStore } from './static-snapshot.interceptor';
import { StaticRootRoute } from './static-root-route';

describe('StaticRootRoute', () => {
  afterEach(() => TestBed.resetTestingModule());

  it.each([
    ['ready', '/overview'],
    ['invalid', '/overview'],
    ['disabled', '/tasks'],
  ] as const)('routes a %s snapshot root to %s', (status, target) => {
    const navigateByUrl = vi.fn(() => Promise.resolve(true));
    TestBed.configureTestingModule({
      imports: [StaticRootRoute],
      providers: [
        { provide: Router, useValue: { navigateByUrl } },
        {
          provide: StaticSnapshotStore,
          useValue: {
            snapshot: of({ overview: { status } } as StaticSnapshot),
          },
        },
      ],
    });

    TestBed.createComponent(StaticRootRoute).detectChanges();

    expect(navigateByUrl).toHaveBeenCalledWith(target, { replaceUrl: true });
  });

  it('keeps a snapshot load failure distinct from a disabled site', () => {
    const navigateByUrl = vi.fn(() => Promise.resolve(true));
    TestBed.configureTestingModule({
      imports: [StaticRootRoute],
      providers: [
        { provide: Router, useValue: { navigateByUrl } },
        {
          provide: StaticSnapshotStore,
          useValue: { snapshot: throwError(() => new Error('Snapshot could not be loaded.')) },
        },
      ],
    });

    const fixture = TestBed.createComponent(StaticRootRoute);
    fixture.detectChanges();

    expect(navigateByUrl).not.toHaveBeenCalled();
    expect((fixture.nativeElement as HTMLElement).textContent).toContain(
      'Snapshot could not be loaded.',
    );
  });
});
