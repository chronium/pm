import { Component, computed, input } from '@angular/core';
import { RouterLink } from '@angular/router';

import { PmButton } from '../ui/button/button.directive';

@Component({
  selector: 'pm-overview-hero',
  imports: [PmButton, RouterLink],
  templateUrl: './overview-hero.html',
  styleUrl: './overview-hero.css',
})
export class OverviewHero {
  readonly projectName = input.required<string>();
  readonly title = input.required<string>();
  readonly description = input<string | null>(null);
  readonly tasksUrl = input.required<string>();
  readonly wikiUrl = input.required<string>();

  protected readonly projectContext = computed(() => {
    const projectName = this.projectName().trim();
    return projectName && projectName !== this.title().trim() ? projectName : null;
  });
  protected readonly resolvedDescription = computed(() => this.description()?.trim() || null);
}
