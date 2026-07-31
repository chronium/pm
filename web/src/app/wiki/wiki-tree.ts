import { Component, computed, forwardRef, inject, input, OnDestroy, signal } from '@angular/core';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { cssChevronRight } from '@ng-icons/css.gg';
import { NavigationEnd, Router, RouterLink } from '@angular/router';
import { filter, Subscription } from 'rxjs';

import { LayoutService } from '../core/layout.service';
import { WikiStore, type WikiTreeNode } from './wiki.store';
import { ProjectContextService } from '../core/project-context.service';

@Component({
  selector: 'pm-wiki-tree',
  imports: [NgIcon, RouterLink, forwardRef(() => WikiTree)],
  providers: [provideIcons({ cssChevronRight })],
  template: ` <ul class="wiki-tree-list">
    @for (node of nodes(); track node.path) {
      <li>
        <div class="wiki-tree-row" [class.active]="active(node.path)">
          @if (node.children.length) {
            <button
              type="button"
              class="wiki-tree-toggle"
              [attr.aria-expanded]="expanded(node.path)"
              [attr.aria-label]="(expanded(node.path) ? 'Collapse ' : 'Expand ') + node.name"
              (click)="toggle(node.path)"
            >
              <ng-icon
                name="cssChevronRight"
                [class.expanded]="expanded(node.path)"
                aria-hidden="true"
              />
            </button>
          } @else {
            <span class="wiki-tree-spacer" aria-hidden="true"></span>
          }
          <a
            [routerLink]="projectContext.wikiUrl(node.path)"
            [attr.aria-current]="active(node.path) ? 'page' : null"
            [class.active]="active(node.path)"
            (click)="layout.closeMobileSidebar()"
            >{{ node.page?.title ?? node.name }}</a
          >
        </div>
        @if (node.children.length && expanded(node.path)) {
          <pm-wiki-tree [nodes]="node.children" />
        }
      </li>
    }
  </ul>`,
  styleUrl: './wiki-tree.css',
})
export class WikiTree implements OnDestroy {
  readonly nodes = input.required<readonly WikiTreeNode[]>();
  protected readonly layout = inject(LayoutService);
  private readonly store = inject(WikiStore);
  private readonly router = inject(Router);
  protected readonly projectContext = inject(ProjectContextService);
  private readonly currentUrl = signal(
    this.router.currentNavigation()?.finalUrl?.toString() ?? this.router.url,
  );
  private readonly selectedPath = computed(() => this.pathFromUrl(this.currentUrl()));
  private readonly navigationSubscription: Subscription = this.router.events
    .pipe(filter((event): event is NavigationEnd => event instanceof NavigationEnd))
    .subscribe(() => this.currentUrl.set(this.router.url));

  ngOnDestroy(): void {
    this.navigationSubscription.unsubscribe();
  }

  protected expanded(path: string): boolean {
    return this.expandedPaths().has(path) || this.selectedPath().startsWith(`${path}/`);
  }
  protected active(path: string): boolean {
    return this.selectedPath() === path;
  }
  protected toggle(path: string): void {
    const paths = this.expandedPaths();
    paths.has(path) ? paths.delete(path) : paths.add(path);
    try {
      sessionStorage.setItem(this.store.expansionKey(), JSON.stringify([...paths]));
    } catch {
      /* optional preference */
    }
  }

  private expandedPaths(): Set<string> {
    try {
      const parsed: unknown = JSON.parse(sessionStorage.getItem(this.store.expansionKey()) ?? '[]');
      return new Set(
        Array.isArray(parsed)
          ? parsed.filter((value): value is string => typeof value === 'string')
          : [],
      );
    } catch {
      return new Set();
    }
  }

  private pathFromUrl(url: string): string {
    const segments =
      this.router.parseUrl(url).root.children['primary']?.segments.map((segment) => segment.path) ??
      [];
    const rest = segments.slice(segments.indexOf('wiki') + 1);
    if (rest[0] === 'edit' || rest[0] === 'meta') rest.shift();
    return rest[0] === 'new' ? '' : rest.join('/');
  }
}
