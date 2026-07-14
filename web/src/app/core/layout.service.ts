import { Injectable, inject, signal } from '@angular/core';
import { ActivatedRoute, NavigationEnd, Router } from '@angular/router';
import { filter } from 'rxjs';

import { AppShell } from '../app.routes';

@Injectable({ providedIn: 'root' })
export class LayoutService {
  private readonly router = inject(Router);
  private readonly activatedRoute = inject(ActivatedRoute);
  private readonly activeShellState = signal<AppShell | null>(null);
  private readonly mobileSidebarState = signal(false);
  private mobileTrigger: HTMLElement | null = null;

  readonly activeShell = this.activeShellState.asReadonly();
  readonly mobileSidebarOpen = this.mobileSidebarState.asReadonly();

  constructor() {
    this.updateActiveShell();
    this.router.events.pipe(filter((event) => event instanceof NavigationEnd)).subscribe(() => {
      this.updateActiveShell();
      this.closeMobileSidebar();
    });
  }

  openMobileSidebar(trigger: HTMLElement): void {
    this.mobileTrigger = trigger;
    this.mobileSidebarState.set(true);
  }

  closeMobileSidebar(restoreFocus = true): void {
    if (!this.mobileSidebarState()) {
      return;
    }

    this.mobileSidebarState.set(false);
    const trigger = this.mobileTrigger;
    this.mobileTrigger = null;
    if (restoreFocus && trigger?.isConnected) {
      queueMicrotask(() => trigger.focus());
    }
  }

  toggleMobileSidebar(trigger: HTMLElement): void {
    if (this.mobileSidebarState()) {
      this.closeMobileSidebar();
    } else {
      this.openMobileSidebar(trigger);
    }
  }

  private updateActiveShell(): void {
    let route = this.activatedRoute;
    while (route.firstChild) {
      route = route.firstChild;
    }

    let snapshot = route.snapshot;
    while (snapshot && snapshot.data['shell'] === undefined) {
      snapshot = snapshot.parent!;
    }
    this.activeShellState.set((snapshot?.data['shell'] as AppShell | undefined) ?? null);
  }
}
