import { Component, input } from '@angular/core';

export interface OverviewIssue {
  code: string;
  message: string;
  path: string;
}

@Component({
  selector: 'pm-overview-invalid-state',
  templateUrl: './overview-invalid-state.html',
  styleUrl: './overview-invalid-state.css',
})
export class OverviewInvalidState {
  readonly issues = input.required<readonly OverviewIssue[]>();
}
