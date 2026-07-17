import { Component, inject } from '@angular/core';
import { RouterOutlet } from '@angular/router';

import { LayoutService } from '../core/layout.service';
import { TaskSidebar } from './task-sidebar/task-sidebar';
import { TaskSidebarStore } from './task-sidebar/task-sidebar.store';
import { PollingCoordinator } from '../core/polling-coordinator';

@Component({
  selector: 'pm-tasks-shell',
  imports: [RouterOutlet, TaskSidebar],
  providers: [TaskSidebarStore, PollingCoordinator],
  templateUrl: './tasks-shell.html',
  styleUrl: '../shell.css',
})
export class TasksShell {
  protected readonly layout = inject(LayoutService);
}
