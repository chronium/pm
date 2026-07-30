import { DOCUMENT } from '@angular/common';
import { Injectable, computed, inject, signal } from '@angular/core';

export type AccentPreference = 'teal' | 'blue' | 'purple' | 'rose' | 'amber' | 'neutral';

export interface AccentOption {
  id: AccentPreference;
  label: string;
}

export const ACCENT_OPTIONS: readonly AccentOption[] = [
  { id: 'teal', label: 'Teal' },
  { id: 'blue', label: 'Blue' },
  { id: 'purple', label: 'Purple' },
  { id: 'rose', label: 'Rose' },
  { id: 'amber', label: 'Amber' },
  { id: 'neutral', label: 'Neutral' },
];

export function normalizeAccentPreference(preference: string | null | undefined): AccentPreference {
  return ACCENT_OPTIONS.some((option) => option.id === preference)
    ? (preference as AccentPreference)
    : 'teal';
}

@Injectable({ providedIn: 'root' })
export class AccentService {
  private readonly document = inject(DOCUMENT);
  private readonly preferenceState = signal<AccentPreference>('teal');

  readonly preference = this.preferenceState.asReadonly();
  readonly label = computed(
    () => ACCENT_OPTIONS.find((option) => option.id === this.preference())?.label ?? 'Teal',
  );

  constructor() {
    this.apply(this.preference());
  }

  applyProjectPreference(preference: string | null | undefined): void {
    const normalized = normalizeAccentPreference(preference);
    this.preferenceState.set(normalized);
    this.apply(normalized);
  }

  private apply(preference: AccentPreference): void {
    this.document.documentElement.dataset['accent'] = preference;
  }
}
