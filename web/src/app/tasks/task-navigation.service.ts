import { Injectable, signal } from '@angular/core';

@Injectable({ providedIn: 'root' })
export class TaskNavigationService {
  readonly remainingCount = signal<number | null>(null);
  readonly refreshRequest = signal(0);
  private origin: HTMLElement | null = null;

  setRemainingCount(count: number): void {
    this.remainingCount.set(count);
  }

  requestNavigationRefresh(): void {
    this.refreshRequest.update((value) => value + 1);
  }

  captureOrigin(element: EventTarget | null): void {
    this.origin = element instanceof HTMLElement ? element : null;
  }

  restoreFocus(): void {
    const origin = this.origin;
    this.origin = null;
    setTimeout(() => origin?.isConnected && origin.focus());
  }
}
