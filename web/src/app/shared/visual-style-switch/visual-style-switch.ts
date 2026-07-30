import { ChangeDetectionStrategy, Component, inject } from '@angular/core';

import { VisualStyleService, type VisualStyle } from '../../core/visual-style.service';

@Component({
  selector: 'pm-visual-style-switch',
  templateUrl: './visual-style-switch.html',
  styleUrl: './visual-style-switch.css',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class VisualStyleSwitch {
  protected readonly visualStyle = inject(VisualStyleService);

  protected select(style: VisualStyle): void {
    this.visualStyle.select(style);
  }
}
