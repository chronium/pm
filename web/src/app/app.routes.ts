import { Routes } from '@angular/router';

import { canLeaveDirtyDialog } from './tasks/task-dialog/task-dialog.types';
import { TasksBoard } from './tasks/tasks-board';
import { TasksShell } from './tasks/tasks-shell';
import { WikiIndex } from './wiki/wiki-index';
import { WikiShell } from './wiki/wiki-shell';

export enum AppShell {
  Tasks = 'tasks',
  Wiki = 'wiki',
}

export const routes: Routes = [
  { path: '', pathMatch: 'full', redirectTo: 'tasks' },
  {
    path: 'tasks',
    component: TasksShell,
    data: { shell: AppShell.Tasks },
    children: [{
      path: '',
      component: TasksBoard,
      children: [
        {
          path: 'new',
          loadComponent: () => import('./tasks/task-dialog/task-create-dialog').then((module) => module.TaskCreateDialog),
          canDeactivate: [canLeaveDirtyDialog],
        },
        {
          path: ':taskId',
          loadComponent: () => import('./tasks/task-dialog/task-detail-dialog').then((module) => module.TaskDetailDialog),
          canDeactivate: [canLeaveDirtyDialog],
        },
      ],
    }],
  },
  {
    path: 'wiki',
    component: WikiShell,
    data: { shell: AppShell.Wiki },
    children: [{ path: '', component: WikiIndex }],
  },
  { path: '**', redirectTo: 'tasks' },
];
