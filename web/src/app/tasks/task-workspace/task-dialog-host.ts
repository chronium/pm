import { Component, computed, inject, OnDestroy, viewChild } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute, Router } from '@angular/router';

import { TaskDialogShell } from '../task-dialog/task-dialog-shell';
import type { DirtyDialogRoute } from '../task-dialog/task-dialog.types';
import { TaskNavigationService } from '../task-navigation.service';
import { TaskWorkspace } from './task-workspace';
import { ProjectContextService } from '../../core/project-context.service';

@Component({
  selector: 'pm-task-dialog-host',
  imports: [TaskDialogShell, TaskWorkspace],
  template: `
    <pm-task-dialog-shell
      dialogTitle="Task workspace"
      [chrome]="false"
      [pending]="workspace()?.pending() ?? false"
      [backdropDismissible]="workspace()?.backdropDismissible() ?? false"
      (closeIntent)="close()"
    >
      <pm-task-workspace
        #workspaceView
        presentation="dialog"
        [mode]="mode()"
        [taskId]="taskId()"
        (closeIntent)="close()"
        (fullscreenIntent)="fullscreen()"
        (created)="created($event.id, $event.close)"
      />
    </pm-task-dialog-shell>
  `,
})
export class TaskDialogHost implements DirtyDialogRoute, OnDestroy {
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly navigation = inject(TaskNavigationService);
  private readonly projectContext = inject(ProjectContextService);
  protected readonly workspace = viewChild(TaskWorkspace);
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

  ngOnDestroy(): void {
    this.navigation.restoreFocus();
  }

  protected close(): void {
    void this.router.navigateByUrl(this.navigation.returnUrl(this.router), { replaceUrl: true });
  }

  protected fullscreen(): void {
    const target =
      this.mode() === 'create'
        ? `${this.projectContext.tasksRoot()}/new`
        : this.projectContext.taskUrl(this.taskId()!);
    const tree = this.router.parseUrl(target);
    tree.queryParams = this.router.parseUrl(this.router.url).queryParams;
    void this.router.navigateByUrl(tree, {
      state: this.navigation.returnState(this.router),
      replaceUrl: true,
    });
  }

  protected created(id: string, close: boolean): void {
    if (close) {
      this.close();
      return;
    }
    const tree = this.router.parseUrl(this.projectContext.taskUrl(id, true));
    tree.queryParams = this.router.parseUrl(this.router.url).queryParams;
    void this.router.navigateByUrl(tree, {
      state: this.navigation.returnState(this.router),
      replaceUrl: true,
    });
  }
}
