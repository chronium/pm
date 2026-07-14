import { Component, inject } from '@angular/core';
import { RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';

import { LayoutService } from '../core/layout.service';

@Component({
  selector: 'pm-wiki-shell',
  imports: [RouterLink, RouterLinkActive, RouterOutlet],
  templateUrl: './wiki-shell.html',
  styleUrl: '../shell.css',
})
export class WikiShell {
  protected readonly layout = inject(LayoutService);
}
