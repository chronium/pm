import { Component, ElementRef, HostListener, inject, viewChild } from '@angular/core';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { cssMenu, cssMoon, cssScreen, cssSun } from '@ng-icons/css.gg';
import { RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';

import { LayoutService } from './core/layout.service';
import { ThemeService } from './core/theme.service';

@Component({
  selector: 'pm-root',
  imports: [NgIcon, RouterLink, RouterLinkActive, RouterOutlet],
  providers: [provideIcons({ cssMenu, cssScreen, cssSun, cssMoon })],
  templateUrl: './app.html',
  styleUrl: './app.css',
})
export class App {
  protected readonly layout = inject(LayoutService);
  protected readonly theme = inject(ThemeService);
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
