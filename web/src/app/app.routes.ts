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
            path: 'dialog/new',
            loadComponent: () =>
              import('./tasks/task-workspace/task-dialog-host').then(
                (module) => module.TaskDialogHost,
              ),
            data: { mode: 'create' },
            canDeactivate: [canLeaveDirtyRoute],
          },
          {
            path: 'dialog/:taskId',
            loadComponent: () =>
              import('./tasks/task-workspace/task-dialog-host').then(
                (module) => module.TaskDialogHost,
              ),
            data: { mode: 'detail' },
            canDeactivate: [canLeaveDirtyRoute],
          },
        ],
      },
      {
        path: 'new',
        loadComponent: () =>
          import('./tasks/task-workspace/task-page-host').then((module) => module.TaskPageHost),
        data: { mode: 'create' },
        canDeactivate: [canLeaveDirtyRoute],
      },
      {
        path: ':taskId',
        loadComponent: () =>
          import('./tasks/task-workspace/task-page-host').then((module) => module.TaskPageHost),
        data: { mode: 'detail' },
        canDeactivate: [canLeaveDirtyRoute],
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

export const staticRoutes: Routes = [
  { path: '', pathMatch: 'full', redirectTo: 'tasks' },
  {
    path: 'tasks',
    component: TasksShell,
    data: { shell: AppShell.Tasks },
    children: [
      { path: 'settings', redirectTo: '' },
      {
        path: '',
        component: TasksBoard,
        children: [
          { path: 'dialog/new', redirectTo: '' },
          {
            path: 'dialog/:taskId',
            loadComponent: () =>
              import('./tasks/task-workspace/task-dialog-host').then(
                (module) => module.TaskDialogHost,
              ),
            data: { mode: 'detail' },
          },
        ],
      },
      { path: 'new', redirectTo: '' },
      {
        path: ':taskId',
        loadComponent: () =>
          import('./tasks/task-workspace/task-page-host').then((module) => module.TaskPageHost),
        data: { mode: 'detail' },
      },
    ],
  },
  {
    path: 'wiki',
    component: WikiShell,
    data: { shell: AppShell.Wiki },
    children: [
      { path: '', pathMatch: 'full', component: WikiIndex },
      { path: 'new', redirectTo: '' },
      { matcher: wikiEditMatcher, redirectTo: ({ params }) => params['wikiPath'] ?? '' },
      { matcher: wikiMetaMatcher, redirectTo: ({ params }) => params['wikiPath'] ?? '' },
      {
        matcher: wikiPathMatcher,
        loadComponent: () => import('./wiki/wiki-workspace').then((module) => module.WikiWorkspace),
      },
    ],
  },
  { path: 'settings', redirectTo: 'tasks' },
  { path: '**', redirectTo: 'tasks' },
];
