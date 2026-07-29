import { Component, computed, effect, input, output, signal, viewChild } from '@angular/core';
import { CdkVirtualScrollViewport, ScrollingModule } from '@angular/cdk/scrolling';

import type { AgentRunConnectivity, AgentRunLogEntry } from './agent-run-events';

@Component({
  selector: 'pm-agent-run-output',
  imports: [ScrollingModule],
  templateUrl: './agent-run-output.html',
  styleUrl: './agent-run-output.css',
})
export class AgentRunOutput {
  readonly entries = input.required<AgentRunLogEntry[]>();
  readonly connectivity = input.required<AgentRunConnectivity>();
  readonly paused = input(false);
  readonly droppedEntries = input(0);
  readonly downloading = input(false);
  readonly pauseChange = output<boolean>();
  readonly downloadRequested = output<void>();
  readonly reconnectRequested = output<void>();

  private readonly viewport = viewChild(CdkVirtualScrollViewport);
  protected readonly query = signal('');
  protected readonly excludedSources = signal<ReadonlySet<string>>(new Set());
  protected readonly follow = signal(true);
  protected readonly copyStatus = signal<string | null>(null);
  protected readonly sources = computed(() =>
    [...new Set(this.entries().map((entry) => entry.source))].sort(),
  );
  protected readonly filteredEntries = computed(() => {
    const query = this.query().trim().toLocaleLowerCase();
    const excluded = this.excludedSources();
    return this.entries().filter(
      (entry) =>
        !excluded.has(entry.source) &&
        (!query ||
          `${entry.sequence} ${entry.timestamp} ${entry.source} ${entry.type} ${entry.message}`
            .toLocaleLowerCase()
            .includes(query)),
    );
  });

  constructor() {
    effect(() => {
      const length = this.filteredEntries().length;
      if (!length || !this.follow()) return;
      queueMicrotask(() => this.viewport()?.scrollToIndex(length - 1, 'auto'));
    });
  }

  protected updateQuery(event: Event): void {
    this.query.set((event.target as HTMLInputElement).value);
  }

  protected toggleSource(source: string, event: Event): void {
    const checked = (event.target as HTMLInputElement).checked;
    this.excludedSources.update((excluded) => {
      const next = new Set(excluded);
      if (checked) next.delete(source);
      else next.add(source);
      return next;
    });
  }

  protected sourceIncluded(source: string): boolean {
    return !this.excludedSources().has(source);
  }

  protected setFollow(event: Event): void {
    this.follow.set((event.target as HTMLInputElement).checked);
  }

  protected setPaused(event: Event): void {
    this.pauseChange.emit((event.target as HTMLInputElement).checked);
  }

  revealLatest(): void {
    const viewport = this.viewport();
    viewport?.checkViewportSize();
    if (viewport && this.follow() && this.filteredEntries().length)
      viewport.scrollToIndex(this.filteredEntries().length - 1, 'auto');
  }

  protected async copyVisible(): Promise<void> {
    const text = this.filteredEntries()
      .map(
        (entry) =>
          `${entry.continuation ? ' '.repeat(8) : '#' + entry.sequence} ` +
          `${this.time(entry.timestamp)} ${entry.source.padEnd(10)} ${entry.message}`,
      )
      .join('\n');
    try {
      await navigator.clipboard.writeText(text);
      this.copyStatus.set(`${this.filteredEntries().length} visible log lines copied.`);
    } catch {
      this.copyStatus.set('Visible log lines could not be copied.');
    }
  }

  protected time(timestamp: string): string {
    const date = new Date(timestamp);
    return Number.isNaN(date.valueOf())
      ? timestamp
      : date.toLocaleTimeString(undefined, {
          hour12: false,
          hour: '2-digit',
          minute: '2-digit',
          second: '2-digit',
        });
  }

  protected trackEntry(_index: number, entry: AgentRunLogEntry): string {
    return entry.key;
  }
}
