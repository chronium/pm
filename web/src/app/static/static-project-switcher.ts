import { AsyncPipe } from '@angular/common';
import { Component, ElementRef, HostListener, inject } from '@angular/core';
import { RouterLink } from '@angular/router';
import { cssChevronRight } from '@ng-icons/css.gg';
import { NgIcon, provideIcons } from '@ng-icons/core';

import { StaticSnapshotStore } from './static-snapshot.interceptor';

@Component({
  selector: 'pm-static-project-switcher',
  imports: [AsyncPipe, RouterLink, NgIcon],
  providers: [provideIcons({ cssChevronRight })],
  templateUrl: './static-project-switcher.html',
  styleUrl: './static-project-switcher.css',
})
export class StaticProjectSwitcher {
  private readonly element = inject<ElementRef<HTMLElement>>(ElementRef);
  protected readonly snapshot = inject(StaticSnapshotStore).snapshot;

  @HostListener('document:pointerdown', ['$event'])
  protected closeOnOutsidePointer(event: PointerEvent): void {
    const details =
      this.element.nativeElement.querySelector<HTMLDetailsElement>('.project-switcher');
    if (details?.open && !event.composedPath().includes(this.element.nativeElement)) {
      details.open = false;
    }
  }
}
