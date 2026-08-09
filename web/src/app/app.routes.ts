import { Routes } from '@angular/router';

import { canLeaveDirtyRoute } from './core/dirty-route';
import { TasksBoard } from './tasks/tasks-board';
import { TasksShell } from './tasks/tasks-shell';
import { WikiIndex } from './wiki/wiki-index';
import { WikiShell } from './wiki/wiki-shell';
import { wikiEditMatcher, wikiMetaMatcher, wikiPathMatcher } from './wiki/wiki.routes';

export enum AppShell {
  Overview = 'overview',
  Tasks = 'tasks',
  Wiki = 'wiki',
  Components = 'components',
}

const linkedTaskRoutes: Routes = [
  {
    path: 'settings',
    loadComponent: () => import('./settings/settings-page').then((module) => module.SettingsPage),
    canDeactivate: [canLeaveDirtyRoute],
  },
  {
    path: '',
    component: TasksBoard,
    children: [
      {
        path: 'dialog/new',
        loadComponent: () =>
          import('./tasks/task-workspace/task-dialog-host').then((module) => module.TaskDialogHost),
        data: { mode: 'create' },
        canDeactivate: [canLeaveDirtyRoute],
      },
      {
        path: 'dialog/:taskId',
        loadComponent: () =>
          import('./tasks/task-workspace/task-dialog-host').then((module) => module.TaskDialogHost),
        data: { mode: 'detail' },
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
  },
];

const linkedWikiRoutes: Routes = [
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
];

export const routes: Routes = [
  { path: '', pathMatch: 'full', redirectTo: 'tasks' },
  {
    path: 'overview',
    loadComponent: () => import('./overview/overview-page').then((module) => module.OverviewPage),
    data: { shell: AppShell.Overview },
  },
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
        path: 'runs/:runId',
        loadComponent: () =>
          import('./agent-runs/agent-run-workspace').then((module) => module.AgentRunWorkspace),
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
  {
    path: 'projects/:projectId/overview',
    loadComponent: () => import('./overview/overview-page').then((module) => module.OverviewPage),
    data: { shell: AppShell.Overview, linkedProject: true },
  },
  {
    path: 'projects/:projectId/tasks',
    component: TasksShell,
    data: { shell: AppShell.Tasks, linkedProject: true },
    children: linkedTaskRoutes,
  },
  {
    path: 'projects/:projectId/wiki',
    component: WikiShell,
    data: { shell: AppShell.Wiki, linkedProject: true },
    children: linkedWikiRoutes,
  },
  {
    path: 'components',
    loadComponent: () =>
      import('./component-gallery/component-gallery-shell').then(
        (module) => module.ComponentGalleryShell,
      ),
    data: { shell: AppShell.Components },
    children: [
      { path: '', pathMatch: 'full', redirectTo: 'buttons' },
      {
        path: 'buttons',
        loadComponent: () =>
          import('./component-gallery/button-gallery').then((module) => module.ButtonGallery),
      },
      {
        path: 'badges',
        loadComponent: () =>
          import('./component-gallery/badge-gallery').then((module) => module.BadgeGallery),
      },
      {
        path: 'forms',
        loadComponent: () =>
          import('./component-gallery/form-gallery').then((module) => module.FormGallery),
      },
      {
        path: 'states',
        loadComponent: () =>
          import('./component-gallery/state-gallery').then((module) => module.StateGallery),
      },
      {
        path: 'dialogs',
        loadComponent: () =>
          import('./component-gallery/dialog-gallery').then((module) => module.DialogGallery),
      },
      {
        path: 'markdown',
        loadComponent: () =>
          import('./component-gallery/markdown-gallery').then((module) => module.MarkdownGallery),
      },
      {
        path: 'search',
        loadComponent: () =>
          import('./component-gallery/search-gallery').then((module) => module.SearchGallery),
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
      {
        path: 'settings',
        loadComponent: () =>
          import('./settings/static-activation-page').then((module) => module.StaticActivationPage),
      },
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
      { path: 'runs/:runId', redirectTo: '' },
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
  {
    path: 'components',
    loadComponent: () =>
      import('./component-gallery/component-gallery-shell').then(
        (module) => module.ComponentGalleryShell,
      ),
    data: { shell: AppShell.Components },
    children: [
      { path: '', pathMatch: 'full', redirectTo: 'buttons' },
      {
        path: 'buttons',
        loadComponent: () =>
          import('./component-gallery/button-gallery').then((module) => module.ButtonGallery),
      },
      {
        path: 'badges',
        loadComponent: () =>
          import('./component-gallery/badge-gallery').then((module) => module.BadgeGallery),
      },
      {
        path: 'forms',
        loadComponent: () =>
          import('./component-gallery/form-gallery').then((module) => module.FormGallery),
      },
      {
        path: 'states',
        loadComponent: () =>
          import('./component-gallery/state-gallery').then((module) => module.StateGallery),
      },
      {
        path: 'dialogs',
        loadComponent: () =>
          import('./component-gallery/dialog-gallery').then((module) => module.DialogGallery),
      },
      {
        path: 'markdown',
        loadComponent: () =>
          import('./component-gallery/markdown-gallery').then((module) => module.MarkdownGallery),
      },
      {
        path: 'search',
        loadComponent: () =>
          import('./component-gallery/search-gallery').then((module) => module.SearchGallery),
      },
    ],
  },
  { path: 'settings', redirectTo: 'tasks' },
  { path: '**', redirectTo: 'tasks' },
];
