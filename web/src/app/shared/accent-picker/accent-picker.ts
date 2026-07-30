import { ChangeDetectionStrategy, Component, ElementRef, inject, viewChild } from '@angular/core';

import { ACCENT_OPTIONS, AccentService, type AccentPreference } from '../../core/accent.service';

@Component({
  selector: 'pm-accent-picker',
  templateUrl: './accent-picker.html',
  styleUrl: './accent-picker.css',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class AccentPicker {
  protected readonly accent = inject(AccentService);
  protected readonly options = ACCENT_OPTIONS;
  private readonly menu = viewChild<ElementRef<HTMLElement>>('menu');

  protected select(preference: AccentPreference): void {
    this.accent.select(preference);
    this.menu()?.nativeElement.hidePopover?.();
  }
}
