import { Component, input } from '@angular/core';

@Component({
  selector: 'pm-overview-section',
  templateUrl: './overview-section.html',
  styleUrl: './overview-section.css',
})
export class OverviewSection {
  readonly headingId = input.required<string>();
  readonly title = input.required<string>();
}
