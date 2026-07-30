import { DOCUMENT } from '@angular/common';
import { Injectable, inject, signal } from '@angular/core';

export type VisualStyle = 'current' | 'exploration';

const VISUAL_STYLE_KEY = 'pm.visual-style';

@Injectable({ providedIn: 'root' })
export class VisualStyleService {
  private readonly document = inject(DOCUMENT);
  private readonly styleState = signal<VisualStyle>(this.readStyle());

  readonly style = this.styleState.asReadonly();

  constructor() {
    this.apply(this.style());
  }

  select(style: VisualStyle): void {
    this.styleState.set(style);
    this.apply(style);
    try {
      this.document.defaultView?.sessionStorage.setItem(VISUAL_STYLE_KEY, style);
    } catch {
      // The in-memory selection still works when storage is unavailable.
    }
  }

  private readStyle(): VisualStyle {
    try {
      return this.document.defaultView?.sessionStorage.getItem(VISUAL_STYLE_KEY) === 'exploration'
        ? 'exploration'
        : 'current';
    } catch {
      return 'current';
    }
  }

  private apply(style: VisualStyle): void {
    this.document.documentElement.dataset['visualStyle'] = style;
  }
}
