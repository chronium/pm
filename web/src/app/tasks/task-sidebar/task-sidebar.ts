import { Component, computed, inject } from '@angular/core';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { cssAdd, cssOptions, cssPlayTrackNext } from '@ng-icons/css.gg';
import { NavigationEnd, Router, RouterLink } from '@angular/router';
import { toSignal } from '@angular/core/rxjs-interop';
import { filter, map } from 'rxjs';

import { LayoutService } from '../../core/layout.service';
import { TaskNavigationService } from '../task-navigation.service';
import { TaskSidebarStore } from './task-sidebar.store';
import { StaticModeService } from '../../static/static-mode.service';

@Component({
  selector: 'pm-task-sidebar',
  imports: [NgIcon, RouterLink],
  providers: [provideIcons({ cssAdd, cssOptions, cssPlayTrackNext })],
  templateUrl: './task-sidebar.html',
  styleUrl: './task-sidebar.css',
})
export class TaskSidebar {
  protected readonly store = inject(TaskSidebarStore);
  private readonly router = inject(Router);
  private readonly layout = inject(LayoutService);
  private readonly navigation = inject(TaskNavigationService);
  protected readonly staticMode = inject(StaticModeService);
  private readonly url = toSignal(
    this.router.events.pipe(
      filter((event): event is NavigationEnd => event instanceof NavigationEnd),
      map(() => this.router.url),
    ),
    { initialValue: this.router.url },
  );
  protected readonly settingsActive = computed(() => this.path() === '/tasks/settings');
  protected readonly activeTrack = computed(() =>
    this.settingsActive() ? null : this.queryValue('track'),
  );
  protected readonly activeMilestone = computed(() =>
    this.settingsActive() ? null : this.queryValue('milestone'),
  );
  protected readonly allActive = computed(
    () => !this.settingsActive() && !this.activeTrack() && !this.activeMilestone(),
  );

  protected select(event: MouseEvent, captureFocus = false): void {
    if (captureFocus) this.navigation.captureOrigin(event.currentTarget);
    this.layout.closeMobileSidebar(false);
  }

  protected openNew(event: MouseEvent): void {
    this.layout.closeMobileSidebar(false);
    this.navigation.openDialog(event, this.router, 'new');
  }

  protected newTaskHref(): string {
    return this.navigation.canonicalHref(this.router, 'new');
  }

  protected async openNext(event: MouseEvent): Promise<void> {
    this.navigation.captureOrigin(event.currentTarget);
    const recommendation = await this.store.recommend(this.activeTrack(), this.activeMilestone());
    if (!recommendation?.found || !recommendation.task) return;
    this.layout.closeMobileSidebar(false);
    await this.navigation.navigateToTask(
      this.router,
      recommendation.task.id,
      recommendation.reason,
    );
  }

  private path(): string {
    return `/${
      this.router
        .parseUrl(this.url())
        .root.children['primary']?.segments.map((segment) => segment.path)
        .join('/') ?? ''
    }`;
  }

  private queryValue(key: string): string | null {
    const value: unknown = this.router.parseUrl(this.url()).queryParams[key];
    return typeof value === 'string' ? value : null;
  }
}
