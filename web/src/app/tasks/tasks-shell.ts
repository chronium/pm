import { Component, inject } from '@angular/core';
import { RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';

import { LayoutService } from '../core/layout.service';
import { TaskNavigationService } from './task-navigation.service';

@Component({
  selector: 'pm-tasks-shell',
  imports: [RouterLink, RouterLinkActive, RouterOutlet],
  templateUrl: './tasks-shell.html',
  styleUrl: '../shell.css',
})
export class TasksShell {
  protected readonly layout = inject(LayoutService);
  protected readonly taskNavigation = inject(TaskNavigationService);
}
