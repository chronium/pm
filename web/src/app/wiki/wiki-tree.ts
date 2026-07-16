import { Component, forwardRef, inject, input } from '@angular/core';
import { Router, RouterLink } from '@angular/router';

import { LayoutService } from '../core/layout.service';
import { WikiStore, type WikiTreeNode } from './wiki.store';

@Component({
  selector: 'pm-wiki-tree',
  imports: [RouterLink, forwardRef(() => WikiTree)],
  template: `
    <ul class="wiki-tree-list">
      @for (node of nodes(); track node.path) {
        <li>
          <div class="wiki-tree-row">
            @if (node.children.length) {
              <button type="button" class="wiki-tree-toggle" [attr.aria-expanded]="expanded(node.path)"
                [attr.aria-label]="(expanded(node.path) ? 'Collapse ' : 'Expand ') + node.name"
                (click)="toggle(node.path)"><span aria-hidden="true">{{ expanded(node.path) ? '▾' : '▸' }}</span></button>
            } @else { <span class="wiki-tree-spacer" aria-hidden="true"></span> }
            <a [routerLink]="['/wiki', ...node.path.split('/')]" [attr.aria-current]="active(node.path) ? 'page' : null"
              [class.active]="active(node.path)" (click)="layout.closeMobileSidebar()">{{ node.page?.title ?? node.name }}</a>
          </div>
          @if (node.children.length && expanded(node.path)) { <pm-wiki-tree [nodes]="node.children" /> }
        </li>
      }
    </ul>`,
  styleUrl: './wiki.css',
})
export class WikiTree {
  readonly nodes = input.required<readonly WikiTreeNode[]>();
  protected readonly layout = inject(LayoutService);
  private readonly store = inject(WikiStore);
  private readonly router = inject(Router);

  protected expanded(path: string): boolean { return this.expandedPaths().has(path) || this.activePath().startsWith(`${path}/`); }
  protected active(path: string): boolean { return this.activePath() === path; }
  protected toggle(path: string): void {
    const paths = this.expandedPaths();
    paths.has(path) ? paths.delete(path) : paths.add(path);
    try { sessionStorage.setItem(this.store.expansionKey(), JSON.stringify([...paths])); } catch { /* optional preference */ }
  }

  private expandedPaths(): Set<string> {
    try {
      const parsed: unknown = JSON.parse(sessionStorage.getItem(this.store.expansionKey()) ?? '[]');
      return new Set(Array.isArray(parsed) ? parsed.filter((value): value is string => typeof value === 'string') : []);
    } catch { return new Set(); }
  }

  private activePath(): string {
    const segments = this.router.parseUrl(this.router.url).root.children['primary']?.segments.map((segment) => segment.path) ?? [];
    const rest = segments.slice(segments.indexOf('wiki') + 1);
    if (rest[0] === 'edit' || rest[0] === 'meta') rest.shift();
    return rest[0] === 'new' ? '' : rest.join('/');
  }
}
