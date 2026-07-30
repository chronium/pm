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

const ACCENT_KEY = 'pm.accent';

@Injectable({ providedIn: 'root' })
export class AccentService {
  private readonly document = inject(DOCUMENT);
  private readonly preferenceState = signal<AccentPreference>(this.readPreference());

  readonly preference = this.preferenceState.asReadonly();
  readonly label = computed(
    () => ACCENT_OPTIONS.find((option) => option.id === this.preference())?.label ?? 'Teal',
  );

  constructor() {
    this.apply(this.preference());
  }

  select(preference: AccentPreference): void {
    this.preferenceState.set(preference);
    this.apply(preference);
    try {
      this.document.defaultView?.sessionStorage.setItem(ACCENT_KEY, preference);
    } catch {
      // The in-memory selection still works when storage is unavailable.
    }
  }

  private readPreference(): AccentPreference {
    try {
      const value = this.document.defaultView?.sessionStorage.getItem(ACCENT_KEY);
      return ACCENT_OPTIONS.some((option) => option.id === value)
        ? (value as AccentPreference)
        : 'teal';
    } catch {
      return 'teal';
    }
  }

  private apply(preference: AccentPreference): void {
    this.document.documentElement.dataset['accent'] = preference;
  }
}
