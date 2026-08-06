import {
  AfterViewInit,
  Component,
  effect,
  ElementRef,
  input,
  model,
  OnDestroy,
  output,
  signal,
  viewChild,
  ViewEncapsulation,
} from '@angular/core';
import type { FormValueControl } from '@angular/forms/signals';
import EasyMDE from 'easymde';

import { MarkdownService } from './markdown.service';
import { ProjectWikiLinkDialog } from './project-wiki-link-dialog';

@Component({
  selector: 'pm-markdown-editor',
  imports: [ProjectWikiLinkDialog],
  template: `
    <textarea
      #textarea
      [disabled]="disabled()"
      [attr.aria-label]="label()"
      [attr.placeholder]="placeholder() || null"
      (input)="fallbackInput($event)"
      (blur)="touch.emit()"
    ></textarea>
    @if (enableProjectWikiLinks()) {
      <pm-project-wiki-link-dialog
        [open]="linkPickerOpen()"
        [initialLabel]="selectedLinkText()"
        (inserted)="insertProjectWikiLink($event)"
        (dismissed)="linkPickerOpen.set(false)"
      />
    }
  `,
  styleUrl: './markdown-editor.css',
  encapsulation: ViewEncapsulation.None,
  host: { '[class.markdown-editor--external-preview]': 'externalPreview()' },
})
export class MarkdownEditor implements FormValueControl<string>, AfterViewInit, OnDestroy {
  readonly value = model.required<string>();
  readonly disabled = input(false);
  readonly label = input('Markdown description');
  readonly placeholder = input('');
  readonly externalPreview = input(false);
  readonly enableProjectWikiLinks = input(false);
  readonly touch = output<void>();
  private readonly textarea = viewChild<ElementRef<HTMLTextAreaElement>>('textarea');
  private readonly editor = signal<EasyMDE | null>(null);
  private syncing = false;
  protected readonly linkPickerOpen = signal(false);
  protected readonly selectedLinkText = signal('');

  constructor(private readonly renderer: MarkdownService) {
    effect(() => {
      const next = this.value();
      const editor = this.editor();
      if (editor && editor.value() !== next) {
        this.syncing = true;
        editor.value(next);
        this.syncing = false;
      } else if (!editor && this.textarea() && this.textarea()!.nativeElement.value !== next) {
        this.textarea()!.nativeElement.value = next;
      }
    });
    effect(() => {
      const editor = this.editor();
      if (editor) this.updateDisabled(editor);
    });
  }

  ngAfterViewInit(): void {
    try {
      const options: EasyMDE.Options = {
        element: this.textarea()!.nativeElement,
        initialValue: this.value(),
        autofocus: false,
        spellChecker: false,
        status: false,
        minHeight: '180px',
        placeholder: this.placeholder(),
        previewRender: (markdown) => this.renderer.render(markdown),
      };
      if (this.externalPreview() || this.enableProjectWikiLinks()) {
        options.toolbar = [
          'bold',
          'italic',
          'heading',
          '|',
          'quote',
          'unordered-list',
          'ordered-list',
          '|',
          'link',
          ...(this.enableProjectWikiLinks()
            ? [
                {
                  name: 'project-wiki-link',
                  action: () => this.openProjectWikiLinkPicker(),
                  className: 'pm-project-wiki-link',
                  title: 'Insert project wiki link',
                  icon: 'PM',
                } satisfies EasyMDE.ToolbarIcon,
              ]
            : []),
          'image',
          '|',
          'guide',
        ];
      }
      const editor = new EasyMDE(options);
      this.editor.set(editor);
      editor.codemirror.on('change', () => {
        if (!this.syncing) this.value.set(this.editor()?.value() ?? '');
      });
      editor.codemirror.on('blur', () => this.touch.emit());
      editor.codemirror.getInputField().setAttribute('aria-label', this.label());
      const container = editor.codemirror.getWrapperElement().closest('.EasyMDEContainer');
      container?.setAttribute('aria-label', this.label());
      container
        ?.querySelectorAll<HTMLButtonElement>('.editor-toolbar button')
        .forEach((button) => (button.tabIndex = 0));
      this.updateDisabled(editor);
      const scroll = editor.codemirror.getScrollerElement();
      scroll.tabIndex = 0;
      scroll.addEventListener('focus', () => editor.codemirror.focus());
    } catch {
      this.editor.set(null);
      this.textarea()!.nativeElement.value = this.value();
    }
  }

  ngOnDestroy(): void {
    this.editor()?.toTextArea();
    this.editor.set(null);
  }

  focus(options?: FocusOptions): void {
    if (this.editor()) this.editor()!.codemirror.focus();
    else this.textarea()?.nativeElement.focus(options);
  }

  protected fallbackInput(event: Event): void {
    if (!this.editor()) this.value.set((event.target as HTMLTextAreaElement).value);
  }

  protected insertProjectWikiLink(markdown: string): void {
    const editor = this.editor();
    if (editor) {
      editor.codemirror.replaceSelection(markdown);
      editor.codemirror.focus();
    } else {
      this.value.update((value) => `${value}${markdown}`);
    }
    this.linkPickerOpen.set(false);
  }

  private openProjectWikiLinkPicker(): void {
    const editor = this.editor();
    this.selectedLinkText.set(editor?.codemirror.getSelection() ?? '');
    this.linkPickerOpen.set(true);
  }

  private updateDisabled(editor: EasyMDE): void {
    const disabled = this.disabled();
    editor.codemirror.setOption('readOnly', disabled ? 'nocursor' : false);
    editor.codemirror
      .getWrapperElement()
      .closest('.EasyMDEContainer')
      ?.querySelectorAll<HTMLButtonElement>('.editor-toolbar button')
      .forEach((button) => (button.disabled = disabled));
  }
}
