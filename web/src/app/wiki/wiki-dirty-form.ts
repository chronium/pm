import { Directive, HostListener, signal } from '@angular/core';
import type { DirtyRoute } from '../core/dirty-route';

@Directive()
export abstract class WikiDirtyForm implements DirtyRoute {
  protected readonly confirmDiscardOpen = signal(false);
  private leaveResolver: ((answer: boolean) => void) | null = null;
  protected allowLeave = false;

  protected abstract dirty(): boolean;
  protected abstract busy(): boolean;

  canDeactivate(): boolean | Promise<boolean> {
    if (this.allowLeave || !this.dirty()) return true;
    if (this.busy()) return false;
    this.confirmDiscardOpen.set(true);
    return new Promise((resolve) => this.leaveResolver = resolve);
  }

  @HostListener('window:beforeunload', ['$event'])
  beforeUnload(event: BeforeUnloadEvent): void { if (this.dirty() && !this.allowLeave) event.preventDefault(); }

  protected discardChanges(): void { this.confirmDiscardOpen.set(false); this.allowLeave = true; this.leaveResolver?.(true); this.leaveResolver = null; }
  protected keepEditing(): void { this.confirmDiscardOpen.set(false); this.leaveResolver?.(false); this.leaveResolver = null; }
}
