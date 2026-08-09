import { Component, DestroyRef, effect, inject } from '@angular/core';
import { Title } from '@angular/platform-browser';
import { Router } from '@angular/router';

import { ProjectContextService } from '../core/project-context.service';
import { PmErrorState, PmLoadingState } from '../ui/state/state';
import { OverviewComposition } from './overview-composition';
import { OverviewInvalidState } from './overview-invalid-state';
import { OverviewShell } from './overview-shell';
import { OverviewStore } from './overview.store';

@Component({
  selector: 'pm-overview-page',
  imports: [OverviewComposition, OverviewInvalidState, OverviewShell, PmErrorState, PmLoadingState],
  templateUrl: './overview-page.html',
  styleUrl: './overview-page.css',
})
export class OverviewPage {
  private readonly router = inject(Router);
  private readonly title = inject(Title);
  private readonly destroyRef = inject(DestroyRef);
  private readonly projectContext = inject(ProjectContextService);
  protected readonly overview = inject(OverviewStore);

  constructor() {
    effect(() => {
      const document = this.overview.document();
      if (document?.status === 'disabled') {
        void this.router.navigateByUrl(this.projectContext.tasksRoot(), { replaceUrl: true });
      }
    });
    effect(() => {
      const document = this.overview.document();
      this.title.setTitle(
        document?.status === 'ready' || document?.status === 'invalid'
          ? document.documentTitle
          : 'PM',
      );
    });
    this.destroyRef.onDestroy(() => this.title.setTitle('PM'));
  }
}
