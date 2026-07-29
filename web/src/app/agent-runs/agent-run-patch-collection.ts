import {
  Component,
  effect,
  ElementRef,
  inject,
  input,
  output,
  signal,
  viewChild,
} from '@angular/core';
import { firstValueFrom } from 'rxjs';

import {
  AgentRunsApiService,
  type AgentRunPatchCollectionResult,
  type AgentRunPatchPreflightResult,
} from './agent-runs-api.service';

@Component({
  selector: 'pm-agent-run-patch-collection',
  templateUrl: './agent-run-patch-collection.html',
  styleUrl: './agent-run-patch-collection.css',
})
export class AgentRunPatchCollection {
  private readonly api = inject(AgentRunsApiService);
  private readonly dialog = viewChild.required<ElementRef<HTMLDialogElement>>('dialog');
  private generation = 0;

  readonly open = input(false);
  readonly runId = input.required<string>();
  readonly openChange = output<boolean>();
  readonly collected = output<AgentRunPatchCollectionResult>();

  protected readonly loading = signal(false);
  protected readonly applying = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly preflight = signal<AgentRunPatchPreflightResult | null>(null);
  protected readonly etag = signal('');

  constructor() {
    effect(() => {
      const dialog = this.dialog().nativeElement;
      if (this.open() && !dialog.open) {
        if (typeof dialog.showModal === 'function') dialog.showModal();
        else dialog.setAttribute('open', '');
      } else if (!this.open() && dialog.open) {
        if (typeof dialog.close === 'function') dialog.close();
        else dialog.removeAttribute('open');
      }
    });
    effect(() => {
      const isOpen = this.open();
      const runId = this.runId();
      if (isOpen) void this.load(runId);
      else this.generation += 1;
    });
  }

  protected async apply(): Promise<void> {
    const preflight = this.preflight();
    if (!preflight?.ready || !this.etag() || this.applying()) return;
    this.applying.set(true);
    this.error.set(null);
    try {
      const response = await firstValueFrom(
        this.api.collectPatch(this.runId(), preflight.artifactSha256, this.etag()),
      );
      if (!response.body) throw new Error('Patch collection returned an empty response.');
      this.collected.emit(response.body);
      this.close();
    } catch (error) {
      this.error.set(this.api.error(error, 'The patch could not be collected.').message);
    } finally {
      this.applying.set(false);
    }
  }

  protected retry(): void {
    void this.load(this.runId());
  }

  protected close(): void {
    if (this.applying()) return;
    this.openChange.emit(false);
  }

  protected handleNativeCancel(event: Event): void {
    event.preventDefault();
    this.close();
  }

  protected short(value: string): string {
    return value.length <= 12 ? value : value.slice(0, 12);
  }

  private async load(runId: string): Promise<void> {
    const generation = ++this.generation;
    this.loading.set(true);
    this.error.set(null);
    this.preflight.set(null);
    this.etag.set('');
    try {
      const response = await firstValueFrom(this.api.preflightPatchCollection(runId));
      if (generation !== this.generation) return;
      if (!response.body) throw new Error('Patch preflight returned an empty response.');
      this.preflight.set(response.body);
      this.etag.set(this.api.etag(response));
    } catch (error) {
      if (generation === this.generation)
        this.error.set(this.api.error(error, 'Patch preflight could not be completed.').message);
    } finally {
      if (generation === this.generation) this.loading.set(false);
    }
  }
}
