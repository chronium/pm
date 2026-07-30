import { Component, inject } from '@angular/core';
import { RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';

import { LayoutService } from '../core/layout.service';

@Component({
  selector: 'pm-component-gallery-shell',
  imports: [RouterLink, RouterLinkActive, RouterOutlet],
  templateUrl: './component-gallery-shell.html',
  styleUrls: ['../shell.css', './component-gallery-shell.css'],
})
export class ComponentGalleryShell {
  protected readonly layout = inject(LayoutService);
}
