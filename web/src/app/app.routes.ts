import { Routes } from '@angular/router';

import { canLeaveDirtyRoute } from './core/dirty-route';
import { TasksBoard } from './tasks/tasks-board';
import { TasksShell } from './tasks/tasks-shell';
import { WikiIndex } from './wiki/wiki-index';
import { WikiShell } from './wiki/wiki-shell';
import { wikiEditMatcher, wikiMetaMatcher, wikiPathMatcher } from './wiki/wiki.routes';

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
    children: [
      {
        path: 'settings',
        loadComponent: () =>
          import('./settings/settings-page').then((module) => module.SettingsPage),
        canDeactivate: [canLeaveDirtyRoute],
      },
      {
        path: '',
        component: TasksBoard,
        children: [
          {
            path: 'new',
            loadComponent: () =>
              import('./tasks/task-dialog/task-create-dialog').then(
                (module) => module.TaskCreateDialog,
              ),
            canDeactivate: [canLeaveDirtyRoute],
          },
          {
            path: ':taskId',
            loadComponent: () =>
              import('./tasks/task-dialog/task-detail-dialog').then(
                (module) => module.TaskDetailDialog,
              ),
            canDeactivate: [canLeaveDirtyRoute],
          },
        ],
      },
    ],
  },
  {
    path: 'wiki',
    component: WikiShell,
    data: { shell: AppShell.Wiki },
    children: [
      { path: '', pathMatch: 'full', component: WikiIndex },
      {
        path: 'new',
        loadComponent: () => import('./wiki/wiki-create').then((module) => module.WikiCreate),
        canDeactivate: [canLeaveDirtyRoute],
      },
      {
        matcher: wikiEditMatcher,
        loadComponent: () => import('./wiki/wiki-edit').then((module) => module.WikiEdit),
        canDeactivate: [canLeaveDirtyRoute],
      },
      {
        matcher: wikiMetaMatcher,
        loadComponent: () => import('./wiki/wiki-metadata').then((module) => module.WikiMetadata),
        canDeactivate: [canLeaveDirtyRoute],
      },
      {
        matcher: wikiPathMatcher,
        loadComponent: () => import('./wiki/wiki-workspace').then((module) => module.WikiWorkspace),
      },
    ],
  },
  { path: 'settings', redirectTo: 'tasks/settings' },
  { path: '**', redirectTo: 'tasks' },
];
