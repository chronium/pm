import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';

import { TaskNavigationService } from './task-navigation.service';

@Component({ template: '' })
class EmptyRoute {}

describe('TaskNavigationService', () => {
  beforeEach(() =>
    TestBed.configureTestingModule({
      providers: [
        provideRouter([
          {
            path: 'tasks',
            children: [
              { path: 'dialog/:taskId', component: EmptyRoute },
              { path: ':taskId', component: EmptyRoute },
              { path: '', component: EmptyRoute },
            ],
          },
        ]),
      ],
    }),
  );
  afterEach(() => vi.unstubAllGlobals());

  it('keeps canonical hrefs while desktop activation opens a scoped dialog route', async () => {
    const router = TestBed.inject(Router);
    const navigation = TestBed.inject(TaskNavigationService);
    await router.navigateByUrl('/tasks?track=PM&state=todo');
    expect(navigation.canonicalHref(router, 'PM-0060')).toBe('/tasks/PM-0060?track=PM&state=todo');
    vi.stubGlobal(
      'matchMedia',
      vi.fn(() => ({ matches: false }) as MediaQueryList),
    );
    const event = new MouseEvent('click', { button: 0, cancelable: true });
    await navigation.openDialog(event, router, 'PM-0060');
    expect(event.defaultPrevented).toBe(true);
    expect(router.url).toBe('/tasks/dialog/PM-0060?track=PM&state=todo');
  });

  it('uses canonical pages on mobile and leaves modified activation to the browser', async () => {
    const router = TestBed.inject(Router);
    const navigation = TestBed.inject(TaskNavigationService);
    await router.navigateByUrl('/tasks?milestone=m1');
    vi.stubGlobal(
      'matchMedia',
      vi.fn(() => ({ matches: true }) as MediaQueryList),
    );
    const mobile = new MouseEvent('click', { button: 0, cancelable: true });
    await navigation.openDialog(mobile, router, 'PM-0060');
    expect(router.url).toBe('/tasks/PM-0060?milestone=m1');

    await router.navigateByUrl('/tasks?milestone=m1');
    const modified = new MouseEvent('click', { button: 0, ctrlKey: true, cancelable: true });
    await navigation.openDialog(modified, router, 'PM-0060');
    expect(modified.defaultPrevented).toBe(false);
    expect(router.url).toBe('/tasks?milestone=m1');
  });
});
