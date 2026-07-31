import { Component, ElementRef, HostListener, inject } from '@angular/core';
import { RouterLink } from '@angular/router';
import { cssChevronRight } from '@ng-icons/css.gg';
import { NgIcon, provideIcons } from '@ng-icons/core';

import { ProjectContextService } from '../core/project-context.service';

@Component({
  selector: 'pm-project-switcher',
  imports: [RouterLink, NgIcon],
  providers: [provideIcons({ cssChevronRight })],
  templateUrl: './project-switcher.html',
  styleUrl: './project-switcher.css',
})
export class ProjectSwitcher {
  private readonly element = inject<ElementRef<HTMLElement>>(ElementRef);
  protected readonly context = inject(ProjectContextService);

  constructor() {
    this.context.enableFamilyMetadata();
  }

  protected close(details: HTMLDetailsElement): void {
    details.open = false;
  }

  @HostListener('document:pointerdown', ['$event'])
  protected closeOnOutsidePointer(event: PointerEvent): void {
    const details =
      this.element.nativeElement.querySelector<HTMLDetailsElement>('.project-switcher');
    if (details?.open && !event.composedPath().includes(this.element.nativeElement))
      details.open = false;
  }

  @HostListener('document:keydown.escape')
  protected closeOnEscape(): void {
    const details =
      this.element.nativeElement.querySelector<HTMLDetailsElement>('.project-switcher');
    if (details?.open) details.open = false;
  }
}
