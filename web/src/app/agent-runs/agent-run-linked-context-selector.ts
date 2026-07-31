import { Component, computed, input, output } from '@angular/core';

import type { LinkedProjectMember } from './agent-runs-api.service';

export type LinkedContextRequirement = 'required' | 'optional';
export type LinkedContextSelections = Readonly<Record<string, LinkedContextRequirement>>;

@Component({
  selector: 'pm-agent-run-linked-context-selector',
  templateUrl: './agent-run-linked-context-selector.html',
  styleUrl: './agent-run-linked-context-selector.css',
})
export class AgentRunLinkedContextSelector {
  readonly projects = input.required<readonly LinkedProjectMember[]>();
  readonly selections = input.required<LinkedContextSelections>();
  readonly loading = input(false);
  readonly error = input<string | null>(null);
  readonly disabled = input(false);
  readonly selectionChange = output<LinkedContextSelections>();

  protected readonly selectedCount = computed(() => Object.keys(this.selections()).length);

  protected selected(projectId: string): boolean {
    return Object.hasOwn(this.selections(), projectId);
  }

  protected toggle(projectId: string, event: Event): void {
    const checked = (event.target as HTMLInputElement).checked;
    const next = { ...this.selections() };
    if (checked) next[projectId] = 'required';
    else delete next[projectId];
    this.selectionChange.emit(next);
  }

  protected changeRequirement(projectId: string, event: Event): void {
    const requirement = (event.target as HTMLSelectElement).value as LinkedContextRequirement;
    this.selectionChange.emit({ ...this.selections(), [projectId]: requirement });
  }
}
