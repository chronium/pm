import {
  Component,
  effect,
  ElementRef,
  input,
  model,
  OnDestroy,
  output,
  signal,
  viewChildren,
} from '@angular/core';
import type { FormValueControl } from '@angular/forms/signals';

import { MarkdownDisplay } from '../markdown/markdown-display';
import { MarkdownEditor } from '../markdown/markdown-editor';

type WikiWorkspacePane = 'editor' | 'preview';

@Component({
  selector: 'pm-wiki-markdown-workspace',
  imports: [MarkdownDisplay, MarkdownEditor],
  template: `
    <div class="wiki-workspace-tabs" role="tablist" aria-label="Markdown workspace view">
      <button
        #tab
        id="wiki-workspace-editor-tab"
        type="button"
        role="tab"
        [attr.aria-selected]="activePane() === 'editor'"
        aria-controls="wiki-workspace-editor"
        [tabIndex]="activePane() === 'editor' ? 0 : -1"
        (click)="selectPane('editor')"
        (keydown)="moveTab($event, 'editor')"
      >
        Editor
      </button>
      <button
        #tab
        id="wiki-workspace-preview-tab"
        type="button"
        role="tab"
        [attr.aria-selected]="activePane() === 'preview'"
        aria-controls="wiki-workspace-preview"
        [tabIndex]="activePane() === 'preview' ? 0 : -1"
        (click)="selectPane('preview')"
        (keydown)="moveTab($event, 'preview')"
      >
        Preview
      </button>
    </div>
    <div class="wiki-workspace-panes">
      <section
        #editorPane
        id="wiki-workspace-editor"
        class="wiki-workspace-pane wiki-workspace-editor"
        [class.wiki-workspace-pane--mobile-hidden]="activePane() !== 'editor'"
        role="tabpanel"
        aria-labelledby="wiki-workspace-editor-tab"
      >
        <h2 class="wiki-workspace-pane-title">Editor</h2>
        <pm-markdown-editor
          [(value)]="value"
          [disabled]="disabled()"
          [label]="label()"
          [externalPreview]="true"
          [enableProjectWikiLinks]="true"
          (touch)="touch.emit()"
        />
      </section>
      <section
        #previewPane
        id="wiki-workspace-preview"
        class="wiki-workspace-pane wiki-workspace-preview"
        [class.wiki-workspace-pane--mobile-hidden]="activePane() !== 'preview'"
        role="tabpanel"
        aria-labelledby="wiki-workspace-preview-tab"
        tabindex="0"
      >
        <h2 class="wiki-workspace-pane-title">Preview</h2>
        @if (previewMarkdown().trim()) {
          <pm-markdown-display [markdown]="previewMarkdown()" />
        } @else {
          <p class="wiki-workspace-empty">Nothing to preview yet.</p>
        }
      </section>
    </div>
  `,
  styleUrl: './wiki-markdown-workspace.css',
})
export class WikiMarkdownWorkspace implements FormValueControl<string>, OnDestroy {
  readonly value = model.required<string>();
  readonly disabled = input(false);
  readonly label = input('Wiki page Markdown body');
  readonly touch = output<void>();
  protected readonly activePane = signal<WikiWorkspacePane>('editor');
  protected readonly previewMarkdown = signal('');
  private readonly tabs = viewChildren<ElementRef<HTMLButtonElement>>('tab');
  private readonly editorPane = viewChildren<ElementRef<HTMLElement>>('editorPane');
  private readonly previewPane = viewChildren<ElementRef<HTMLElement>>('previewPane');
  private readonly scrollPositions: Record<WikiWorkspacePane, number> = {
    editor: 0,
    preview: 0,
  };
  private previewTimer: ReturnType<typeof setTimeout> | null = null;

  constructor() {
    effect(() => {
      const markdown = this.value();
      if (this.previewTimer !== null) clearTimeout(this.previewTimer);
      this.previewTimer = setTimeout(() => {
        this.previewMarkdown.set(markdown);
        this.previewTimer = null;
      }, 120);
    });
  }

  ngOnDestroy(): void {
    if (this.previewTimer !== null) clearTimeout(this.previewTimer);
  }

  protected selectPane(pane: WikiWorkspacePane, focus = false): void {
    const current = this.activePane();
    this.scrollPositions[current] = this.paneScroller(current)?.scrollTop ?? 0;
    this.activePane.set(pane);
    if (focus) {
      const index = pane === 'editor' ? 0 : 1;
      this.tabs()[index]?.nativeElement.focus();
    }
    setTimeout(() => {
      const scroller = this.paneScroller(pane);
      if (scroller) scroller.scrollTop = this.scrollPositions[pane];
    }, 0);
  }

  protected moveTab(event: KeyboardEvent, current: WikiWorkspacePane): void {
    let next: WikiWorkspacePane | null = null;
    if (event.key === 'ArrowRight' || event.key === 'ArrowDown')
      next = current === 'editor' ? 'preview' : 'editor';
    else if (event.key === 'ArrowLeft' || event.key === 'ArrowUp')
      next = current === 'preview' ? 'editor' : 'preview';
    else if (event.key === 'Home') next = 'editor';
    else if (event.key === 'End') next = 'preview';
    if (!next) return;
    event.preventDefault();
    this.selectPane(next, true);
  }

  private paneScroller(pane: WikiWorkspacePane): HTMLElement | null {
    const host =
      pane === 'editor'
        ? this.editorPane()[0]?.nativeElement
        : this.previewPane()[0]?.nativeElement;
    return pane === 'editor'
      ? (host?.querySelector<HTMLElement>('.CodeMirror-scroll') ?? host ?? null)
      : (host ?? null);
  }
}
