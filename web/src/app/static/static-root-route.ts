import { Component, DestroyRef, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { Router } from '@angular/router';

import { PmErrorState, PmLoadingState } from '../ui/state/state';
import { StaticSnapshotStore } from './static-snapshot.interceptor';

@Component({
  selector: 'pm-static-root-route',
  imports: [PmErrorState, PmLoadingState],
  template: `
    @if (error(); as errorMessage) {
      <pm-error-state>
        <strong>Could not open this snapshot</strong>
        <span>{{ errorMessage }}</span>
      </pm-error-state>
    } @else {
      <pm-loading-state>Opening project…</pm-loading-state>
    }
  `,
})
export class StaticRootRoute {
  private readonly router = inject(Router);
  private readonly snapshotStore = inject(StaticSnapshotStore);
  private readonly destroyRef = inject(DestroyRef);
  protected readonly error = signal<string | null>(null);

  constructor() {
    this.snapshotStore.snapshot.pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: (snapshot) => {
        const target = snapshot.overview.status === 'disabled' ? '/tasks' : '/overview';
        void this.router.navigateByUrl(target, { replaceUrl: true });
      },
      error: (error: unknown) => {
        this.error.set(
          error instanceof Error ? error.message : 'The static snapshot is unavailable.',
        );
      },
    });
  }
}
