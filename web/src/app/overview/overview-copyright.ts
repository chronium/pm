import { Component, input } from '@angular/core';

@Component({
  selector: 'pm-overview-copyright',
  templateUrl: './overview-copyright.html',
  styleUrl: './overview-copyright.css',
})
export class OverviewCopyright {
  readonly notice = input.required<string>();
}
