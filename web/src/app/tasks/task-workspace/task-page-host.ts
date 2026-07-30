import { Component, computed, inject, viewChild } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute, Router } from '@angular/router';

import type { DirtyDialogRoute } from '../task-dialog/task-dialog.types';
import { TaskNavigationService } from '../task-navigation.service';
import { TaskWorkspace } from './task-workspace';

@Component({
  selector: 'pm-task-page-host',
  imports: [TaskWorkspace],
  template: `
    <section class="task-page pm-frosted-surface" aria-label="Task workspace">
      <pm-task-workspace
        #workspaceView
        presentation="page"
        [mode]="mode()"
        [taskId]="taskId()"
        (closeIntent)="close()"
        (created)="created($event.id, $event.close)"
      />
    </section>
  `,
  styles: `
    :host {
      display: block;
      height: 100%;
      min-width: 0;
    }
    .task-page {
      position: relative;
      width: 100%;
      height: 100%;
      box-sizing: border-box;
      padding: var(--pm-space-5);
    }
    :host .task-page {
      --pm-frosted-surface-radius: var(--pm-radius-md);

      width: calc(100% - var(--pm-content-surface-margin) - var(--pm-content-surface-margin));
      height: calc(100% - var(--pm-content-surface-margin) - var(--pm-content-surface-margin));
      margin: var(--pm-content-surface-margin);
      padding: var(--pm-space-4);
      background: var(--pm-frosted-surface-fill);
    }
    :host .task-page > * {
      position: relative;
      z-index: 1;
    }
    @media (max-width: 767px) {
      :host {
        height: auto;
        min-height: 100%;
      }
      .task-page {
        height: auto;
        min-height: 100%;
        padding: var(--pm-space-3);
      }
      :host .task-page {
        width: 100%;
        height: auto;
        min-height: 100%;
        margin: 0;
        border: 0;
        border-radius: 0;
        padding: var(--pm-space-3);
      }
    }
  `,
})
export class TaskPageHost implements DirtyDialogRoute {
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly navigation = inject(TaskNavigationService);
  private readonly workspace = viewChild(TaskWorkspace);
  private readonly routeData = toSignal(this.route.data, {
    initialValue: this.route.snapshot.data,
  });
  private readonly routeParams = toSignal(this.route.paramMap, {
    initialValue: this.route.snapshot.paramMap,
  });
  protected readonly mode = computed(() =>
    this.routeData()['mode'] === 'create' ? 'create' : 'detail',
  );
  protected readonly taskId = computed(() => this.routeParams().get('taskId'));

  canDeactivate(): boolean | Promise<boolean> {
    return this.workspace()?.canDeactivate() ?? true;
  }

  protected close(): void {
    void this.router.navigateByUrl(this.navigation.returnUrl(this.router));
  }

  protected created(id: string, close: boolean): void {
    if (close) {
      this.close();
      return;
    }
    void this.router.navigate(['/tasks', id], {
      queryParams: this.router.parseUrl(this.router.url).queryParams,
      state: this.navigation.returnState(this.router),
      replaceUrl: true,
    });
  }
}
