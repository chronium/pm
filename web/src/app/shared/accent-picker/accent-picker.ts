import { ChangeDetectionStrategy, Component, input, output } from '@angular/core';

import { ACCENT_OPTIONS, type AccentPreference } from '../../core/accent.service';

@Component({
  selector: 'pm-accent-picker',
  templateUrl: './accent-picker.html',
  styleUrl: './accent-picker.css',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class AccentPicker {
  readonly preference = input.required<AccentPreference>();
  readonly disabled = input(false);
  readonly selection = output<AccentPreference>();
  protected readonly options = ACCENT_OPTIONS;

  protected select(preference: AccentPreference): void {
    if (!this.disabled() && preference !== this.preference()) this.selection.emit(preference);
  }
}
