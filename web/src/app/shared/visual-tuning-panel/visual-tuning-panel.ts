import { ChangeDetectionStrategy, Component, inject } from '@angular/core';

import { VisualTuningService, type VisualTuningKey } from '../../core/visual-tuning.service';
import { VisualStyleService } from '../../core/visual-style.service';

@Component({
  selector: 'pm-visual-tuning-panel',
  templateUrl: './visual-tuning-panel.html',
  styleUrl: './visual-tuning-panel.css',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class VisualTuningPanel {
  protected readonly visualStyle = inject(VisualStyleService);
  protected readonly tuning = inject(VisualTuningService);

  protected update(key: VisualTuningKey, event: Event): void {
    this.tuning.update(key, Number((event.target as HTMLInputElement).value));
  }
}
