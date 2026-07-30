import { DatePipe } from '@angular/common';
import { Component, ElementRef, HostListener, inject, viewChild } from '@angular/core';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { cssMenu, cssMoon, cssScreen, cssSun } from '@ng-icons/css.gg';
import { RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';

import { LayoutService } from './core/layout.service';
import { ThemeService } from './core/theme.service';
import { ProjectApiService } from './api/project-api.service';
import { TaskNavigationService } from './tasks/task-navigation.service';
import { AppShell } from './app.routes';
import { TaskSearch } from './tasks/task-search/task-search';
import { WikiSearch } from './wiki/wiki-search';
import { StaticModeService } from './static/static-mode.service';
import { VisualStyleSwitch } from './shared/visual-style-switch/visual-style-switch';
import { AccentPicker } from './shared/accent-picker/accent-picker';

@Component({
  selector: 'pm-root',
  imports: [
    AccentPicker,
    DatePipe,
    NgIcon,
    RouterLink,
    RouterLinkActive,
    RouterOutlet,
    TaskSearch,
    VisualStyleSwitch,
    WikiSearch,
  ],
  providers: [provideIcons({ cssMenu, cssScreen, cssSun, cssMoon })],
  templateUrl: './app.html',
  styleUrl: './app.css',
})
export class App {
  protected readonly AppShell = AppShell;
  protected readonly layout = inject(LayoutService);
  protected readonly theme = inject(ThemeService);
  protected readonly projectApi = inject(ProjectApiService);
  protected readonly taskNavigation = inject(TaskNavigationService);
  protected readonly staticMode = inject(StaticModeService);
  private readonly menuButton = viewChild<ElementRef<HTMLButtonElement>>('menuButton');

  protected toggleNavigation(): void {
    const trigger = this.menuButton()?.nativeElement;
    if (trigger) {
      this.layout.toggleMobileSidebar(trigger);
    }
  }

  @HostListener('document:keydown.escape')
  protected closeNavigation(): void {
    this.layout.closeMobileSidebar();
  }
}
