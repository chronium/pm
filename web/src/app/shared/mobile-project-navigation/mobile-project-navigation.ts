import { Component, inject } from '@angular/core';

import { ProjectSwitcher } from '../../project-switcher/project-switcher';
import { StaticModeService } from '../../static/static-mode.service';
import { StaticProjectSwitcher } from '../../static/static-project-switcher';

@Component({
  selector: 'pm-mobile-project-navigation',
  imports: [ProjectSwitcher, StaticProjectSwitcher],
  templateUrl: './mobile-project-navigation.html',
  styleUrl: './mobile-project-navigation.css',
})
export class MobileProjectNavigation {
  protected readonly staticMode = inject(StaticModeService);
}
