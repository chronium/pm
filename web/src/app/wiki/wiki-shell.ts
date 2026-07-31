import { Component, inject } from '@angular/core';
import { RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';

import { LayoutService } from '../core/layout.service';
import { WikiStore } from './wiki.store';
import { PollingCoordinator } from '../core/polling-coordinator';
import { WikiTree } from './wiki-tree';
import { ProjectContextService } from '../core/project-context.service';

@Component({
  selector: 'pm-wiki-shell',
  imports: [RouterLink, RouterLinkActive, RouterOutlet, WikiTree],
  templateUrl: './wiki-shell.html',
  styleUrls: ['../shell.css', './wiki.css'],
  providers: [WikiStore, PollingCoordinator],
})
export class WikiShell {
  protected readonly layout = inject(LayoutService);
  protected readonly store = inject(WikiStore);
  protected readonly projectContext = inject(ProjectContextService);
}
