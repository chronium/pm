import { DOCUMENT } from '@angular/common';
import { Injectable, inject, signal } from '@angular/core';

export type VisualTuningKey = 'satinOpacity' | 'satinNoise' | 'frostedFill' | 'frostedNoise';

export interface VisualTuningValues {
  satinOpacity: number;
  satinNoise: number;
  frostedFill: number;
  frostedNoise: number;
}

const STORAGE_KEY = 'pm.visual-tuning';
const defaults: VisualTuningValues = {
  satinOpacity: 100,
  satinNoise: 26,
  frostedFill: 6,
  frostedNoise: 2,
};

@Injectable({ providedIn: 'root' })
export class VisualTuningService {
  private readonly document = inject(DOCUMENT);
  private readonly valuesState = signal<VisualTuningValues>(this.read());

  readonly values = this.valuesState.asReadonly();

  constructor() {
    this.apply(this.values());
  }

  update(key: VisualTuningKey, value: number): void {
    const next = { ...this.values(), [key]: this.clamp(value) };
    this.valuesState.set(next);
    this.apply(next);
    this.persist(next);
  }

  reset(): void {
    const next = { ...defaults };
    this.valuesState.set(next);
    this.apply(next);
    this.persist(next);
  }

  private apply(values: VisualTuningValues): void {
    const style = this.document.documentElement.style;
    style.setProperty('--pm-satin-background-opacity', `${values.satinOpacity}%`);
    style.setProperty('--pm-satin-grain-opacity', String(values.satinNoise / 100));
    style.setProperty('--pm-frosted-surface-fill-opacity', `${values.frostedFill}%`);
    style.setProperty('--pm-frosted-surface-noise-opacity', String(values.frostedNoise / 100));
  }

  private read(): VisualTuningValues {
    try {
      const stored = this.document.defaultView?.sessionStorage.getItem(STORAGE_KEY);
      if (!stored) return { ...defaults };
      const parsed = JSON.parse(stored) as Partial<Record<VisualTuningKey, unknown>>;
      return {
        satinOpacity: this.readNumber(parsed.satinOpacity, defaults.satinOpacity),
        satinNoise: this.readNumber(parsed.satinNoise, defaults.satinNoise),
        frostedFill: this.readNumber(parsed.frostedFill, defaults.frostedFill),
        frostedNoise: this.readNumber(parsed.frostedNoise, defaults.frostedNoise),
      };
    } catch {
      return { ...defaults };
    }
  }

  private persist(values: VisualTuningValues): void {
    try {
      this.document.defaultView?.sessionStorage.setItem(STORAGE_KEY, JSON.stringify(values));
    } catch {
      // Live tuning remains available when storage is unavailable.
    }
  }

  private readNumber(value: unknown, fallback: number): number {
    return typeof value === 'number' && Number.isFinite(value) ? this.clamp(value) : fallback;
  }

  private clamp(value: number): number {
    return Math.min(100, Math.max(0, value));
  }
}
