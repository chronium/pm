import { Component, input, output } from '@angular/core';

import type { ValidationResponse } from './settings-api.service';

@Component({
  selector: 'pm-project-health',
  templateUrl: './project-health.html',
  styleUrl: './project-health.css',
})
export class ProjectHealth {
  readonly validation = input<ValidationResponse>();
  readonly loading = input(false);
  readonly refreshing = input(false);
  readonly error = input<string | null>(null);
  readonly retry = output<void>();
}
