import { Routes } from '@angular/router';

import { TasksIndex } from './tasks/tasks-index';
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
    children: [{ path: '', component: TasksIndex }],
  },
  {
    path: 'wiki',
    component: WikiShell,
    data: { shell: AppShell.Wiki },
    children: [{ path: '', component: WikiIndex }],
  },
  { path: '**', redirectTo: 'tasks' },
];
