import { DOCUMENT } from '@angular/common';
import { Injectable, computed, inject, signal } from '@angular/core';

export type ThemePreference = 'system' | 'light' | 'dark';

const THEME_KEY = 'pm.theme';
const THEMES: readonly ThemePreference[] = ['system', 'light', 'dark'];

@Injectable({ providedIn: 'root' })
export class ThemeService {
  private readonly document = inject(DOCUMENT);
  private readonly preferenceState = signal<ThemePreference>(this.readPreference());

  readonly preference = this.preferenceState.asReadonly();
  readonly iconName = computed(
    () => ({ system: 'cssScreen', light: 'cssSun', dark: 'cssMoon' })[this.preference()],
  );
  readonly actionLabel = computed(() => {
    const current = this.title(this.preference());
    const next = this.title(this.nextPreference());
    return `Theme: ${current}. Switch to ${next}`;
  });

  constructor() {
    this.apply(this.preference());
  }

  cycle(): void {
    const next = this.nextPreference();
    this.preferenceState.set(next);
    this.apply(next);
    try {
      this.document.defaultView?.localStorage.setItem(THEME_KEY, next);
    } catch {
      // The in-memory preference still works when storage is unavailable.
    }
  }

  private nextPreference(): ThemePreference {
    const index = THEMES.indexOf(this.preference());
    return THEMES[(index + 1) % THEMES.length];
  }

  private readPreference(): ThemePreference {
    try {
      const value = this.document.defaultView?.localStorage.getItem(THEME_KEY);
      return value === 'light' || value === 'dark' || value === 'system' ? value : 'system';
    } catch {
      return 'system';
    }
  }

  private apply(preference: ThemePreference): void {
    const root = this.document.documentElement;
    root.dataset['themePreference'] = preference;
    if (preference === 'system') {
      root.removeAttribute('data-theme');
    } else {
      root.dataset['theme'] = preference;
    }
  }

  private title(preference: ThemePreference): string {
    return preference[0].toUpperCase() + preference.slice(1);
  }
}
