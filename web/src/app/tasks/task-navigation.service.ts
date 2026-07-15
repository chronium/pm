import { Injectable, signal } from '@angular/core';

@Injectable({ providedIn: 'root' })
export class TaskNavigationService {
  readonly remainingCount = signal<number | null>(null);

  setRemainingCount(count: number): void {
    this.remainingCount.set(count);
  }
}
